using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SenSÉ.Tools.Generation;

/// <summary>
/// Anime chaque image d'un dossier, et met les clips bout à bout.
/// </summary>
/// <remarks>
/// <b>Ce que la mesure autorise, et pas davantage.</b> Un clip SVD tient deux secondes et demie ;
/// repartir de sa dernière image en donne quatre et demie, encore nettes ; un troisième maillon
/// casse — visage perdu, anatomie brisée, le fichier grossit de bruit. La borne est donc à deux
/// maillons, et elle est mesurée, pas devinée.
///
/// <para>
/// <b>Le pont le plus court possible.</b> Chaque image du dossier est un point choisi par
/// l'utilisateur ; ce que le modèle invente entre deux points est ce qu'il dérive tout seul, sans
/// viser le point suivant — ni SVD ni aucun modèle d'aujourd'hui ne sait rejoindre une image cible.
/// Plus le pont est court, moins la dérive a le temps de s'installer, et plus la coupe vers l'image
/// suivante passe inaperçue. D'où le défaut à un seul maillon.
/// </para>
///
/// <para>
/// <b>Le montage se fait au niveau des fichiers</b>, par le démultiplexeur <c>concat</c> de ffmpeg,
/// sans réencoder : les clips sortent du même flux avec les mêmes réglages, donc se recollent
/// sans perte. Réencoder aurait dégradé une seconde fois ce que la génération avait déjà coûté.
/// </para>
/// </remarks>
public static class SequenceAnimee
{
    /// <summary>
    /// Le nombre de maillons au-delà duquel la chaîne se dévore.
    /// </summary>
    /// <remarks>
    /// Trois maillons ont été générés et regardés : le troisième était inutilisable. Cette borne
    /// n'est pas une prudence, c'est le résultat.
    /// </remarks>
    public const int PontsMaximum = 2;

    /// <summary>Les extensions d'image que le générateur sait charger.</summary>
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI");

    private static string Sorties => Path.Combine(Racine, "output");

    /// <summary>
    /// Les images d'un dossier, dans l'ordre où on les animera.
    /// </summary>
    /// <remarks>
    /// Trié par nom, et c'est le seul ordre qu'un utilisateur puisse prévoir : il renomme ses
    /// fichiers <c>01</c>, <c>02</c>, <c>03</c> et s'attend à les voir défiler ainsi. Trier par date
    /// aurait suivi l'ordre de copie, que personne ne contrôle.
    /// </remarks>
    public static IReadOnlyList<string> Ordonner(string dossier)
    {
        if (!Directory.Exists(dossier))
        {
            return [];
        }

        return [.. Directory.GetFiles(dossier)
            .Where(f => Images.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Le fichier que <c>ffmpeg -f concat</c> attend.
    /// </summary>
    /// <remarks>
    /// Les apostrophes sont doublées selon la convention du démultiplexeur : un chemin qui en
    /// contient — et « Modspack perso » n'en est pas loin — couperait la liste en deux et ffmpeg
    /// se plaindrait d'un fichier introuvable dont le nom serait la moitié du vrai.
    /// </remarks>
    public static string Liste(IEnumerable<string> clips)
    {
        var texte = new StringBuilder();

        foreach (var clip in clips)
        {
            texte.Append("file '").Append(clip.Replace("'", "'\\''", StringComparison.Ordinal))
                .Append('\'').Append('\n');
        }

        return texte.ToString();
    }

    /// <summary>
    /// Copie un dossier d'images dans les entrées du générateur et rend le nom à employer.
    /// </summary>
    /// <param name="source">Le dossier de l'utilisateur, n'importe où sur ses disques.</param>
    /// <param name="journal">Reçoit le détail de ce qui a été copié.</param>
    /// <returns>
    /// Le nom du dossier sous <c>input</c>, celui que <see cref="Lancer"/> attend — ou un refus qui
    /// commence par « Impossible » et explique quoi faire.
    /// </returns>
    /// <remarks>
    /// <b>Ce qui manquait entre le chemin donné et le travail lancé.</b> L'assistant demandait un
    /// dossier à l'utilisateur, le recevait, et n'en faisait rien : le générateur ne lit que sous
    /// <c>Outils\ComfyUI\input</c>, et un chemin comme <c>D:\mes photos</c> n'y désigne rien. Le
    /// dépôt existait pour les fichiers isolés d'un flux, jamais pour un dossier entier — c'est-à-dire
    /// jamais pour le seul cas où l'utilisateur en a un.
    ///
    /// <para>
    /// <b>Le nom est préfixé, comme pour les fichiers, et pour la même raison.</b> Le dossier
    /// <c>input</c> appartient au serveur et l'utilisateur y a peut-être les siens ; recouvrir son
    /// <c>photos</c> parce qu'il en a un du même nom serait une perte silencieuse.
    /// </para>
    ///
    /// <para>
    /// Rien n'est effacé de ce qui s'y trouvait déjà, et un fichier identique n'est pas recopié :
    /// redéposer le même dossier deux fois est une opération sans effet, ce qui rend un second essai
    /// gratuit au lieu de dangereux.
    /// </para>
    /// </remarks>
    public static string Deposer(string source, Action<string>? journal)
    {
        var propre = (source ?? "").Trim().Trim('"');

        if (propre.Length == 0 || !Directory.Exists(propre))
        {
            return $"Impossible : « {propre} » n'est pas un dossier existant. "
                + "Demandez à l'utilisateur le dossier qui contient ses images.";
        }

        var images = Ordonner(propre);

        if (images.Count == 0)
        {
            return $"Impossible : aucune image dans « {propre} ». "
                + "Les formats lus sont PNG, JPG, JPEG, WEBP et BMP.";
        }

        var nom = "SenSÉ-" + Nommer(Path.GetFileName(propre.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        var vers = Path.Combine(Racine, "input", nom);

        var copiees = 0;

        try
        {
            Directory.CreateDirectory(vers);

            foreach (var image in images)
            {
                var arrivee = Path.Combine(vers, Path.GetFileName(image));

                if (Identiques(image, arrivee))
                {
                    continue;
                }

                File.Copy(image, arrivee, overwrite: true);
                copiees++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // On renonce au lieu de rendre un nom à moitié rempli : une séquence animée à partir
            // d'un dossier incomplet produirait une vidéo à laquelle il manque des images, et rien
            // dans le résultat ne dirait laquelle ni pourquoi.
            journal?.Invoke($"dépôt du dossier interrompu : {exception.Message}");

            return $"Impossible : la copie s'est arrêtée à {exception.Message} "
                + "Le dossier est peut-être en cours d'utilisation.";
        }

        journal?.Invoke(
            $"{copiees.ToString(CultureInfo.InvariantCulture)} image(s) copiée(s) vers input\\{nom}, "
            + $"{images.Count.ToString(CultureInfo.InvariantCulture)} au total.");

        return nom;
    }

    /// <summary>Réduit un nom de dossier à ce qu'un système de fichiers accepte partout.</summary>
    private static string Nommer(string brut)
    {
        var propre = new StringBuilder();

        foreach (var c in brut.Trim())
        {
            propre.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        var reduit = propre.ToString().Trim('-');

        return reduit.Length == 0 ? "images" : reduit;
    }

    /// <summary>Le même fichier, déjà déposé ?</summary>
    private static bool Identiques(string source, string depose)
    {
        try
        {
            if (!File.Exists(depose))
            {
                return false;
            }

            var un = new FileInfo(source);
            var deux = new FileInfo(depose);

            return un.Length == deux.Length && un.LastWriteTimeUtc == deux.LastWriteTimeUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Anime chaque image du dossier et rend une seule vidéo.</summary>
    /// <param name="cible">
    /// <c>dossier|ponts|mouvement|rendu|prefixe</c> — le dossier d'images sous les entrées du
    /// générateur, le nombre de maillons par image, et les réglages passés à chaque animation.
    /// </param>
    public static string Lancer(string cible, Action<string>? journal)
    {
        // Sur les barres non échappées : le préfixe est un texte libre, et une barre tapée là
        // décalerait tous les réglages d'un cran. Voir SenSÉ.Plugins.Segments.
        var morceaux = SenSÉ.Plugins.Segments.Decouper(cible);

        if (morceaux.Count < 5 || morceaux[0].Trim().Length == 0)
        {
            return "Choisissez un dossier d'images, puis lancez.";
        }

        var dossier = Path.Combine(Racine, "input", morceaux[0].Trim());
        var ponts = Math.Clamp(Lire(morceaux[1], 1), 1, PontsMaximum);
        var mouvement = Lire(morceaux[2], 127);
        var rendu = Lire(morceaux[3], 20);
        var prefixe = morceaux[4].Trim().Length > 0 ? morceaux[4].Trim() : "sequence";

        // Le graphe vient du manifeste, plus du code.
        //
        // Il était écrit ici, en dur : « SVD_Image_to_Video.json », avec les numéros de ses nœuds
        // dans la ligne suivante. Ce fichier contredisait à lui seul la règle que le dépôt répète
        // partout — le manifeste décrit, l'hôte exécute — et il condamnait la séquence animée à
        // SVD pour toujours : changer de moteur demandait de recompiler le produit.
        //
        // Absent de la cible, l'ancien graphe est repris : un manifeste écrit avant ce changement
        // continue de fonctionner, il est simplement figé comme il l'était.
        var flux = morceaux.Count > 5 && morceaux[5].Trim().Length > 0
            ? morceaux[5].Trim()
            : Path.Combine(AppContext.BaseDirectory, "Flux", "SVD_Image_to_Video.json");

        var images = Ordonner(dossier);

        if (images.Count == 0)
        {
            return $"Aucune image dans {dossier}.";
        }

        if (ComfyServer.Preparer(journal) is { } absent)
        {
            return absent;
        }

        journal?.Invoke($"{images.Count} images à animer, {ponts} maillon(s) chacune. "
            + $"Compter environ {(images.Count * ponts * 5).ToString(CultureInfo.InvariantCulture)} minutes.");

        var clips = new List<string>();
        var rang = 0;

        foreach (var image in images)
        {
            rang++;

            if (Animer(image, flux, ponts, mouvement, rendu, prefixe, rang, images.Count, journal, clips)
                is { } refus)
            {
                return refus;
            }
        }

        return Coller(clips, prefixe, journal);
    }

    /// <summary>Anime une image, en un ou deux maillons enchaînés.</summary>
    private static string? Animer(
        string image,
        string flux,
        int ponts,
        int mouvement,
        int rendu,
        string prefixe,
        int rang,
        int combien,
        Action<string>? journal,
        List<string> clips)
    {
        var depart = image;

        for (var maillon = 1; maillon <= ponts; maillon++)
        {
            var nom = $"{prefixe}-{rang.ToString("D3", CultureInfo.InvariantCulture)}"
                + $"-{maillon.ToString(CultureInfo.InvariantCulture)}";

            journal?.Invoke($"Image {rang.ToString(CultureInfo.InvariantCulture)}"
                + $"/{combien.ToString(CultureInfo.InvariantCulture)}, "
                + $"maillon {maillon.ToString(CultureInfo.InvariantCulture)} : "
                + $"{Path.GetFileName(depart)}");

            var reglages = $"{flux}|!2.image={depart}"
                + $"|4.video_frames=14|4.motion_bucket_id={mouvement.ToString(CultureInfo.InvariantCulture)}"
                + $"|6.steps={rendu.ToString(CultureInfo.InvariantCulture)}"
                + $"|6.seed={(rang * 100 + maillon).ToString(CultureInfo.InvariantCulture)}"
                + $"|9.filename_prefix={nom}";

            var dit = FluxTravail.Lancer(reglages, journal);

            if (Assistant.FichierProduit.Trouver(dit) is not { } clip)
            {
                return $"Image {rang.ToString(CultureInfo.InvariantCulture)} : {dit}";
            }

            clips.Add(clip);

            // Le maillon suivant repart de la dernière image de ce clip. Au-delà de deux, la chaîne
            // se dévore : mesuré, le troisième maillon perd le visage et l'anatomie.
            if (maillon < ponts)
            {
                if (DerniereImage(clip, journal) is not { } suite)
                {
                    return $"Image {rang.ToString(CultureInfo.InvariantCulture)} : "
                        + "la dernière image du clip n'a pas pu être extraite.";
                }

                depart = suite;
            }
        }

        return null;
    }

    /// <summary>Extrait la dernière image d'un clip, pour servir de départ au maillon suivant.</summary>
    private static string? DerniereImage(string clip, Action<string>? journal)
    {
        var bac = Path.Combine(Path.GetTempPath(), "sxb-maillon-" + Path.GetFileNameWithoutExtension(clip));

        try
        {
            Directory.CreateDirectory(bac);

            // Une seule image, la dernière : « sseof -1 » se place une seconde avant la fin, ce qui
            // évite d'écrire les quatorze images pour n'en garder qu'une.
            if (!Ffmpeg(["-y", "-sseof", "-1", "-i", clip, "-frames:v", "1", "-update", "1",
                    Path.Combine(bac, "fin.png")], journal))
            {
                return null;
            }

            var fin = Path.Combine(bac, "fin.png");

            return File.Exists(fin) ? fin : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal?.Invoke($"extraction impossible : {exception.Message}");

            return null;
        }
    }

    /// <summary>Recolle les clips en une vidéo, sans réencoder.</summary>
    private static string Coller(IReadOnlyList<string> clips, string prefixe, Action<string>? journal)
    {
        if (clips.Count == 0)
        {
            return "Aucun clip produit.";
        }

        if (clips.Count == 1)
        {
            return $"Terminé : {clips[0]}";
        }

        var liste = Path.Combine(Path.GetTempPath(), "sxb-sequence.txt");
        var sortie = Path.Combine(Sorties, prefixe + "-complet.mp4");

        try
        {
            File.WriteAllText(liste, Liste(clips));

            journal?.Invoke($"Montage de {clips.Count.ToString(CultureInfo.InvariantCulture)} clips...");

            if (!Ffmpeg(["-y", "-f", "concat", "-safe", "0", "-i", liste, "-c", "copy", sortie], journal))
            {
                return "Le montage a échoué. Les clips restent dans le dossier de sortie.";
            }

            return File.Exists(sortie)
                ? $"Terminé : {sortie}"
                : "Le montage n'a rien produit. Les clips restent dans le dossier de sortie.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Montage impossible : {exception.Message}";
        }
        finally
        {
            try
            {
                File.Delete(liste);
            }
            catch (IOException)
            {
                // Un fichier de liste laissé derrière ne gêne personne.
            }
        }
    }

    private static bool Ffmpeg(string[] arguments, Action<string>? journal)
    {
        var programme = Path.Combine(AppContext.BaseDirectory, "Outils", "ffmpeg", "bin", "ffmpeg.exe");

        if (!File.Exists(programme))
        {
            journal?.Invoke($"ffmpeg est absent : {programme}");

            return false;
        }

        var depart = new ProcessStartInfo(programme)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments)
        {
            depart.ArgumentList.Add(argument);
        }

        try
        {
            using var processus = Process.Start(depart);

            if (processus is null)
            {
                return false;
            }

            var plainte = Serveurs.Tuyaux.Vider(processus).Erreur;

            if (processus.ExitCode != 0)
            {
                journal?.Invoke($"ffmpeg a rendu {processus.ExitCode.ToString(CultureInfo.InvariantCulture)} : "
                    + Ecourter(plainte));
            }

            return processus.ExitCode == 0;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            journal?.Invoke($"ffmpeg n'a pas démarré : {exception.Message}");

            return false;
        }
    }

    private static string Ecourter(string texte)
    {
        var lignes = texte.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return lignes.Length == 0 ? "" : lignes[^1].Trim();
    }

    private static int Lire(string texte, int defaut)
        => int.TryParse(texte.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lu)
            ? lu
            : defaut;
}
