using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que l'Atelier offre au modèle, depuis que le générateur multimédia est parti.
/// </summary>
/// <remarks>
/// <b>Un verbe qui n'est pas déclaré n'existe pas pour le modèle.</b> Le générateur lui offrait
/// cinq verbes — <c>flux_modeles</c>, <c>flux_prets</c>, <c>flux_catalogue</c>, <c>flux_noeud</c>,
/// <c>flux_lancer</c> — et il a été retiré du produit. Trois avaient déjà leur équivalent côté
/// Atelier ; les deux autres non, <b>alors que les routes qui les servent existaient déjà</b>.
/// Elles étaient écrites et injoignables.
///
/// <para>
/// Ces épreuves tiennent le raccordement : ce que le générateur savait faire, l'Atelier le déclare.
/// Elles n'appellent aucun serveur — construire la liste des capacités n'en demande pas, et c'est
/// précisément ce qui rend la déclaration éprouvable.
/// </para>
/// </remarks>
public class AtelierCapacitesTests
{
    private static IReadOnlyList<string> Verbes()
        => [.. AssistantAtelier.Creer().Select(c => c.Nom)];

    /// <summary>Ce que le générateur savait faire, l'Atelier le déclare.</summary>
    /// <remarks>
    /// Un par un, et nommés : c'est la liste que le modèle perdait quand le générateur est parti,
    /// et la retrouver au complet est tout l'objet de ce raccordement.
    /// </remarks>
    [Theory]
    [InlineData("atelier_modeles", "flux_modeles : quels modèles sont installés")]
    [InlineData("atelier_gabarits", "flux_prets : ce qui est déjà tout fait")]
    [InlineData("atelier_gabarit_charger", "reprendre un tout-fait plutôt que recomposer")]
    [InlineData("atelier_catalogue_types", "flux_catalogue : quels nœuds existent")]
    [InlineData("atelier_type", "flux_noeud : comment paramétrer celui-ci")]
    [InlineData("atelier_executer_graphe", "flux_lancer : exécuter")]
    [InlineData("atelier_essayer_noeud", "flux_verifier : éprouver un réglage sans tout lancer")]
    public void WhatTheGeneratorCouldDoTheWorkbenchNowDeclares(string verbe, string quoi)
        => Assert.True(Verbes().Contains(verbe), $"{verbe} manque — {quoi}");

    /// <summary>Plus aucun verbe ne porte le vocabulaire du générateur.</summary>
    /// <remarks>
    /// <b>Deux vocabulaires pour un seul geste, et le modèle en choisit un au hasard.</b> C'est
    /// arrivé le 9 septembre 2026 avec les deux verbes de recherche web : celui qu'il a pris
    /// perdait toutes les sources. La leçon vaut ici — l'établi a changé, le vocabulaire aussi,
    /// et il n'en reste qu'un.
    /// </remarks>
    [Fact]
    public void NoVerbCarriesTheOldGeneratorsVocabulary()
    {
        var restes = Verbes().Where(v => v.StartsWith("flux_", StringComparison.Ordinal)
                                         || v.StartsWith("generateur_", StringComparison.Ordinal));

        Assert.Empty(restes);
    }

    /// <summary>Chaque verbe dit à quoi il sert, et non seulement ce qu'il fait.</summary>
    /// <remarks>
    /// La mesure de l'aiguilleur l'a montré ailleurs le même jour : une liste de noms nus fait
    /// tomber la justesse de 12/12 à 4/10. Un verbe sans description utile est le même défaut à
    /// une plus petite échelle.
    /// </remarks>
    [Fact]
    public void EveryVerbSaysWhatItIsFor()
    {
        foreach (var capacite in AssistantAtelier.Creer())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(capacite.Description),
                capacite.Nom + " n'a pas de description");

            Assert.True(
                capacite.Description.Length >= 40,
                capacite.Nom + " : description trop courte pour situer son usage");
        }
    }

    // Deux capacites du meme nom se masqueraient l'une l'autre, et laquelle gagne dependrait de
    // l'ordre de construction — donc de rien de lisible.
    [Fact]
    public void NoTwoVerbsShareAName()
    {
        var verbes = Verbes();

        Assert.Equal(verbes.Count, verbes.Distinct(StringComparer.Ordinal).Count());
    }
}
