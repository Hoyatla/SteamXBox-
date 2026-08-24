using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Archiver, remettre et éjecter un outil dont le corps vit ailleurs que son manifeste.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci ferme.</b> Archiver compressait <c>Plugins\&lt;id&gt;</c> — la description,
/// un kilooctet — et laissait les cinq cents mégaoctets du programme intacts dans <c>Outils</c>. La
/// place annoncée comme libérée ne l'était pas, et personne ne pouvait s'en apercevoir depuis
/// l'écran.
/// </remarks>
public class CycleDeVieCorpsTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("cycle");

    // La storage de l'hôte est déjà déplacée pour toute la série par TestHostStorage. La déplacer
    // une seconde fois ici la faisait changer sous les pieds des autres classes, que xUnit exécute
    // en parallèle : quatre épreuves tombaient, dont aucune n'était fausse.
    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Plugins => Path.Combine(_bac.FullName, "Plugins");

    private string Corps => Path.Combine(_bac.FullName, "Outils", "Comfy-Desktop");

    /// <summary>Un outil hébergé : une description légère, un corps lourd.</summary>
    private void Outil()
    {
        var manifeste = Path.Combine(Plugins, "comfy-desktop");

        Directory.CreateDirectory(manifeste);
        File.WriteAllText(
            Path.Combine(manifeste, "plugin.json"),
            """
            { "id": "comfy-desktop", "name": "Comfy Desktop", "category": "tool",
              "version": "1.0.0", "surface": "tile", "does": "application",
              "target": "{tools}\\Comfy-Desktop\\Comfy Desktop.exe" }
            """);

        Directory.CreateDirectory(Corps);
        File.WriteAllText(Path.Combine(Corps, "Comfy Desktop.exe"), new string('x', 4096));
    }

    /// <summary>Archiver emporte le corps, pas seulement la description.</summary>
    [Fact]
    public void ArchivingTakesTheBodyAndNotOnlyTheDescription()
    {
        Outil();

        Assert.True(PluginLifecycle.Archive(Plugins, "comfy-desktop", null, Corps));

        Assert.False(Directory.Exists(Corps), "le corps doit avoir quitté le disque");
        Assert.True(File.Exists(PluginLifecycle.ArchiveCorps(Corps, "comfy-desktop")));
    }

    /// <summary>L'archive du corps se range à côté du corps, pas des descriptions.</summary>
    /// <remarks>
    /// Cinq cents mégaoctets dans le dossier des manifestes le rendraient plus lourd que tout le
    /// reste, et une sauvegarde des descriptions emporterait les programmes sans le vouloir.
    /// </remarks>
    [Fact]
    public void TheBodyArchiveIsKeptBesideTheBody()
        => Assert.StartsWith(
            Path.Combine(_bac.FullName, "Outils"),
            PluginLifecycle.ArchiveCorps(Corps, "comfy-desktop"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Remettre en place rend le corps aussi.</summary>
    [Fact]
    public void RestoringBringsTheBodyBackToo()
    {
        Outil();
        PluginLifecycle.Archive(Plugins, "comfy-desktop", null, Corps);

        Assert.True(PluginLifecycle.Restore(Plugins, "comfy-desktop", null, Corps));

        Assert.Equal(4096, new FileInfo(Path.Combine(Corps, "Comfy Desktop.exe")).Length);
    }

    /// <summary>Éjecter emporte le corps, sinon il reste sans rien pour le nommer.</summary>
    [Fact]
    public void EjectingTakesTheBodyOrItStaysWithNothingToNameIt()
    {
        Outil();

        Assert.True(PluginLifecycle.Delete(Plugins, "comfy-desktop", null, Corps));
        Assert.False(Directory.Exists(Corps));
    }

    /// <summary>
    /// Si le corps ne peut pas être rangé, la description reste en place.
    /// </summary>
    /// <remarks>
    /// L'ordre n'est pas un détail : ranger la description d'abord, puis échouer sur le corps,
    /// laisserait un outil sans nom et ses cinq cents mégaoctets toujours là — le pire des deux
    /// états, et celui dont on ne se relève pas sans fouiller le disque.
    /// </remarks>
    [Fact]
    public void IfTheBodyCannotBePackedTheDescriptionStays()
    {
        Outil();

        using var tenu = File.Open(
            Path.Combine(Corps, "Comfy Desktop.exe"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.False(PluginLifecycle.Archive(Plugins, "comfy-desktop", null, Corps));
        Assert.True(Directory.Exists(Path.Combine(Plugins, "comfy-desktop")), "la description doit rester");
    }

    /// <summary>Un outil livré, qui n'a pas de corps déclaré, se comporte comme avant.</summary>
    [Fact]
    public void AShippedToolWithNoDeclaredBodyBehavesAsBefore()
    {
        Outil();

        Assert.True(PluginLifecycle.Archive(Plugins, "comfy-desktop"));

        Assert.True(Directory.Exists(Corps), "sans corps déclaré, rien d'autre ne bouge");
        Assert.True(PluginLifecycle.HasArchive(Plugins, "comfy-desktop"));
    }
}
