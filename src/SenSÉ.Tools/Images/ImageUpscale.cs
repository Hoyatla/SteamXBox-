using SenSÉ.Tools.Agrandissement;

namespace SenSÉ.Tools.Images;

/// <summary>
/// Agrandit une image sans la rendre floue, et sans interpréteur.
/// </summary>
/// <remarks>
/// Remplace un script Python qui réclamait PyTorch — plusieurs gigaoctets pour agrandir une photo.
/// Le moteur appelé ici est le même binaire compilé que pour la vidéo, déjà livré avec le produit.
///
/// <para>
/// <b>Ce que le moteur ne sait pas faire, et qui est rattrapé ici.</b> Il ne lit et n'écrit que du
/// jpg, du png et du webp. L'outil annonce davantage — bmp, tiff, avif — alors les autres formats
/// sont convertis en PNG avant et reconvertis après, par ffmpeg. Sans cela, sept extensions sur dix
/// seraient refusées par un outil qui les affiche.
/// </para>
///
/// <para>
/// Le HEIC a été retiré de la liste plutôt que rattrapé : ffmpeg n'a pas de démultiplexeur pour ce
/// conteneur, vérifié sur la machine. Une extension proposée qui échoue toujours est un piège.
/// </para>
/// </remarks>
public static class ImageUpscale
{
    /// <summary>Les formats de sortie que l'outil sait produire, au-delà des trois natifs.</summary>
    private static readonly string[] ParConversion = ["bmp", "tif", "tiff"];

    /// <summary>
    /// Agrandit l'image désignée et écrit le résultat à côté d'elle.
    /// </summary>
    /// <param name="image">Le fichier désigné par l'utilisateur.</param>
    /// <param name="modele">Famille de modèle ; l'échelle y est ajoutée si elle existe.</param>
    /// <param name="echelle">2, 3 ou 4.</param>
    /// <param name="format">Format de sortie, ou « auto » pour garder celui de l'image.</param>
    /// <param name="journal">Reçoit ce qui mérite d'être dit pendant le travail.</param>
    public static string Agrandir(
        string image,
        string modele,
        string echelle,
        string format,
        Action<string>? journal)
    {
        if (!File.Exists(image))
        {
            return "L'image choisie est introuvable.";
        }

        var dossier = Path.GetDirectoryName(image);

        if (string.IsNullOrEmpty(dossier))
        {
            return "L'image choisie n'a pas de dossier.";
        }

        if (!File.Exists(Moteur.Agrandisseur))
        {
            return $"Moteur d'agrandissement absent : {Moteur.Agrandisseur}";
        }

        if (Moteur.ResoudreModele(ref modele, ref echelle, journal) is { } manquant)
        {
            return manquant;
        }

        var entree = Extension(image);
        var sortie = format.Equals("auto", StringComparison.OrdinalIgnoreCase) ? entree : Normaliser(format);

        if (!Natif(sortie) && !Array.Exists(ParConversion, f => f == sortie))
        {
            sortie = "png";
        }

        var nom = Path.GetFileNameWithoutExtension(image);
        var final = Path.Combine(dossier, $"{nom}_out.{sortie}");

        // Réservé plutôt que nommé : le dossier est effacé récursivement à la fin, et un dossier de
        // l'utilisateur qui portait déjà ce nom partait avec. Voir Moteur.Reserver.
        var travail = Moteur.Reserver(Path.Combine(dossier, $"{nom}_travail"));

        try
        {
            return Traiter(image, final, travail, entree, sortie, modele, echelle, journal);
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

    private static string Traiter(
        string image,
        string final,
        string travail,
        string entree,
        string sortie,
        string modele,
        string echelle,
        Action<string>? journal)
    {
        var conversionEntree = !Natif(entree);
        var conversionSortie = !Natif(sortie);

        // ffmpeg n'est demandé que si une conversion l'exige : agrandir un png en png ne réclame
        // rien d'autre que le moteur, et cet outil doit rester utilisable sur une machine nue.
        string? ffmpeg = null;

        if (conversionEntree || conversionSortie)
        {
            ffmpeg = Moteur.Trouver("ffmpeg");

            if (ffmpeg is null)
            {
                return $"Le format « {(conversionEntree ? entree : sortie)} » demande ffmpeg, introuvable.";
            }
        }

        var source = image;

        if (conversionEntree)
        {
            Directory.CreateDirectory(travail);
            source = Path.Combine(travail, "entree.png");
            journal?.Invoke($"Conversion du {entree} en png avant agrandissement.");

            if (!Moteur.Lancer(ffmpeg!, out var refus, "-y", "-v", "error", "-i", image, "-frames:v", "1", source))
            {
                return $"Lecture impossible de cette image : {refus}";
            }
        }

        var produit = final;

        if (conversionSortie)
        {
            Directory.CreateDirectory(travail);
            produit = Path.Combine(travail, "sortie.png");
        }

        journal?.Invoke($"Agrandissement en {echelle} fois, modèle {modele}.");

        if (!Moteur.Lancer(Moteur.Agrandisseur, out var plainte,
                "-i", source, "-o", produit, "-n", modele, "-s", echelle,
                "-f", Format(produit), "-j", Moteur.Threads))
        {
            return $"Agrandissement impossible : {plainte}";
        }

        if (conversionSortie
            && !Moteur.Lancer(ffmpeg!, out var souci, "-y", "-v", "error", "-i", produit, "-frames:v", "1", final))
        {
            return $"Conversion en {sortie} impossible : {souci}";
        }

        return $"Terminé : {final}";
    }

    /// <summary>L'extension, en minuscules et sans le point.</summary>
    private static string Extension(string chemin)
        => Normaliser(Path.GetExtension(chemin).TrimStart('.'));

    /// <summary>« jpeg » et « jpg » désignent la même chose ; le moteur ne connaît que le second.</summary>
    private static string Normaliser(string format)
        => format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : format.ToLowerInvariant();

    private static bool Natif(string format)
        => Array.Exists(Moteur.FormatsNatifs, f => f == format);

    private static string Format(string chemin)
    {
        var extension = Extension(chemin);

        return Natif(extension) ? extension : "png";
    }
}
