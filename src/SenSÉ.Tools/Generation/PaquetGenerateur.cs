using System.IO.Compression;

namespace SenSÉ.Tools.Generation;

/// <summary>Où en est un empaquetage, pour que l'écran puisse le dire.</summary>
/// <param name="Fait">Combien d'octets ont été traités.</param>
/// <param name="Total">Combien il y en a en tout, ou zéro si on ne sait pas encore.</param>
/// <param name="Quoi">Le fichier en cours, sans son chemin.</param>
public readonly record struct AvanceePaquet(long Fait, long Total, string Quoi);

/// <summary>
/// Met le générateur dans un seul fichier, et l'en ressort.
/// </summary>
/// <remarks>
/// <b>Ce que « portable » veut dire ici.</b> Le générateur est un programme Python et son
/// interpréteur : cent mégaoctets de code et quatre gigaoctets et demi d'environnement, torch et
/// CUDA compris. Rien de tout cela n'est installé au sens de Windows — pas de registre, pas de
/// service, pas de dossier système. Il tient donc dans un fichier, et un fichier se déplace d'une
/// machine à l'autre.
///
/// <para>
/// <b>Les modèles restent dehors, et ce n'est pas une facilité.</b> Ils pèsent soixante-six
/// gigaoctets contre cinq pour le programme, ils ne se compressent pas — ce sont déjà des poids
/// quantifiés — et ils sont déjà décrits ailleurs : <c>Modeles\jeux-modeles.json</c> dit lesquels,
/// où les prendre et ce qu'ils réclament de la carte. Les mettre dans l'archive multiplierait sa
/// taille par quatorze pour transporter des fichiers qu'un fichier de déclaration sait déjà
/// nommer.
/// </para>
///
/// <para>
/// <b>Aucun réseau.</b> Ni ici ni au déballage : tout ce qui est nécessaire au fonctionnement est
/// dans l'archive, et ce qui n'y est pas est déclaré et se réclame. C'est la seule façon qu'un
/// générateur soit vraiment indépendant — sur une machine sans internet, il doit démarrer.
/// </para>
/// </remarks>
public static class PaquetGenerateur
{
    /// <summary>Ce que le paquet emporte, relatif au dossier des outils.</summary>
    /// <remarks>
    /// Deux dossiers, et pas un de plus. <c>ComfyUI</c> porte le programme et ses modules ;
    /// <c>Python</c> porte l'interpréteur avec torch et CUDA. Le reste d'<c>Outils</c> — ffmpeg,
    /// les moteurs d'agrandissement, le modèle de langage — a sa propre vie et ses propres
    /// licences.
    /// </remarks>
    public static readonly string[] Dossiers = ["ComfyUI", "Python"];

    /// <summary>Ce qui n'entre jamais dans le paquet.</summary>
    /// <remarks>
    /// Les modèles pour la raison dite plus haut. Les sorties, les entrées et les caches parce que
    /// ce sont les fichiers de quelqu'un — ses images, ses vidéos, ses essais — et qu'une archive
    /// du programme n'a pas à emporter le travail de l'utilisateur à son insu.
    /// </remarks>
    public static readonly string[] Exclus =
    [
        @"ComfyUI\models",
        @"ComfyUI\output",
        @"ComfyUI\input",
        @"ComfyUI\temp",
        @"ComfyUI\user",
        @"ComfyUI\.git",
        @"ComfyUI\__pycache__",
    ];

    private static string Outils(string racine) => Path.Combine(racine, "Outils");

    /// <summary>Les fichiers qui iront dans le paquet, et ce qu'ils pèsent.</summary>
    /// <param name="racine">Le dossier du produit.</param>
    public static IReadOnlyList<string> Contenu(string racine)
    {
        var depuis = Outils(racine);
        var pris = new List<string>();

        foreach (var dossier in Dossiers)
        {
            var complet = Path.Combine(depuis, dossier);

            if (!Directory.Exists(complet))
            {
                continue;
            }

            foreach (var fichier in Directory.EnumerateFiles(complet, "*", SearchOption.AllDirectories))
            {
                if (!Ecarte(Path.GetRelativePath(depuis, fichier)))
                {
                    pris.Add(fichier);
                }
            }
        }

        return pris;
    }

    /// <summary>Ce chemin relatif tombe-t-il dans une exclusion ?</summary>
    public static bool Ecarte(string relatif)
    {
        var propre = relatif.Replace('/', '\\');

        return Exclus.Any(e =>
            propre.Equals(e, StringComparison.OrdinalIgnoreCase)
            || propre.StartsWith(e + '\\', StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Met le générateur dans une archive.
    /// </summary>
    /// <param name="racine">Le dossier du produit.</param>
    /// <param name="vers">Le fichier à écrire.</param>
    /// <param name="avancee">Reçoit l'avancement, fichier par fichier.</param>
    /// <param name="arret">Pour renoncer en cours de route.</param>
    /// <returns>Une phrase vide si tout s'est bien passé, la raison sinon.</returns>
    /// <remarks>
    /// Écrit d'abord à côté, sous un nom provisoire, et renomme à la fin. Une archive de cinq
    /// gigaoctets interrompue au milieu — disque plein, machine éteinte — laisserait sinon un
    /// fichier qui porte le bon nom, s'ouvre à moitié, et ne se découvre qu'au déballage sur une
    /// autre machine.
    /// </remarks>
    public static string Compresser(
        string racine,
        string vers,
        Action<AvanceePaquet>? avancee = null,
        CancellationToken arret = default)
    {
        var depuis = Outils(racine);
        var fichiers = Contenu(racine);

        if (fichiers.Count == 0)
        {
            return "Aucun générateur à empaqueter : le dossier Outils ne porte ni ComfyUI ni Python.";
        }

        var total = fichiers.Sum(f => new FileInfo(f).Length);
        var provisoire = vers + ".part";
        var fait = 0L;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(vers)!);

            if (File.Exists(provisoire))
            {
                File.Delete(provisoire);
            }

            using (var flux = File.Create(provisoire))
            using (var archive = new ZipArchive(flux, ZipArchiveMode.Create))
            {
                foreach (var fichier in fichiers)
                {
                    arret.ThrowIfCancellationRequested();

                    var relatif = Path.GetRelativePath(depuis, fichier);

                    // Sans compression pour ce qui est déjà comprimé. Repasser un .pyd ou un .zip
                    // dans le compresseur coûte du temps et ne gagne rien ; la bibliothèque CUDA,
                    // elle, se comprime de moitié.
                    archive.CreateEntryFromFile(fichier, relatif.Replace('\\', '/'), Niveau(fichier));

                    fait += new FileInfo(fichier).Length;
                    avancee?.Invoke(new AvanceePaquet(fait, total, Path.GetFileName(fichier)));
                }
            }

            if (File.Exists(vers))
            {
                File.Delete(vers);
            }

            File.Move(provisoire, vers);

            return "";
        }
        catch (OperationCanceledException)
        {
            Nettoyer(provisoire);

            return "Empaquetage interrompu.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Nettoyer(provisoire);

            return $"Empaquetage impossible : {exception.Message}";
        }
    }

    /// <summary>
    /// Ressort le générateur d'une archive, dans le dossier des outils.
    /// </summary>
    /// <remarks>
    /// Ce qui existe déjà est recouvert, jamais effacé en bloc : les modèles vivent dans le même
    /// arbre et une remise à zéro du dossier emporterait soixante-six gigaoctets que l'archive ne
    /// contient pas.
    /// </remarks>
    public static string Decompresser(
        string racine,
        string depuis,
        Action<AvanceePaquet>? avancee = null,
        CancellationToken arret = default)
    {
        if (!File.Exists(depuis))
        {
            return $"Paquet introuvable : {depuis}";
        }

        var vers = Outils(racine);

        try
        {
            Directory.CreateDirectory(vers);

            using var archive = ZipFile.OpenRead(depuis);

            var total = archive.Entries.Sum(e => e.Length);
            var fait = 0L;

            foreach (var entree in archive.Entries)
            {
                arret.ThrowIfCancellationRequested();

                // Un chemin d'archive qui remonte hors du dossier est refusé. Une archive n'est pas
                // toujours celle qu'on a faite soi-même, et « ..\..\Windows\System32 » est le plus
                // vieux tour du métier.
                var cible = Path.GetFullPath(Path.Combine(vers, entree.FullName));

                if (!cible.StartsWith(Path.GetFullPath(vers), StringComparison.OrdinalIgnoreCase))
                {
                    return $"Paquet refusé : une entrée sort du dossier des outils ({entree.FullName}).";
                }

                if (entree.Length == 0 && entree.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(cible);

                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                entree.ExtractToFile(cible, overwrite: true);

                fait += entree.Length;
                avancee?.Invoke(new AvanceePaquet(fait, total, entree.Name));
            }

            return "";
        }
        catch (OperationCanceledException)
        {
            return "Déballage interrompu. Ce qui a été écrit reste en place.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Déballage impossible : {exception.Message}";
        }
    }

    /// <summary>Comprimer ce qui s'y prête, laisser le reste tranquille.</summary>
    private static CompressionLevel Niveau(string fichier)
        => Path.GetExtension(fichier).ToLowerInvariant() switch
        {
            ".zip" or ".7z" or ".gz" or ".xz" or ".whl" or ".png" or ".jpg" => CompressionLevel.NoCompression,
            _ => CompressionLevel.Optimal,
        };

    private static void Nettoyer(string provisoire)
    {
        try
        {
            if (File.Exists(provisoire))
            {
                File.Delete(provisoire);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un provisoire qui reste ne gêne personne : le suivant l'écrase.
        }
    }
}
