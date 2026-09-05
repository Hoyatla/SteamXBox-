using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Switching a tool off, packing it away and throwing it out.
/// </summary>
/// <remarks>
/// These three do different things on purpose, and the tests exist because the differences are the
/// whole feature: one is instant to undo, one frees space and is recoverable, and one destroys
/// something. Anything that blurred them would make the screen dishonest.
/// </remarks>
public class PluginLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PluginLifecycleTests() => Install("essai");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Install(string id)
    {
        var folder = Path.Combine(_root, id);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, PluginCatalog.ManifestFileName), $$"""
            {
              "id": "{{id}}", "name": "Essai", "category": "tool", "version": "1.0.0",
              "licence": "MIT", "glyph": "E916", "surface": "tile", "does": "clear-screen"
            }
            """);

        File.WriteAllText(Path.Combine(folder, "notes.txt"), "une ressource de l'outil");
    }

    private string Folder(string id) => Path.Combine(_root, id);

    [Fact]
    public void AnInstalledToolIsListedAsEnabled()
    {
        var entry = Assert.Single(PluginLifecycle.List(_root));

        Assert.Equal("essai", entry.Id);
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.True(entry.Bytes > 0);
    }

    // Switching off must not move anything: it is the difference between "not now" and "gone".
    [Fact]
    public void SwitchingOffLeavesTheFolderExactlyWhereItWas()
    {
        PluginLifecycle.SetEnabled("essai", enabled: false);

        try
        {
            Assert.True(Directory.Exists(Folder("essai")));
            Assert.True(File.Exists(Path.Combine(Folder("essai"), "notes.txt")));
            Assert.False(PluginLifecycle.IsEnabled("essai", byDefault: true));
        }
        finally
        {
            PluginLifecycle.SetEnabled("essai", enabled: true);
        }
    }

    [Fact]
    public void ArchivingCompressesTheFolderAndRemovesIt()
    {
        Assert.True(PluginLifecycle.Archive(_root, "essai"));

        Assert.False(Directory.Exists(Folder("essai")));
        Assert.True(PluginLifecycle.HasArchive(_root, "essai"));

        var entry = Assert.Single(PluginLifecycle.List(_root));
        Assert.Equal(PluginState.Archived, entry.State);
    }

    // An archive nobody can unpack is a deletion with extra steps.
    [Fact]
    public void AnArchivedToolComesBackWithEverythingInIt()
    {
        PluginLifecycle.Archive(_root, "essai");

        Assert.True(PluginLifecycle.Restore(_root, "essai"));

        Assert.True(Directory.Exists(Folder("essai")));
        Assert.True(File.Exists(Path.Combine(Folder("essai"), "notes.txt")));
        Assert.Equal("une ressource de l'outil", File.ReadAllText(Path.Combine(Folder("essai"), "notes.txt")));

        // The archive is consumed by restoring: two copies of one tool is a question nobody asked.
        Assert.False(PluginLifecycle.HasArchive(_root, "essai"));
    }

    // The rule the user asked for by name: ejecting removes the tool, not its archive.
    [Fact]
    public void EjectingRemovesTheFolderAndKeepsTheArchive()
    {
        PluginLifecycle.Archive(_root, "essai");
        PluginLifecycle.Restore(_root, "essai");
        PluginLifecycle.Archive(_root, "essai");
        PluginLifecycle.Restore(_root, "essai");

        // Archive again, then put a copy back beside it so both exist at once.
        PluginLifecycle.Archive(_root, "essai");
        Install("essai");

        Assert.True(PluginLifecycle.Delete(_root, "essai"));

        Assert.False(Directory.Exists(Folder("essai")));
        Assert.True(PluginLifecycle.HasArchive(_root, "essai"));
    }

    [Fact]
    public void ForgettingIsTheOnlyStepThatDestroysTheArchive()
    {
        PluginLifecycle.Archive(_root, "essai");

        Assert.True(PluginLifecycle.Forget(_root, "essai"));
        Assert.False(PluginLifecycle.HasArchive(_root, "essai"));
        Assert.Empty(PluginLifecycle.List(_root));
    }

    // The archive folder holds no manifest, so the scan must walk past it rather than complain.
    [Fact]
    public void TheArchiveFolderIsNotMistakenForATool()
    {
        PluginLifecycle.Archive(_root, "essai");

        var scan = PluginCatalog.Scan(_root);

        Assert.Empty(scan.Loaded);
        Assert.Empty(scan.Rejected);
    }

    // Only tools. A cursor pack changes a Windows setting and has no tile to switch off; listing it
    // here invited turning off something whose effect is somewhere else entirely.
    [Fact]
    public void AWindowsThemeIsNotListedAmongTheTools()
    {
        var folder = Path.Combine(_root, "curseurs");
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, PluginCatalog.ManifestFileName), """
            {
              "id": "curseurs", "name": "Curseurs", "category": "theme.windows",
              "version": "1.0.0", "licence": "GPL-3.0", "revertible": true
            }
            """);

        var listed = PluginLifecycle.List(_root);

        Assert.Single(listed);
        Assert.Equal("essai", listed[0].Id);
    }

    // An identifier comes from a file somebody downloaded; it must not choose where the host writes.
    [Fact]
    public void AnIdentifierCannotEscapeTheFolder()
    {
        var outside = Path.Combine(Path.GetTempPath(), "SenSÉ-escape.zip");

        try
        {
            PluginLifecycle.Archive(_root, @"..\..\SenSÉ-escape");

            Assert.False(File.Exists(outside));
        }
        finally
        {
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }
        }
    }
}
