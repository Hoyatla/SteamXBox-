using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Ce que SteamXBox reconnaît dans son dossier d'outils.
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
