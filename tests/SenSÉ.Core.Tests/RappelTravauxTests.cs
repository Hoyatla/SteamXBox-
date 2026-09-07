using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Où les travaux ouverts sont rappelés au modèle, et pourquoi cet endroit compte.
/// </summary>
/// <remarks>
/// <b>Dix secondes par tour se jouaient là.</b> Le serveur garde en cache le début du dialogue d'une
/// question à l'autre : c'est ce qui évite de retraiter des milliers de jetons à chaque fois. Ce
/// cache porte sur un <em>préfixe</em> — il tient tant que le début ne bouge pas.
///
/// <para>
/// Les carnets ont d'abord été écrits dans le message système, donc en tête. Ils changent à chaque
/// cochage ; le début changeait donc à chaque cochage, et le cache tombait entièrement. Mesuré dans
/// une vraie session : 6 579 jetons à 1,46 ms le jeton, soit 9,6 s avant que le modèle n'écrive un
/// mot — payés de nouveau à chaque tour, et précisément pendant le travail en plusieurs étapes où la
/// lenteur pèse le plus.
/// </para>
/// </remarks>
[Collection(Carnets.Nom)]
public class RappelTravauxTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("rappel");

    public RappelTravauxTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Les règles ne bougent pas quand les carnets bougent.</summary>
    /// <remarks>
    /// C'est l'invariante qui protège le cache, et elle ne se voit pas à l'usage : un message
    /// système qui changerait donnerait exactement les mêmes réponses, seulement dix secondes plus
    /// tard. Une lenteur n'a pas l'air d'un défaut de conception — elle a l'air d'un petit
    /// ordinateur.
    /// </remarks>
    [Fact]
    public void TheRulesDoNotMoveWhenTheNotebooksDo()
    {
        var avant = AssistantLocal.Regles;

        FichierTravail.Noter("Animer trois photos", ["choisir", "animer"], null);
        FichierTravail.Cocher("Animer trois photos", "choisir", null);

        Assert.Equal(avant, AssistantLocal.Regles);
    }

    /// <summary>Les règles ne portent aucun carnet, à aucun moment.</summary>
    /// <remarks>
    /// La vérification tient à un détail : si un titre de carnet se retrouvait dans les règles, le
    /// test précédent passerait encore tant que le titre ne change pas, et la faute reviendrait sans
    /// bruit à la première conversation qui en ouvre un second.
    /// </remarks>
    [Fact]
    public void TheRulesCarryNoNotebook()
    {
        FichierTravail.Noter("Montage vidéo du mariage", ["a"], null);

        Assert.DoesNotContain("mariage", AssistantLocal.Regles, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sans carnet, le rappel le dit au lieu de se taire.</summary>
    /// <remarks>
    /// Le silence serait ambigu : le modèle ne saurait pas s'il n'y a rien en cours ou si on ne le
    /// lui a pas dit, et dans le doute il rattacherait une demande neuve à un travail imaginaire.
    /// </remarks>
    [Fact]
    public void WithNoNotebookTheReminderSaysSoRatherThanStayingSilent()
        => Assert.Contains(
            "Aucun travail ouvert", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);

    /// <summary>Le rappel porte l'état à jour, coché compris.</summary>
    [Fact]
    public void TheReminderCarriesTheCurrentState()
    {
        FichierTravail.Noter("Animer trois photos", ["choisir", "animer"], null);
        FichierTravail.Cocher("Animer trois photos", "choisir", null);

        var rappel = AssistantLocal.RappelTravaux(null);

        Assert.Contains("Animer trois photos", rappel, StringComparison.Ordinal);
        Assert.Contains("[x] choisir", rappel, StringComparison.Ordinal);
        Assert.Contains("[ ] animer", rappel, StringComparison.Ordinal);
    }

    /// <summary>Le rappel se reconnaît, pour être remplacé et non empilé.</summary>
    /// <remarks>
    /// Sans marque, les rappels s'accumuleraient et le modèle lirait l'état des carnets à trois
    /// tours d'écart — il croirait à trois travaux différents, dont deux périmés. La marque est en
    /// clair plutôt qu'un numéro de position : la conversation est élaguée par le début quand elle
    /// s'allonge, et tout index devient faux au premier élagage, silencieusement.
    /// </remarks>
    [Fact]
    public void TheReminderIsRecognisableSoItCanBeReplaced()
    {
        Assert.StartsWith("[travaux]", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);

        FichierTravail.Noter("Montage vidéo", ["a"], null);

        Assert.StartsWith("[travaux]", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);
    }
}
