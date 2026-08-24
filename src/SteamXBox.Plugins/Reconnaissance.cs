namespace SteamXBox.Plugins;

/// <summary>Ce qu'un dossier d'outil se trouve être.</summary>
public enum FormeProgramme
{
    /// <summary>Quelque chose qu'on ouvre, et qui mérite donc une tuile.</summary>
    Application,

    /// <summary>Des programmes dont d'autres se servent : un interpréteur, un encodeur, un moteur.</summary>
    Bibliotheque,

    /// <summary>Ni l'un ni l'autre : des fichiers. Les modèles, par exemple.</summary>
    Donnees,
}

/// <summary>Un programme trouvé dans le dossier des outils.</summary>
/// <param name="Id">Le nom du dossier, qui sert d'identité.</param>
/// <param name="Nom">Ce qu'on affiche.</param>
/// <param name="Dossier">Son corps, tout entier.</param>
/// <param name="Executable">Ce qu'on lance, vide s'il n'y a rien à lancer.</param>
/// <param name="Forme">Application, bibliothèque ou données.</param>
/// <param name="Pourquoi">La raison du classement, en clair.</param>
public sealed record ProgrammeReconnu(
    string Id,
    string Nom,
    string Dossier,
    string Executable,
    FormeProgramme Forme,
    string Pourquoi);

/// <summary>
/// Reconnaît ce qui se trouve dans le dossier des outils, au lieu de tout ramasser.
/// </summary>
/// <remarks>
/// <b>Pourquoi reconnaître plutôt que lister.</b> Donner une tuile à tout dossier présent mettrait
/// <c>Python</c>, <c>ffmpeg</c> et <c>Modeles</c> sur la grille à côté des applications. Cliquer
/// dessus n'aurait aucun sens, et la grille cesserait de vouloir dire quelque chose. Mais l'inverse
/// — ne connaître que ce qui a une tuile — laisserait l'assistant aveugle à la moitié de ce dont il
/// dispose. On inventorie donc tout, et l'on ne met sur la grille que ce qui s'ouvre.
///
/// <para>
/// <b>Le critère n'est pas une liste de noms.</b> Une liste vieillirait au premier outil ajouté.
/// Deux formes se reconnaissent d'elles-mêmes : celle de PortableApps, qui se déclare dans
/// <c>App\AppInfo\appinfo.ini</c>, et celle d'un programme ordinaire, qui a un exécutable unique à
/// la racine de son dossier.
/// </para>
///
/// <para>
/// <b>Et un exécutable unique ne suffit pas.</b> Constaté sur le dossier réel :
/// <c>rife-ncnn-vulkan</c> et <c>Real-ESRGAN-ncnn</c> n'ont qu'un exécutable chacun, mais ce sont
/// des programmes en ligne de commande — une tuile ouvrirait une console qui se referme aussitôt.
/// Windows dit lui-même lequel est lequel, dans l'en-tête du fichier : sous-système 2 pour une
/// fenêtre, 3 pour une console. C'est le fichier qui répond, pas nous.
/// </para>
/// </remarks>
public static class Reconnaissance
{
    /// <summary>Tout ce que porte le dossier des outils, classé.</summary>
    /// <param name="outils">Le dossier <c>Outils</c> du produit.</param>
    public static IReadOnlyList<ProgrammeReconnu> Lire(string outils)
    {
        if (!Directory.Exists(outils))
        {
            return [];
        }

        var vus = new List<ProgrammeReconnu>();

        foreach (var dossier in Directory.EnumerateDirectories(outils).OrderBy(d => d))
        {
            if (Regarder(dossier) is { } programme)
            {
                vus.Add(programme);
            }
        }

        return vus;
    }

    /// <summary>Ce qu'est ce dossier-ci.</summary>
    public static ProgrammeReconnu? Regarder(string dossier)
    {
        if (!Directory.Exists(dossier))
        {
            return null;
        }

        var id = Path.GetFileName(dossier.TrimEnd(Path.DirectorySeparatorChar));

        // Une application PortableApps se présente elle-même : son fichier de description porte le
        // nom à afficher, ce qu'aucune devinette sur le nom de dossier ne donnerait aussi bien.
        if (Declaree(dossier) is { } declaree)
        {
            return declaree;
        }

        var lancables = Racine(dossier);

        if (lancables.Count == 1 && Graphique(lancables[0]))
        {
            return new ProgrammeReconnu(
                id,
                Path.GetFileNameWithoutExtension(lancables[0]),
                dossier,
                lancables[0],
                FormeProgramme.Application,
                "un seul exécutable à la racine, et il ouvre une fenêtre");
        }

        var partout = Executable(dossier);

        return new ProgrammeReconnu(
            id,
            id,
            dossier,
            "",
            partout ? FormeProgramme.Bibliotheque : FormeProgramme.Donnees,
            partout
                ? lancables.Count switch
                {
                    0 => "aucun exécutable à la racine : d'autres outils s'en servent",
                    1 => "son exécutable est un programme en ligne de commande",
                    _ => $"{lancables.Count} exécutables à la racine : c'est une boîte à outils",
                }
                : "aucun exécutable : ce sont des fichiers");
    }

    /// <summary>Ce que Windows sait exécuter, ou faire exécuter par un interpréteur.</summary>
    /// <remarks>
    /// Chercher des <c>.exe</c> ne suffit pas, et le dossier réel l'a montré tout de suite : ComfyUI
    /// n'en contient pas un seul — c'est un programme Python, lancé par l'interpréteur voisin — et il
    /// se voyait classé « données » à côté des modèles. « Pas d'exécutable » et « rien à exécuter »
    /// ne sont pas la même chose.
    /// </remarks>
    private static readonly string[] Executables =
        [".exe", ".dll", ".py", ".bat", ".cmd", ".ps1", ".jar"];

    private static bool Executable(string dossier)
    {
        // La racine d'abord, et pas seulement pour aller vite : une descente récursive s'arrête à la
        // première permission refusée, et son échec rendrait « rien à exécuter » pour un dossier qui
        // porte son programme sous les yeux. ComfyUI a son main.py à la racine.
        if (Porte(dossier, SearchOption.TopDirectoryOnly))
        {
            return true;
        }

        return Directory
            .EnumerateDirectories(dossier)
            .Any(sous => Porte(sous, SearchOption.TopDirectoryOnly));
    }

    private static bool Porte(string dossier, SearchOption jusquou)
    {
        try
        {
            return Directory
                .EnumerateFiles(dossier, "*", jusquou)
                .Any(f => Executables.Contains(
                    Path.GetExtension(f).ToLowerInvariant(), StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Une application PortableApps, si le dossier en a la forme.</summary>
    private static ProgrammeReconnu? Declaree(string dossier)
    {
        var description = Path.Combine(dossier, "App", "AppInfo", "appinfo.ini");

        if (!File.Exists(description))
        {
            return null;
        }

        var id = Path.GetFileName(dossier.TrimEnd(Path.DirectorySeparatorChar));
        var nom = Nomme(description) is { Length: > 0 } dit ? dit : id;

        // Le lanceur porte le nom du dossier — c'est la convention du format. On retombe sinon sur
        // le premier exécutable graphique trouvé, plutôt que de rendre une application sans porte.
        var attendu = Path.Combine(dossier, id + ".exe");
        var lanceur = File.Exists(attendu)
            ? attendu
            : Racine(dossier).FirstOrDefault(Graphique) ?? "";

        return new ProgrammeReconnu(
            id, nom, dossier, lanceur, FormeProgramme.Application, "elle se déclare en PortableApps");
    }

    /// <summary>Le nom que la description annonce.</summary>
    private static string Nomme(string description)
    {
        try
        {
            foreach (var ligne in File.ReadLines(description))
            {
                if (ligne.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                {
                    return ligne[5..].Trim();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un nom illisible n'empêche pas de reconnaître l'application : le dossier en donne un.
        }

        return "";
    }

    /// <summary>Les exécutables de la racine, sans les désinstalleurs.</summary>
    /// <remarks>
    /// Un installeur laisse toujours son désinstalleur à côté du programme. Le compter ferait passer
    /// toute application installée pour une boîte à outils — constaté sur Comfy Desktop, qui a
    /// exactement deux exécutables à la racine dont l'un s'appelle « Uninstall Comfy Desktop ».
    /// </remarks>
    private static List<string> Racine(string dossier)
    {
        try
        {
            return [.. Directory
                .EnumerateFiles(dossier, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !Desinstalleur(Path.GetFileName(f)))
                .OrderBy(f => f)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Ce nom est-il celui d'un désinstalleur ?</summary>
    /// <remarks>
    /// Les deux conventions qui couvrent l'essentiel : <c>Uninstall …</c> pour NSIS et donc pour
    /// Electron, <c>unins000.exe</c> pour Inno Setup.
    /// </remarks>
    public static bool Desinstalleur(string nom)
        => nom.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
            || nom.StartsWith("unins0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ce programme ouvre-t-il une fenêtre, ou une console ?
    /// </summary>
    /// <remarks>
    /// La réponse est dans le fichier, pas dans son nom. L'en-tête PE porte un champ « sous-système »
    /// — 2 pour une application graphique, 3 pour une console — et Windows s'en sert lui-même pour
    /// décider s'il ouvre une fenêtre de commandes au lancement. On lit donc ce que Windows lira.
    ///
    /// <para>
    /// Le chemin jusqu'au champ : un entier à l'offset 60 donne où commence l'en-tête PE ; la
    /// signature <c>PE\0\0</c> l'ouvre ; le sous-système est 92 octets plus loin, au même endroit
    /// que le fichier soit en 32 ou en 64 bits.
    /// </para>
    /// </remarks>
    public static bool Graphique(string executable)
    {
        try
        {
            using var flux = File.OpenRead(executable);
            using var lecture = new BinaryReader(flux);

            if (flux.Length < 64)
            {
                return false;
            }

            flux.Position = 60;

            var entete = lecture.ReadInt32();

            if (entete <= 0 || entete + 94 > flux.Length)
            {
                return false;
            }

            flux.Position = entete;

            if (lecture.ReadUInt32() != 0x00004550)
            {
                return false;
            }

            flux.Position = entete + 92;

            return lecture.ReadUInt16() == 2;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }
}
