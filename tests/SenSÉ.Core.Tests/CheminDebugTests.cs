using SenSÉ.Core.Diagnostics;
using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Où partent les traces, et quand elles s'en vont.
/// </summary>
/// <remarks>
/// <b>Ce que ces épreuves protègent est une panne muette.</b> Un signal est un rendez-vous entre
/// deux processus : l'un l'écrit, l'autre l'attend. Quand seul l'un des deux déménage, rien ne
/// casse bruyamment — le bouton Menu de la manette cesse simplement de faire quoi que ce soit, et
/// le clavier n'entend plus l'ordre de se fermer. C'est arrivé pendant le déplacement vers
/// <c>Debug/</c>, et c'est précisément ce qu'un chemin recopié à deux endroits finit toujours par
/// produire.
/// </remarks>
public class CheminDebugTests
{
    /// <summary>Les trois écrivains et les trois lecteurs pointent le même fichier.</summary>
    /// <remarks>
    /// Le nom vient de <see cref="OskInstanceNaming"/> et porte déjà son extension ; d'autres
    /// appelants passent le radical seul. Les deux formes doivent désigner le même endroit, sans
    /// quoi l'un écrirait un <c>osk-close.signal.signal</c> que personne n'attend.
    /// </remarks>
    [Fact]
    public void ASignalNameLandsInOnePlaceWhicheverFormItTakes()
    {
        var naming = OskInstanceNaming.FromSuffix("b3784bec");

        Assert.Equal(
            CheminDebug.Signal("osk-close-b3784bec"),
            CheminDebug.Signal(naming.CloseSignalFile));

        Assert.EndsWith(
            ".signal", CheminDebug.Signal(naming.CloseSignalFile), StringComparison.Ordinal);

        Assert.DoesNotContain(
            ".signal.signal", CheminDebug.Signal(naming.CloseSignalFile), StringComparison.Ordinal);
    }

    /// <summary>Les traces vivent sous <c>Debug/</c>, jamais à la racine du produit.</summary>
    /// <remarks>
    /// C'est la demande d'origine : la racine porte les binaires, et un dossier où l'on cherche
    /// <c>SenSÉ.Desktop.exe</c> parmi quarante journaux n'est plus un dossier d'installation.
    /// </remarks>
    [Fact]
    public void EveryDiagnosticFileLivesUnderDebug()
    {
        foreach (var chemin in new[]
        {
            CheminDebug.Signal("osk-exit"),
            CheminDebug.OskLog("Xbox", "790c71b1"),
            CheminDebug.DebugLog("desktop"),
        })
        {
            Assert.StartsWith(CheminDebug.Racine, chemin, StringComparison.Ordinal);
            Assert.NotEqual(
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetDirectoryName(chemin));
        }
    }

    /// <summary>Le ménage emporte ce qui est vieux et laisse ce qui est frais.</summary>
    /// <remarks>
    /// <b>C'est toute la raison de purger par âge plutôt que de tout vider à l'ouverture.</b> Un
    /// <c>.signal</c> frais est un <i>ordre en vol</i> : le clavier met plusieurs secondes à se
    /// lever, et une bascule pressée pendant ce temps écrit son signal avant que le guetteur
    /// n'existe. Un balayage au démarrage avalerait cette première pression — le défaut que
    /// <c>SenSÉ.Osk</c> documente et corrige déjà de son côté.
    /// </remarks>
    [Fact]
    public void HousekeepingTakesTheOldAndLeavesTheOrderInFlight()
    {
        CheminDebug.AssurerRacine();

        var vieux = CheminDebug.Signal("epreuve-vieux");
        var frais = CheminDebug.Signal("epreuve-frais");

        try
        {
            File.WriteAllText(vieux, "vieux");
            File.WriteAllText(frais, "frais");
            File.SetLastWriteTimeUtc(vieux, DateTime.UtcNow - TimeSpan.FromDays(45));

            CheminDebug.Purger(TimeSpan.FromDays(30));

            Assert.False(File.Exists(vieux), "une trace de 45 jours n'apprend plus rien a personne");
            Assert.True(File.Exists(frais), "un signal frais est un ordre en vol, pas une trace");
        }
        finally
        {
            foreach (var reste in new[] { vieux, frais })
            {
                try { if (File.Exists(reste)) File.Delete(reste); } catch { }
            }
        }
    }

    // Un dossier absent n'est pas une panne : le menage tourne au demarrage, avant que quoi que ce
    // soit n'ait eu l'occasion d'ecrire.
    [Fact]
    public void HousekeepingOnAnEmptyInstallationIsNotAFault()
        => Assert.Equal(0, CheminDebug.Purger(TimeSpan.FromDays(3650)));

    /// <summary>Un mois, comme demandé.</summary>
    [Fact]
    public void TheExpiryIsOneMonth()
        => Assert.Equal(30, CheminDebug.Peremption.TotalDays);
}
