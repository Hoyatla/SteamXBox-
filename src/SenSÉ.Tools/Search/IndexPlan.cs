namespace SenSÉ.Tools.Search;

/// <summary>
/// Where the index looks, and with what priority — the deciding half of building it.
/// </summary>
/// <remarks>
/// The walk itself belongs to the environment, which is allowed to touch the disk. What is here is
/// the judgement: which places are worth indexing and how much a result from each is worth before
/// anyone types anything. Those numbers decide what the user sees first, so they belong where they
/// can be read and tested rather than buried in a loop.
///
/// <para>
/// Drives are absent on purpose. Indexing their surface produced thousands of entries nobody was
/// looking for, and filed them under a drive letter that moves; a volume is now one entry found by
/// its own identity, which the environment builds separately.
/// </para>
/// </remarks>
public static class IndexPlan
{
    /// <summary>Never indexed: enormous, and nothing in it is what anyone was looking for.</summary>
    public const string ExcludedFragment = @"\Windows\WinSxS\";

    /// <summary>Whether a path is one the index refuses outright.</summary>
    public static bool IsExcluded(string path)
        => path.Contains(ExcludedFragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A directory of the <c>PATH</c> is worth far less when it is inside Windows.
    /// </summary>
    /// <remarks>
    /// System32 alone holds hundreds of executables that match almost any query. Ten against eighty
    /// keeps them findable and out of the way.
    /// </remarks>
    public static int PathDirectoryPriority(string directory, string windowsDirectory)
        => SearchText.Normalize(directory).StartsWith(SearchText.Normalize(windowsDirectory), StringComparison.Ordinal)
            ? 10
            : 80;

    /// <summary>
    /// Ce que SenSÉ héberge lui-même, avant tout le reste.
    /// </summary>
    /// <remarks>
    /// Au-dessus des raccourcis, et c'est délibéré. Quelqu'un qui cherche depuis SenSÉ cherche
    /// d'abord ce que SenSÉ contient : un outil accueilli dans <c>Outils</c> doit sortir avant
    /// un programme homonyme installé ailleurs sur la machine. Le produit est le premier endroit où
    /// regarder, pas le dernier.
    /// </remarks>
    public const int OutilsPriority = 200;

    /// <summary>Start menus and desktops: shortcuts, swept deep, ranked high.</summary>
    public const int ShortcutPriority = 120;

    /// <summary>
    /// Store and MSIX applications, just below shortcuts.
    /// </summary>
    /// <remarks>
    /// Below, because a packaged application that also has a Start menu shortcut should be found
    /// through the shortcut: the shortcut carries the name the user gave it and a real path, and the
    /// packaged entry carries an identifier nobody recognises.
    /// </remarks>
    public const int StoreApplicationPriority = 110;

    /// <summary>The user's own folders — Documents, Downloads, Pictures and the rest.</summary>
    public const int UserFolderPriority = 70;

    /// <summary>
    /// Ceilings on what one user folder may contribute.
    /// </summary>
    /// <remarks>
    /// A Downloads folder with forty thousand files would otherwise drown the Start menu entries,
    /// which are almost always what was wanted.
    /// </remarks>
    public const int MaxUserFolders = 150;

    /// <inheritdoc cref="MaxUserFolders"/>
    public const int MaxUserFiles = 250;

    /// <summary>
    /// Combien de niveaux descendre sous un dossier utilisateur pour y trouver un exécutable.
    /// </summary>
    /// <remarks>
    /// <b>Ce que zéro niveau coûtait.</b> Les dossiers utilisateur n'étaient lus qu'à leur premier
    /// niveau, et les exécutables seulement dans les dossiers du <c>PATH</c>. Une application
    /// portable — celles qu'on range précisément dans ses Documents parce qu'elles ne s'installent
    /// pas — était donc introuvable. Constaté : chercher « libreoffice » ne rendait que
    /// l'installeur dans Téléchargements, pendant que
    /// <c>Documents\Portable API\PortableApps\LibreOfficePortable\…\soffice.exe</c> attendait trois
    /// niveaux plus bas. Ni la barre de recherche ni l'Assistant ne pouvaient l'ouvrir.
    ///
    /// <para>Quatre, parce que c'est la profondeur où vivent réellement ces applications :
    /// <c>PortableApps\LibreOfficePortable\App\libreoffice\program\soffice.exe</c> en demande cinq
    /// depuis Documents, et un dossier de rangement personnel en ajoute souvent un. Descendre sans
    /// limite ferait parcourir des arbres de code source et des dépôts entiers, pour des binaires
    /// que personne ne cherche par leur nom.</para>
    /// </remarks>
    public const int UserExecutableDepth = 4;

    /// <summary>
    /// Plafond du balayage d'exécutables sous les dossiers utilisateur.
    /// </summary>
    /// <remarks>
    /// Séparé de <see cref="MaxUserFiles"/> : un seul <c>node_modules</c> ou un dossier de
    /// compilation rendrait des milliers de binaires, qui noieraient le menu Démarrer — lequel est
    /// presque toujours la réponse voulue.
    /// </remarks>
    public const int MaxUserExecutables = 400;

    /// <summary>
    /// Dossiers qu'on ne traverse pas en cherchant un exécutable.
    /// </summary>
    /// <remarks>
    /// Ce sont des arbres de construction et de dépendances : profonds, pleins de binaires, et dont
    /// aucun n'est ce que quelqu'un tape dans une barre de recherche. Les exclure est ce qui rend
    /// la descente abordable.
    /// </remarks>
    public static bool IsBuildDirectory(string name)
        => name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("target", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".venv", StringComparison.OrdinalIgnoreCase)
        || name.Equals("__pycache__", StringComparison.OrdinalIgnoreCase);
}
