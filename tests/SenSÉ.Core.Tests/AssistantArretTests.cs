using System.Text.Json.Nodes;
using SenSÉ.Plugins;
using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que le bouton « Arrêter » garantit.
/// </summary>
/// <remarks>
/// <b>Il manquait au moment où il servait le plus.</b> L'assistant est parti sur un diagnostic faux
/// — un avertissement de démarrage pris pour une panne — et a proposé d'installer une bibliothèque
/// Python pour réparer un problème inexistant. Rien ne permettait de l'interrompre avant qu'il
/// n'aille au bout de son enchaînement.
/// </remarks>
[Collection(Carnets.Nom)]
public class AssistantArretTests
{
    /// <summary>Un arrêt déjà demandé n'exécute aucun tour, et ne joint même pas le modèle.</summary>
    /// <remarks>
    /// C'est la propriété qui compte : l'arrêt est vérifié avant l'appel au serveur, donc il tient
    /// même quand le modèle n'est pas chargé — sans quoi « Arrêter » aurait attendu la fin d'un
    /// chargement de plusieurs minutes avant de faire quoi que ce soit.
    /// </remarks>
    [Fact]
    public void AnAlreadyCancelledRunExecutesNothing()
    {
        using var arret = new CancellationTokenSource();
        arret.Cancel();

        var lances = 0;

        var reponse = new AssistantLocal().Repondre(
            "anime mes photos",
            [],
            [],
            (_, _) => { lances++; return ""; },
            null,
            arret.Token);

        Assert.Equal(0, lances);
        Assert.Contains("Arrêté à votre demande", reponse, StringComparison.Ordinal);
    }
}
