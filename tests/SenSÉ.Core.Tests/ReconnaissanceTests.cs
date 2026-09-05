using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que SenSÉ reconnaît dans son dossier d'outils.
/// </summary>
/// <remarks>
/// Les cas éprouvés ici sont ceux du dossier réel, pas des cas inventés : une application installée
/// qui traîne son désinstalleur, un interpréteur avec quatre exécutables, deux outils en ligne de
/// commande qui n'en ont qu'un, un dossier de modèles qui n'en a aucun.
/// </remarks>
public class ReconnaissanceTests : IDisposable
{
    private readonly DirectoryInfo _outils = Directory.CreateTempSubdirectory("outils");

    public void Dispose()
    {
        _outils.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Écrit un exécutable dont l'en-tête annonce le sous-système demandé.
    /// </summary>
    /// <remarks>
    /// Un faux fichier suffit : ce qui est éprouvé est la lecture de l'en-tête, et un vrai binaire
    /// rendrait l'épreuve dépendante de ce qui traîne sur la machine.
    /// </remarks>
    private static void Exe(string chemin, ushort sousSysteme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);

        var octets = new byte[256];

        // L'offset 60 dit où commence l'en-tête PE ; on le pose à 128.
        BitConverter.GetBytes(128).CopyTo(octets, 60);
        BitConverter.GetBytes(0x00004550u).CopyTo(octets, 128);
        BitConverter.GetBytes(sousSysteme).CopyTo(octets, 128 + 92);

        File.WriteAllBytes(chemin, octets);
    }

    private string Dossier(string nom)
    {
        var chemin = Path.Combine(_outils.FullName, nom);

        Directory.CreateDirectory(chemin);

        return chemin;
    }

    private ProgrammeReconnu Voir(string nom) => Reconnaissance.Regarder(Path.Combine(_outils.FullName, nom))!;

    /// <summary>Une application installée est reconnue malgré son désinstalleur.</summary>
    /// <remarks>
    /// Le cas de Comfy Desktop, tel qu'il est sur le disque : deux exécutables à la racine, dont l'un
    /// n'est là que pour l'enlever. Le compter ferait passer toute application installée pour une
    /// boîte à outils.
    /// </remarks>
    [Fact]
    public void AnInstalledApplicationIsRecognisedDespiteItsUninstaller()
    {
        var dossier = Dossier("Comfy-Desktop");

        Exe(Path.Combine(dossier, "Comfy Desktop.exe"), 2);
        Exe(Path.Combine(dossier, "Uninstall Comfy Desktop.exe"), 2);

        var vu = Voir("Comfy-Desktop");

        Assert.Equal(FormeProgramme.Application, vu.Forme);
        Assert.Equal("Comfy Desktop", vu.Nom);
        Assert.EndsWith("Comfy Desktop.exe", vu.Executable, StringComparison.Ordinal);
    }

    /// <summary>Inno Setup nomme son désinstalleur autrement, et il ne compte pas non plus.</summary>
    [Fact]
    public void TheOtherUninstallerConventionIsIgnoredToo()
    {
        var dossier = Dossier("UnOutil");

        Exe(Path.Combine(dossier, "outil.exe"), 2);
        Exe(Path.Combine(dossier, "unins000.exe"), 2);

        Assert.Equal(FormeProgramme.Application, Voir("UnOutil").Forme);
    }

    /// <summary>
    /// Un programme en ligne de commande n'obtient pas de tuile.
    /// </summary>
    /// <remarks>
    /// <c>rife-ncnn-vulkan</c> et <c>Real-ESRGAN-ncnn</c> n'ont qu'un exécutable chacun : la règle du
    /// fichier unique, seule, leur donnerait une tuile qui ouvrirait une console aussitôt refermée.
    /// </remarks>
    [Fact]
    public void ACommandLineProgramGetsNoTile()
    {
        Exe(Path.Combine(Dossier("rife-ncnn-vulkan"), "rife-ncnn-vulkan.exe"), 3);

        var vu = Voir("rife-ncnn-vulkan");

        Assert.Equal(FormeProgramme.Bibliotheque, vu.Forme);
        Assert.Contains("ligne de commande", vu.Pourquoi, StringComparison.Ordinal);
    }

    /// <summary>Un interpréteur est une bibliothèque, pas une application.</summary>
    [Fact]
    public void AnInterpreterIsALibraryNotAnApplication()
    {
        var dossier = Dossier("Python");

        Exe(Path.Combine(dossier, "python.exe"), 3);
        Exe(Path.Combine(dossier, "pythonw.exe"), 2);

        Assert.Equal(FormeProgramme.Bibliotheque, Voir("Python").Forme);
    }

    /// <summary>Un programme rangé sous un sous-dossier ne se met pas sur la grille.</summary>
    /// <remarks>Le cas de ffmpeg, dont l'exécutable vit dans <c>bin</c>.</remarks>
    [Fact]
    public void AProgramTuckedInASubfolderDoesNotReachTheGrid()
    {
        Exe(Path.Combine(Dossier("ffmpeg"), "bin", "ffmpeg.exe"), 3);

        Assert.Equal(FormeProgramme.Bibliotheque, Voir("ffmpeg").Forme);
    }

    /// <summary>Des fichiers sans le moindre programme sont des données.</summary>
    /// <remarks>
    /// Le dossier des modèles pèse soixante-six gigaoctets et n'exécute rien. Le confondre avec une
    /// bibliothèque le ferait proposer à l'assistant comme quelque chose à lancer.
    /// </remarks>
    [Fact]
    public void FilesWithoutAnyProgramAreData()
    {
        File.WriteAllText(Path.Combine(Dossier("Modeles"), "jeux-modeles.json"), "{}");

        Assert.Equal(FormeProgramme.Donnees, Voir("Modeles").Forme);
    }

    /// <summary>Une application PortableApps se déclare, et son nom vient d'elle.</summary>
    [Fact]
    public void APortableAppsApplicationDeclaresItselfAndItsName()
    {
        var dossier = Dossier("KritaPortable");

        Directory.CreateDirectory(Path.Combine(dossier, "App", "AppInfo"));
        File.WriteAllText(
            Path.Combine(dossier, "App", "AppInfo", "appinfo.ini"),
            "[Details]\nName=Krita Portable\nAppID=KritaPortable\n");
        Exe(Path.Combine(dossier, "KritaPortable.exe"), 2);

        var vu = Voir("KritaPortable");

        Assert.Equal(FormeProgramme.Application, vu.Forme);
        Assert.Equal("Krita Portable", vu.Nom);
        Assert.EndsWith("KritaPortable.exe", vu.Executable, StringComparison.Ordinal);
    }

    /// <summary>L'inventaire porte tout, y compris ce qui n'aura pas de tuile.</summary>
    /// <remarks>
    /// C'est la moitié de la règle : la grille ne montre que ce qui s'ouvre, mais l'assistant doit
    /// savoir que Python et ffmpeg sont là — sans quoi il propose d'installer ce qu'il a déjà.
    /// </remarks>
    [Fact]
    public void TheInventoryCarriesEverythingIncludingWhatGetsNoTile()
    {
        Exe(Path.Combine(Dossier("Comfy-Desktop"), "Comfy Desktop.exe"), 2);
        Exe(Path.Combine(Dossier("Python"), "python.exe"), 3);
        File.WriteAllText(Path.Combine(Dossier("Modeles"), "m.json"), "{}");

        var tout = Reconnaissance.Lire(_outils.FullName);

        Assert.Equal(3, tout.Count);
        Assert.Single(tout.Where(p => p.Forme == FormeProgramme.Application));
    }

    /// <summary>
    /// Un programme fait de scripts est une bibliothèque, pas des données.
    /// </summary>
    /// <remarks>
    /// « Pas d'exécutable » et « rien à exécuter » ne sont pas la même chose : un programme Python
    /// n'a aucun <c>.exe</c>, c'est l'interpréteur voisin qui le lance. Le classer avec les modèles
    /// le rendrait invisible à l'assistant, qui proposerait d'installer ce qui est déjà là.
    /// </remarks>
    [Fact]
    public void AProgramMadeOfScriptsIsALibraryNotData()
    {
        var dossier = Dossier("UnProgrammePython");

        File.WriteAllText(Path.Combine(dossier, "main.py"), "print('bonjour')");

        Assert.Equal(FormeProgramme.Bibliotheque, Voir("UnProgrammePython").Forme);
    }

    /// <summary>Et un dossier vidé de son programme redevient ce qu'il reste : des fichiers.</summary>
    /// <remarks>
    /// Constaté sur le dossier réel : <c>Outils\ComfyUI</c> ne portait plus que ses entrées, ses
    /// sorties et ses modèles, son programme ayant été retiré. La reconnaissance doit le dire, pas
    /// continuer à le présenter comme un programme au motif qu'il en fut un.
    /// </remarks>
    [Fact]
    public void AFolderEmptiedOfItsProgramBecomesWhatIsLeft()
    {
        var dossier = Dossier("ComfyUI");

        Directory.CreateDirectory(Path.Combine(dossier, "models"));
        File.WriteAllText(Path.Combine(dossier, "models", "un.safetensors"), "poids");

        Assert.Equal(FormeProgramme.Donnees, Voir("ComfyUI").Forme);
    }

    [Fact]
    public void AnAbsentFolderIsNotAnError()
        => Assert.Empty(Reconnaissance.Lire(Path.Combine(_outils.FullName, "jamais")));

    /// <summary>Un fichier qui n'est pas un programme ne passe pas pour graphique.</summary>
    [Fact]
    public void AFileThatIsNotAProgramDoesNotPassForAWindowedOne()
    {
        var faux = Path.Combine(_outils.FullName, "faux.exe");

        File.WriteAllText(faux, "ceci n'est pas un exécutable");

        Assert.False(Reconnaissance.Graphique(faux));
    }
}

/// <summary>
/// Le rang que la recherche donne à ce que SenSÉ héberge.
/// </summary>
/// <remarks>
/// Chercher depuis SenSÉ, c'est chercher d'abord dans SenSÉ. Cette épreuve tient la règle
/// par son seul point vérifiable sans lancer d'interface : la note de départ.
/// </remarks>
public class RangDesOutilsTests
{
    /// <summary>Ce que le produit héberge passe avant tout le reste.</summary>
    /// <remarks>
    /// Au-dessus des raccourcis du menu Démarrer, donc au-dessus de la source la mieux notée. Sans
    /// cela, un outil accueilli sortirait derrière un homonyme installé ailleurs sur la machine.
    /// </remarks>
    [Fact]
    public void WhatTheProductHostsComesBeforeEverythingElse()
    {
        Assert.True(
            SenSÉ.Tools.Search.IndexPlan.OutilsPriority
                > SenSÉ.Tools.Search.IndexPlan.ShortcutPriority,
            "le dossier des outils doit primer sur les raccourcis");

        Assert.True(
            SenSÉ.Tools.Search.IndexPlan.OutilsPriority
                > SenSÉ.Tools.Search.IndexPlan.StoreApplicationPriority);
    }
}

/// <summary>
/// Le cas où l'on ne sait pas quoi ouvrir, et où on l'avoue.
/// </summary>
/// <remarks>
/// Mesuré sur LibreOffice : rien à la racine de son dossier, seize programmes à fenêtre dans
/// <c>program\</c>, et ni raccourci ni clé de registre pour désigner le principal. La distinction
/// d'avec un interpréteur tient à la racine — Python et llama.cpp y posent leurs exécutables,
/// LibreOffice n'en pose aucun.
/// </remarks>
public class PorteInconnueTests : IDisposable
{
    private readonly DirectoryInfo _outils = Directory.CreateTempSubdirectory("portes");

    public void Dispose()
    {
        _outils.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private static void Exe(string chemin, ushort sousSysteme, int taille = 256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);

        var octets = new byte[Math.Max(taille, 256)];

        BitConverter.GetBytes(128).CopyTo(octets, 60);
        BitConverter.GetBytes(0x00004550u).CopyTo(octets, 128);
        BitConverter.GetBytes(sousSysteme).CopyTo(octets, 128 + 92);

        File.WriteAllBytes(chemin, octets);
    }

    private string Bureautique()
    {
        var dossier = Path.Combine(_outils.FullName, "LibreOffice");

        Exe(Path.Combine(dossier, "program", "soffice.exe"), 2, 8000);
        Exe(Path.Combine(dossier, "program", "swriter.exe"), 2, 2000);
        Exe(Path.Combine(dossier, "program", "unopkg.exe"), 3, 4000);

        return dossier;
    }

    /// <summary>Des fenêtres sous la racine, rien à la racine : la porte est inconnue.</summary>
    [Fact]
    public void WindowsBelowTheRootAndNothingAtItMeansTheDoorIsUnknown()
        => Assert.Equal(
            FormeProgramme.PorteInconnue,
            Reconnaissance.Regarder(Bureautique())!.Forme);

    /// <summary>On ne propose que ce qui ouvre une fenêtre.</summary>
    /// <remarks>
    /// Proposer les programmes console reviendrait à demander à l'utilisateur de choisir entre des
    /// réponses dont certaines sont sûrement fausses.
    /// </remarks>
    [Fact]
    public void OnlyWhatOpensAWindowIsOffered()
    {
        var portes = Reconnaissance.Portes(Bureautique()).Select(Path.GetFileName).ToList();

        Assert.Contains("soffice.exe", portes);
        Assert.DoesNotContain("unopkg.exe", portes);
    }

    /// <summary>Le plus gros vient en tête, comme aide et non comme certitude.</summary>
    [Fact]
    public void TheLargestComesFirstAsAnAidNotACertainty()
        => Assert.Equal("soffice.exe", Path.GetFileName(Reconnaissance.Portes(Bureautique())[0]));

    /// <summary>
    /// Un interpréteur ne devient pas une porte inconnue pour autant.
    /// </summary>
    /// <remarks>
    /// Python a un exécutable à fenêtre — <c>pythonw.exe</c> — et resterait une bibliothèque : ce
    /// qui le distingue est qu'il pose ses exécutables à la racine.
    /// </remarks>
    [Fact]
    public void AnInterpreterDoesNotBecomeAnUnknownDoor()
    {
        var dossier = Path.Combine(_outils.FullName, "Python");

        Exe(Path.Combine(dossier, "python.exe"), 3);
        Exe(Path.Combine(dossier, "pythonw.exe"), 2);
        Exe(Path.Combine(dossier, "Scripts", "pip.exe"), 3);

        Assert.Equal(FormeProgramme.Bibliotheque, Reconnaissance.Regarder(dossier)!.Forme);
    }

    /// <summary>Une porte désignée par l'utilisateur pose la tuile, même sans reconnaissance.</summary>
    /// <remarks>
    /// Et elle garde le chemin depuis le dossier de l'outil : la porte est un cran plus bas, et ne
    /// retenir que le nom du fichier donnerait une cible qui n'existe pas.
    /// </remarks>
    [Fact]
    public void ADoorNamedByTheUserLaysTheTileAndKeepsItsPath()
    {
        var dossier = Bureautique();
        var plugins = Path.Combine(_outils.FullName, "Plugins");
        var programme = Reconnaissance.Regarder(dossier)!;

        var dit = AccueilOutil.Declarer(
            programme, plugins, Path.Combine(dossier, "program", "soffice.exe"));

        Assert.Contains("tuile posée", dit, StringComparison.Ordinal);
        Assert.Equal(
            @"{tools}\LibreOffice\program\soffice.exe",
            PluginCatalog.Scan(plugins).Loaded.Single().Target);
    }
}
