using SteamXBox.Tools.Serveurs;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Ce que le produit s'autorise à tuer pour libérer la carte graphique.
/// </summary>
/// <remarks>
/// <b>Une éviction se juge à ce qu'elle rend, pas à ce qu'elle arrête.</b> Le balayage des serveurs
/// laissés par une session précédente existe pour une raison unique : douze gigaoctets de mémoire
/// vidéo ne tiennent pas deux gros consommateurs. Tout ce qui n'en occupe pas n'a rien à y faire.
///
/// <para>
/// La règle a été apprise à ses dépens le 22 août à 23:52:37. Le modèle de langage figurait dans la
/// liste ; l'ouverture du générateur l'a tué comme un reliquat, cinq secondes après qu'il eut
/// demandé un chemin d'accès à l'utilisateur. Celui-ci n'a jamais eu de réponse et a cru
/// l'assistant planté — le symptôme ne désignait rien de ce qui l'avait causé.
/// </para>
/// </remarks>
public class RessourcesTests
{
    /// <summary>Le modèle de langage n'est jamais évincé pour libérer la carte.</summary>
    /// <remarks>
    /// Il est chargé en <c>-ngl 0</c>, sur le processeur, sans exception possible : le tuer ne rend
    /// pas un mégaoctet de mémoire vidéo. Cela ne coûte que l'assistant, au moment précis où
    /// l'utilisateur s'en sert — car c'est en le pilotant vers le générateur qu'on déclenche
    /// l'éviction.
    /// </remarks>
    [Fact]
    public void TheLanguageModelIsNeverEvictedToFreeTheCard()
        => Assert.DoesNotContain(
            Ressources.Gourmands,
            g => g.EndsWith("llama-server.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Le générateur, lui, en occupe vraiment : il reste évinçable.</summary>
    /// <remarks>
    /// Sans cette moitié-là, vider la liste passerait l'épreuve précédente et on aurait troqué une
    /// faute contre une autre : deux serveurs se disputant la carte, sans que rien ne l'explique.
    /// </remarks>
    [Fact]
    public void TheGeneratorRemainsEvictable()
        => Assert.Contains(
            Ressources.Gourmands,
            g => g.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reconnus par leur chemin complet sous « Outils », jamais par leur nom.</summary>
    /// <remarks>
    /// Un <c>python.exe</c> installé ailleurs sur la machine ne nous appartient pas. Tuer celui du
    /// voisin serait bien pire que deux serveurs qui se gênent, et le rapprochement par nom seul
    /// rendrait cette faute inévitable sur toute machine où Python est installé — c'est-à-dire la
    /// plupart.
    /// </remarks>
    [Fact]
    public void TheyAreRecognisedByFullPathUnderTools()
        => Assert.All(
            Ressources.Gourmands,
            g =>
            {
                Assert.True(Path.IsPathFullyQualified(g), g);
                Assert.Contains(
                    Path.DirectorySeparatorChar + "Outils" + Path.DirectorySeparatorChar,
                    g,
                    StringComparison.Ordinal);
            });
}
