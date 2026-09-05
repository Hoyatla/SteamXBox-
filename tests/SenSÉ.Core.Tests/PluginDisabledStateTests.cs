using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// What the user decided about each tool, which is the one part of the uninstall screen that
/// outlives a session.
/// </summary>
/// <remarks>
/// Kept in the host's own storage rather than inside a tool's folder, because a folder has to stay
/// exactly what was dropped in — otherwise throwing it away no longer leaves nothing behind.
///
/// <para>
/// Every test uses an identifier of its own. They share one file on this machine and xUnit runs
/// them at the same time, so a shared name made them fail each other intermittently — which is
/// exactly the kind of test that gets ignored instead of fixed.
/// </para>
/// </remarks>
public class PluginDisabledStateTests
{
    private static string Fresh(string what) => $"test-SenSÉ-{what}-{Guid.NewGuid():N}";

    // Three states, not two: on, off, and never decided. Only the third takes the manifest's word.
    [Fact]
    public void AToolNobodyTouchedTakesItsOwnDefault()
    {
        var id = Fresh("untouched");

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: true));
        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: false));
    }

    // The reason the third state exists: a tool shipped idle has to be switchable on, and stay on.
    [Fact]
    public void AToolShippedIdleStaysOnOnceSwitchedOn()
    {
        var id = Fresh("idle");

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: false));

        PluginLifecycle.SetEnabled(id, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: false));
    }

    [Fact]
    public void SwitchingOffIsRemembered()
    {
        var id = Fresh("off");

        PluginLifecycle.SetEnabled(id, enabled: false);

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: true));
    }

    [Fact]
    public void SwitchingBackOnIsRememberedToo()
    {
        var id = Fresh("back-on");

        PluginLifecycle.SetEnabled(id, enabled: false);
        PluginLifecycle.SetEnabled(id, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(id, byDefault: true));
    }

    // Switching one must not switch the others.
    [Fact]
    public void EachToolIsSwitchedIndependently()
    {
        var one = Fresh("one");
        var two = Fresh("two");

        PluginLifecycle.SetEnabled(one, enabled: false);
        PluginLifecycle.SetEnabled(two, enabled: false);
        PluginLifecycle.SetEnabled(one, enabled: true);

        Assert.True(PluginLifecycle.IsEnabled(one, byDefault: true));
        Assert.False(PluginLifecycle.IsEnabled(two, byDefault: true));
    }

    [Fact]
    public void TheNameIsMatchedWithoutRegardToCase()
    {
        var id = Fresh("case");

        PluginLifecycle.SetEnabled(id.ToUpperInvariant(), enabled: false);

        Assert.False(PluginLifecycle.IsEnabled(id, byDefault: true));
    }

    /// <summary>
    /// Deux decisions prises en meme temps tiennent toutes les deux.
    /// </summary>
    /// <remarks>
    /// <b>Le defaut que ceci corrige.</b> Enregistrer une decision lit le fichier entier, y ajoute
    /// une entree et le reecrit. Deux appels simultanes lisaient donc le meme etat d'avant, et le
    /// second effacait la decision du premier. Rien n'echouait : l'entree manquait, l'outil
    /// reprenait son defaut, et le test qui venait de l'allumer le trouvait eteint.
    ///
    /// <para>
    /// C'est ce qui faisait tomber un test au hasard dans cette classe environ une execution sur
    /// cinq — jamais le meme, parce que le perdant est celui que l'ordonnanceur a servi en premier.
    /// Les identifiants uniques par test, deja en place, ne pouvaient rien y faire : ce qui etait
    /// partage n'etait pas le nom, c'etait le fichier.
    /// </para>
    ///
    /// <para>
    /// Ecrit en tache et non avec des attentes : la course est gagnee ou perdue en quelques
    /// microsecondes, donc le test ne l'attend pas, il la provoque — trente-deux ecrivains relaches
    /// ensemble, et les trente-deux decisions doivent se retrouver.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ConcurrentDecisionsDoNotOverwriteEachOther()
    {
        const int Writers = 32;

        var ids = Enumerable.Range(0, Writers).Select(i => Fresh($"race-{i}")).ToArray();

        // Ce que la classe dit quand elle n'arrive pas a enregistrer. Sans ca, une decision perdue
        // parce que le fichier n'a pas pu etre ecrit et une decision perdue parce qu'une autre l'a
        // ecrasee donnent exactement le meme echec, et ce sont deux defauts differents.
        var complaints = new System.Collections.Concurrent.ConcurrentBag<string>();

        using var start = new ManualResetEventSlim(false);

        var writes = ids.Select(id => Task.Run(() =>
        {
            start.Wait();
            PluginLifecycle.SetEnabled(id, enabled: true, log: complaints.Add);
        })).ToArray();

        start.Set();
        await Task.WhenAll(writes);

        var lost = ids.Where(id => !PluginLifecycle.IsEnabled(id, byDefault: false)).ToArray();
        var refused = complaints.Where(c => c.Contains("could not", StringComparison.Ordinal)).ToArray();

        Assert.True(
            lost.Length == 0,
            $"{lost.Length} decision(s) sur {Writers} perdues. "
            + (refused.Length == 0
                ? "Aucune n'a ete refusee a l'ecriture : elles se sont donc ecrasees entre elles."
                : $"{refused.Length} refusee(s) a l'ecriture : {string.Join(" | ", refused.Distinct().Take(3))}"));
    }
}
