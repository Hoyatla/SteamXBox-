using System.Diagnostics;
using System.Globalization;
using System.Text;
using SteamXBox.Tools.Agrandissement;

namespace SteamXBox.Tools.Video;

/// <summary>
/// Agrandit une vidéo, et change sa cadence, sans interpréteur.
/// </summary>
/// <remarks>
/// Remplace un script Python qui faisait la même chose. Les deux moteurs appelés ici sont des
/// binaires compilés et autonomes, livrés avec le produit sous licences permissives ; le script,
/// lui, réclamait un Python sur la machine — neuf gigaoctets et demi pour orchestrer trois appels
/// de processus.
///
/// <para>
/// <b>Pourquoi par tronçons.</b> Les moteurs ne lisent pas une vidéo, seulement des fichiers image.
/// Mesuré sur un film de 80 minutes en 720p : la chaîne naïve réclamerait 572 Go d'images
/// intermédiaires. Découper, traiter, encoder, effacer, avancer — le disque reste borné à quelques
/// gigaoctets, et le découpage ne coûte qu'une seconde par tronçon.
/// </para>
///
/// <para>
/// <b>Ce que les mesures ont tranché.</b> L'agrandissement passe par ncnn plutôt que par PyTorch :
/// 28 s contre 84 s pour 300 images en 720p, soit trois fois plus rapide. L'encodage reste sur le
/// processeur — NVENC a été essayé et rendu, 94 s contre 65 s, car le circuit d'encodage se dispute
/// la carte avec le calcul.
/// </para>
/// </remarks>
public static class VideoUpscale
{
    /// <summary>
    /// Durée d'un tronçon. Trente secondes de 720p tiennent dans trois gigaoctets d'images.
    /// </summary>
    private const int TronconSecondes = 30;

    /// <summary>En dessous, une cadence ne produit plus un mouvement, mais un diaporama.</summary>
    private const int CadenceMinimale = 16;

    /// <summary>
    /// Un écart plus petit que celui-ci désigne la cadence du film, pas une conversion.
    /// </summary>
    /// <remarks>
    /// Beaucoup de films sont en 23,976 et non en 24. Sans cette tolérance, choisir « 24 » sur un
    /// tel film introduirait un écart d'un millième — soit près de quatre secondes de décalage du
    /// son sur une heure et demie.
    /// </remarks>
    private const double ToleranceCadence = 0.01;

    /// <summary>Les conteneurs qui savent porter du H.264 sans réencoder.</summary>
    /// <remarks>
    /// webm n'en fait pas partie : il n'accepte que VP8, VP9 et AV1. L'y recopier échoue, et une
    /// liste qui propose un format impossible est un piège plutôt qu'un choix.
    /// </remarks>
    private static readonly string[] ConteneursCompatibles = ["mp4", "mkv", "mov", "avi"];

    /// <summary>
    /// Agrandit la vidéo désignée et écrit le résultat à côté d'elle.
    /// </summary>
    /// <param name="video">Le fichier désigné par l'utilisateur.</param>
    /// <param name="modele">Famille de modèle ; l'échelle y est ajoutée si elle existe.</param>
    /// <param name="echelle">2, 3 ou 4.</param>
    /// <param name="fps">Cadence voulue, ou « 0 » pour conserver celle du film.</param>
    /// <param name="qualite">CRF x264 : 18 très haute, 23 équilibrée, 28 légère.</param>
    /// <param name="format">Conteneur de sortie.</param>
    /// <param name="journal">Reçoit l'avancement, tronçon par tronçon.</param>
    public static string Agrandir(
        string video,
        string modele,
        string echelle,
        string fps,
        string qualite,
        string format,
        Action<string>? journal)
    {
        if (!File.Exists(video))
        {
            return "La vidéo choisie est introuvable.";
        }

        var dossier = Path.GetDirectoryName(video);

        if (string.IsNullOrEmpty(dossier))
        {
            return "La vidéo choisie n'a pas de dossier.";
        }

        if (!File.Exists(Moteur.Agrandisseur))
        {
            return $"Moteur d'agrandissement absent : {Moteur.Agrandisseur}";
        }

        var ffmpeg = Moteur.Trouver("ffmpeg");
        var ffprobe = Moteur.Trouver("ffprobe");

        if (ffmpeg is null || ffprobe is null)
        {
            return "ffmpeg est introuvable. Il porte le décodage et l'encodage de la vidéo.";
        }

        if (!Array.Exists(ConteneursCompatibles, c => c.Equals(format, StringComparison.OrdinalIgnoreCase)))
        {
            format = "mp4";
        }

        if (Moteur.ResoudreModele(ref modele, ref echelle, journal) is { } manquant)
        {
            return manquant;
        }

        var cadenceSource = Cadence(ffprobe, video);

        if (cadenceSource <= 0)
        {
            return "Impossible de lire la cadence de cette vidéo.";
        }

        var cadenceVoulue = CadenceVoulue(fps, cadenceSource, journal);

        if (cadenceVoulue > cadenceSource && !File.Exists(Moteur.Interpolateur))
        {
            return $"Augmenter la cadence demande le moteur d'interpolation, absent de : {Moteur.Interpolateur}";
        }

        var nom = Path.GetFileNameWithoutExtension(video);
        // Réservé plutôt que nommé : le dossier est effacé récursivement à la fin, et un dossier de
        // l'utilisateur qui portait déjà ce nom partait avec. Voir Moteur.Reserver.
        var travail = Moteur.Reserver(Path.Combine(dossier, $"{nom}_travail"));
        var final = Path.Combine(dossier, $"{nom}_out.{format}");

        try
        {
            return Traiter(
                new Chaine(ffmpeg, ffprobe, video, travail, final, format, nom),
                modele, echelle, qualite, cadenceSource, cadenceVoulue, journal);
        }
        catch (IOException erreur)
        {
            return $"Écriture impossible : {erreur.Message}";
        }
        catch (UnauthorizedAccessException erreur)
        {
            return $"Accès refusé : {erreur.Message}";
        }
        finally
        {
            Moteur.Effacer(travail);
        }
    }

    /// <summary>Ce qui ne change pas d'un tronçon à l'autre, réuni pour ne pas le repasser.</summary>
    private sealed record Chaine(
        string Ffmpeg,
        string Ffprobe,
        string Video,
        string Travail,
        string Final,
        string Format,
        string Nom);

    private static string Traiter(
        Chaine chaine,
        string modele,
        string echelle,
        string qualite,
        double cadenceSource,
        double cadenceVoulue,
        Action<string>? journal)
    {
        var parts = Path.Combine(chaine.Travail, "parts");
        var finis = Path.Combine(chaine.Travail, "finis");

        Directory.CreateDirectory(parts);
        Directory.CreateDirectory(finis);

        // Découpe sans réencodage : alignée sur les images clés, aucune image perdue. Vérifié par
        // comptage — 181 + 100 + 19 pour 300 images à l'entrée.
        if (!Moteur.Lancer(chaine.Ffmpeg, out var refus,
                "-y", "-v", "error", "-i", chaine.Video, "-an", "-c", "copy",
                "-f", "segment", "-segment_time", TronconSecondes.ToString(CultureInfo.InvariantCulture),
                Path.Combine(parts, "%05d.mp4")))
        {
            return $"Découpage impossible : {refus}";
        }

        var morceaux = Directory.GetFiles(parts, "*.mp4");
        Array.Sort(morceaux, StringComparer.Ordinal);

        if (morceaux.Length == 0)
        {
            return "Le découpage n'a produit aucun tronçon.";
        }

        journal?.Invoke($"{morceaux.Length} tronçon(s) à traiter.");

        var depart = Stopwatch.StartNew();

        for (var i = 0; i < morceaux.Length; i++)
        {
            var suivant = i + 1 < morceaux.Length ? morceaux[i + 1] : null;

            if (Troncon(chaine, morceaux[i], suivant, finis, modele, echelle, qualite,
                    cadenceSource, cadenceVoulue) is { } probleme)
            {
                return probleme;
            }

            var ecoule = depart.Elapsed.TotalSeconds;
            var reste = ecoule / (i + 1) * (morceaux.Length - i - 1);

            journal?.Invoke(
                $"tronçon {i + 1}/{morceaux.Length} — {ecoule:F0} s écoulées, reste environ {reste:F0} s.");
        }

        return Rassembler(chaine, finis);
    }

    /// <summary>Un tronçon : extraire, cadencer, agrandir, encoder.</summary>
    private static string? Troncon(
        Chaine chaine,
        string morceau,
        string? suivant,
        string finis,
        string modele,
        string echelle,
        string qualite,
        double cadenceSource,
        double cadenceVoulue)
    {
        var brut = Moteur.Neuf(Path.Combine(chaine.Travail, "brut"));
        var agrandi = Moteur.Neuf(Path.Combine(chaine.Travail, "agrandi"));

        var extraction = new List<string> { "-y", "-v", "error", "-i", morceau };

        if (cadenceVoulue < cadenceSource)
        {
            // Réduction ici, avant tout calcul : les images jetées ne sont jamais agrandies, donc
            // le temps baisse d'autant. Le filtre « fps » convertit en préservant la durée ; « -r »
            // la déforme — mesuré, il allongeait un clip de 2,002 s à 2,167 s.
            extraction.Add("-vf");
            extraction.Add("fps=" + cadenceVoulue.ToString("G", CultureInfo.InvariantCulture));
        }

        extraction.Add(Path.Combine(brut, "f%06d.png"));

        if (!Moteur.Lancer(chaine.Ffmpeg, out var refus, [.. extraction]))
        {
            return $"Extraction impossible : {refus}";
        }

        var source = brut;
        var motif = "f%06d.png";

        if (cadenceVoulue > cadenceSource)
        {
            if (Interpoler(chaine, brut, suivant, cadenceSource, cadenceVoulue) is { } echec)
            {
                return echec;
            }

            source = Path.Combine(chaine.Travail, "interp");
            motif = "%08d.png";
        }

        if (!Moteur.Lancer(Moteur.Agrandisseur, out var plainte,
                "-i", source, "-o", agrandi, "-n", modele, "-s", echelle, "-f", "png", "-j", Moteur.Threads))
        {
            return $"Agrandissement impossible : {plainte}";
        }

        var cadence = Math.Abs(cadenceVoulue - cadenceSource) < double.Epsilon
            ? cadenceSource
            : cadenceVoulue;

        if (!Moteur.Lancer(chaine.Ffmpeg, out var souci,
                "-y", "-v", "error",
                "-framerate", cadence.ToString("G", CultureInfo.InvariantCulture),
                "-i", Path.Combine(agrandi, motif),
                "-c:v", "libx264", "-crf", qualite, "-pix_fmt", "yuv420p",
                Path.Combine(finis, Path.GetFileName(morceau))))
        {
            return $"Encodage impossible : {souci}";
        }

        Moteur.Effacer(brut);
        Moteur.Effacer(agrandi);
        Moteur.Effacer(Path.Combine(chaine.Travail, "interp"));

        return null;
    }

    /// <summary>
    /// Fabrique les images manquantes, avant l'agrandissement.
    /// </summary>
    /// <remarks>
    /// <b>L'image de raccord.</b> Sans elle, les images intermédiaires qui enjambent la jointure
    /// entre deux tronçons n'existeraient jamais : un micro-saut toutes les trente secondes. La
    /// première image du tronçon suivant sert de point d'appui, puis la queue qui ne nous appartient
    /// pas est jetée.
    ///
    /// <para>
    /// L'interpolateur restitue les images d'origine au pixel près — vérifié, écart nul sur tous les
    /// instants coïncidents. Il n'invente que l'entre-deux.
    /// </para>
    /// </remarks>
    private static string? Interpoler(
        Chaine chaine,
        string brut,
        string? suivant,
        double cadenceSource,
        double cadenceVoulue)
    {
        var images = Directory.GetFiles(brut, "*.png").Length;

        if (images == 0)
        {
            return "Ce tronçon ne contient aucune image.";
        }

        var pont = 0;

        if (suivant is not null)
        {
            if (!Moteur.Lancer(chaine.Ffmpeg, out var refus,
                    "-y", "-v", "error", "-i", suivant, "-frames:v", "1",
                    Path.Combine(brut, $"f{images + 1:D6}.png")))
            {
                return $"Image de raccord impossible : {refus}";
            }

            pont = 1;
        }

        var garder = (int)Math.Round(images * cadenceVoulue / cadenceSource);
        var vise = (int)Math.Round((images + pont) * cadenceVoulue / cadenceSource);

        var interp = Moteur.Neuf(Path.Combine(chaine.Travail, "interp"));

        if (!Moteur.Lancer(Moteur.Interpolateur, out var plainte,
                "-i", brut, "-o", interp, "-m", Moteur.ModeleInterpolation,
                "-n", vise.ToString(CultureInfo.InvariantCulture), "-j", Moteur.Threads))
        {
            return $"Interpolation impossible : {plainte}";
        }

        var produites = Directory.GetFiles(interp, "*.png");
        Array.Sort(produites, StringComparer.Ordinal);

        for (var k = garder; k < produites.Length; k++)
        {
            File.Delete(produites[k]);
        }

        return null;
    }

    /// <summary>Recolle les tronçons et remet le son d'origine d'un seul bloc.</summary>
    /// <remarks>
    /// Le son n'est jamais découpé : il est remis entier à la fin, donc aucun raccord ne peut le
    /// décaler. Et pas de <c>-shortest</c> : il tronquait la vidéo à la longueur de la piste audio
    /// et mangeait deux images sur trois cents — une demi-minute de désynchronisation sur un film.
    /// </remarks>
    private static string Rassembler(Chaine chaine, string finis)
    {
        var morceaux = Directory.GetFiles(finis, "*.mp4");
        Array.Sort(morceaux, StringComparer.Ordinal);

        var liste = Path.Combine(chaine.Travail, "liste.txt");
        var contenu = new StringBuilder();

        foreach (var morceau in morceaux)
        {
            contenu.Append("file '").Append(morceau.Replace('\\', '/')).Append('\'').Append('\n');
        }

        File.WriteAllText(liste, contenu.ToString());

        var arguments = new List<string>
        {
            "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", liste,
        };

        if (AvecSon(chaine.Ffprobe, chaine.Video))
        {
            arguments.AddRange(["-i", chaine.Video, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "copy"]);
        }
        else
        {
            arguments.AddRange(["-c", "copy"]);
        }

        arguments.Add(chaine.Final);

        return Moteur.Lancer(chaine.Ffmpeg, out var refus, [.. arguments])
            ? $"Terminé : {chaine.Final}"
            : $"Assemblage impossible : {refus}";
    }

    /// <summary>La cadence demandée, ramenée à ce qui a un sens.</summary>
    private static double CadenceVoulue(string demande, double source, Action<string>? journal)
    {
        if (demande.Length == 0
            || demande.Equals("source", StringComparison.OrdinalIgnoreCase)
            || !double.TryParse(demande, NumberStyles.Float, CultureInfo.InvariantCulture, out var voulue)
            || voulue <= 0)
        {
            return source;
        }

        if (voulue < CadenceMinimale)
        {
            journal?.Invoke($"Cadence trop basse, ramenée à {CadenceMinimale}.");
            voulue = CadenceMinimale;
        }

        return Math.Abs(voulue - source) / source < ToleranceCadence ? source : voulue;
    }

    private static double Cadence(string ffprobe, string video)
    {
        var lu = Moteur.Lire(ffprobe,
            "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=r_frame_rate",
            "-of", "default=nw=1:nk=1", video).Trim();

        var barre = lu.IndexOf('/');

        if (barre < 0)
        {
            return double.TryParse(lu, NumberStyles.Float, CultureInfo.InvariantCulture, out var seul)
                ? seul
                : 0;
        }

        return double.TryParse(lu[..barre], NumberStyles.Float, CultureInfo.InvariantCulture, out var haut)
               && double.TryParse(lu[(barre + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var bas)
               && bas > 0
            ? haut / bas
            : 0;
    }

    private static bool AvecSon(string ffprobe, string video)
        => Moteur.Lire(ffprobe,
            "-v", "error", "-select_streams", "a", "-show_entries", "stream=codec_type",
            "-of", "csv=p=0", video).Trim().Length > 0;
}
