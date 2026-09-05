using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// La tuile qu'un programme accueilli reçoit — et celles qu'il ne reçoit pas.
/// </summary>
/// <remarks>
/// Une tuile qui n'existerait qu'en mémoire disparaîtrait au redémarrage, et l'écran des outils
/// n'aurait rien à archiver ni à éteindre. Le manifeste est donc ce qui rend l'outil visible et
/// gouvernable à la fois, et c'est ce double emploi qui est éprouvé ici.
/// </remarks>
public class AccueilDeclarationTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("declaration");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Plugins => Path.Combine(_bac.FullName, "Plugins");

    private static ProgrammeReconnu Application(string dossier, string nom = "Comfy Desktop")
        => new(
            Path.GetFileName(dossier),
            nom,
            dossier,
            Path.Combine(dossier, nom + ".exe"),
            FormeProgramme.Application,
            "un seul exécutable à la racine, et il ouvre une fenêtre");

    private string Corps(string nom = "Comfy-Desktop")
    {
        var dossier = Path.Combine(_bac.FullName, "Outils", nom);

        Directory.CreateDirectory(dossier);

        return dossier;
    }

    /// <summary>Le manifeste écrit se relit, et il désigne bien le programme.</summary>
    /// <remarks>
    /// L'aller-retour par le lecteur du produit, plutôt qu'une comparaison de texte : ce qui compte
    /// n'est pas ce qu'on a écrit, c'est ce que le chargeur en fera au prochain démarrage.
    /// </remarks>
    [Fact]
    public void TheManifestWrittenReadsBackAndPointsAtTheProgram()
    {
        var corps = Corps();

        Assert.Contains("tuile posée", AccueilOutil.Declarer(Application(corps), Plugins), StringComparison.Ordinal);

        var relu = PluginCatalog.Scan(Plugins).Loaded.Single();

        Assert.Equal("comfy-desktop", relu.Id);
        Assert.Equal("Comfy Desktop", relu.Name);
        Assert.Equal(PluginActions.Application, relu.Does);
        Assert.Equal(@"{tools}\Comfy-Desktop\Comfy Desktop.exe", relu.Target);
    }

    /// <summary>
    /// Le manifeste nomme le corps du programme, pas seulement sa porte.
    /// </summary>
    /// <remarks>
    /// C'est ce champ qui permettra d'archiver les cinq cents mégaoctets au lieu du kilooctet de
    /// description. Sans lui, le cycle de vie continue d'agir sur le mauvais dossier.
    /// </remarks>
    [Fact]
    public void TheManifestNamesTheBodyAndNotOnlyTheDoor()
    {
        AccueilOutil.Declarer(Application(Corps()), Plugins);

        Assert.Equal(
            @"{tools}\Comfy-Desktop",
            PluginCatalog.Scan(Plugins).Loaded.Single().Environnement?.Dossier);
    }

    /// <summary>Les accents survivent à l'écriture.</summary>
    /// <remarks>
    /// Un manifeste a déjà été abîmé une fois par un outil d'édition qui relisait en ANSI et
    /// réécrivait en UTF-8. Ici c'est nous qui écrivons, donc c'est à nous de le prouver.
    /// </remarks>
    [Fact]
    public void AccentsSurviveTheWriting()
    {
        AccueilOutil.Declarer(Application(Corps("Editeur"), "Éditeur de scènes"), Plugins);

        Assert.Equal("Éditeur de scènes", PluginCatalog.Scan(Plugins).Loaded.Single().Name);
    }

    /// <summary>Le fichier ne porte pas de marque d'ordre d'octets.</summary>
    /// <remarks>
    /// Elle ne se voit pas et casse les lecteurs stricts — la même marque a déjà fait échouer la
    /// lecture d'une configuration ailleurs dans ce projet.
    /// </remarks>
    [Fact]
    public void TheFileCarriesNoByteOrderMark()
    {
        AccueilOutil.Declarer(Application(Corps()), Plugins);

        var octets = File.ReadAllBytes(Path.Combine(Plugins, "comfy-desktop", "plugin.json"));

        Assert.Equal((byte)'{', octets[0]);
    }

    /// <summary>Une bibliothèque n'obtient pas de tuile, et l'écran dit pourquoi.</summary>
    [Fact]
    public void ALibraryGetsNoTileAndTheScreenSaysWhy()
    {
        var python = new ProgrammeReconnu(
            "Python", "Python", Corps("Python"), "", FormeProgramme.Bibliotheque,
            "4 exécutables à la racine : c'est une boîte à outils");

        Assert.Contains("pas de tuile", AccueilOutil.Declarer(python, Plugins), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Plugins, "python")));
    }

    /// <summary>
    /// Accueillir deux fois le même programme ne pose pas deux tuiles.
    /// </summary>
    /// <remarks>
    /// Réinstaller pour mettre à jour est le cas normal, pas l'exception. Deux boutons pour la même
    /// chose, dont l'un mourra en premier sans qu'on sache lequel, serait pire que pas de tuile.
    /// </remarks>
    [Fact]
    public void WelcomingTheSameProgramTwiceDoesNotLayTwoTiles()
    {
        var corps = Corps();

        AccueilOutil.Declarer(Application(corps), Plugins);

        var second = AccueilOutil.Declarer(Application(corps), Plugins);

        Assert.Contains("déjà déclaré", second, StringComparison.Ordinal);
        Assert.Single(PluginCatalog.Scan(Plugins).Loaded);
    }

    /// <summary>Un nom de dossier impossible devient un identifiant tenable.</summary>
    [Fact]
    public void AnImpossibleFolderNameBecomesAWorkableIdentifier()
    {
        AccueilOutil.Declarer(Application(Corps("Mon Éditeur 2026"), "Mon Éditeur"), Plugins);

        Assert.Equal("mon-diteur-2026", PluginCatalog.Scan(Plugins).Loaded.Single().Id);
    }
}

/// <summary>
/// La langue qu'on parle à chaque famille d'installeur.
/// </summary>
/// <remarks>
/// Le protocole ne force pas un installeur, il lui parle dans sa langue. Se tromper de langue ne
/// donne pas une erreur claire : NSIS ignore ce qu'il ne comprend pas et s'installe où il veut,
/// Windows Installer ouvre une fenêtre que personne ne verra.
/// </remarks>
public class CommandeInstalleurTests
{
    /// <summary>Un paquet Windows Installer se pilote par msiexec, jamais directement.</summary>
    [Fact]
    public void AWindowsInstallerPackageIsDrivenThroughMsiexec()
    {
        var commande = SenSÉ.Plugins.AccueilOutil.Commande(
            @"C:\Telechargements\LibreOffice.msi", @"C:\Program Files\SenSÉ\Outils\LibreOffice");

        Assert.EndsWith("msiexec.exe", commande.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/qn", commande.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    /// Il s'installe pour l'utilisateur, sinon il ne s'installe pas du tout.
    /// </summary>
    /// <remarks>
    /// Un MSI vise la machine entière par défaut, donc l'élévation — et Windows refuse de composer
    /// l'environnement d'un processus qu'il élève. Sans ces deux propriétés, l'accueil est
    /// impossible, pas seulement imparfait.
    /// </remarks>
    [Fact]
    public void ItInstallsForTheUserOrNotAtAll()
    {
        var commande = SenSÉ.Plugins.AccueilOutil.Commande(@"C:\x.msi", @"C:\Outils\X");

        Assert.Contains("MSIINSTALLPERUSER=1", commande.Arguments, StringComparison.Ordinal);
        Assert.Contains("ALLUSERS=2", commande.Arguments, StringComparison.Ordinal);
    }

    /// <summary>La destination d'un MSI supporte les espaces, parce qu'elle est entre guillemets.</summary>
    [Fact]
    public void AnMsiDestinationToleratesSpacesBecauseItIsQuoted()
        => Assert.Contains(
            @"INSTALLLOCATION=""C:\Program Files\SenSÉ\Outils\LibreOffice""",
            SenSÉ.Plugins.AccueilOutil.Commande(
                @"C:\x.msi", @"C:\Program Files\SenSÉ\Outils\LibreOffice").Arguments,
            StringComparison.Ordinal);

    /// <summary>Un installeur ordinaire garde la langue de NSIS.</summary>
    [Fact]
    public void AnOrdinaryInstallerKeepsTheNsisTongue()
    {
        var commande = SenSÉ.Plugins.AccueilOutil.Commande(
            @"C:\Telechargements\Setup.exe", Path.GetTempPath());

        Assert.EndsWith("Setup.exe", commande.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/S /D=", commande.Arguments, StringComparison.Ordinal);
    }
}
