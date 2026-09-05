using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Les deux façons de servir, et l'ordre de reprendre un travail commencé.
/// </summary>
/// <remarks>
/// Ce qui est éprouvé ici est ce que le modèle <b>lit</b> avant de répondre. Ce qu'il en fait
/// ensuite ne se teste pas sans le faire tourner, mais l'inverse se teste et vaut la peine : une
/// consigne absente du rappel n'a aucune chance d'être suivie, et c'était le cas de toutes celles
/// qui suivent avant ce jour.
/// </remarks>
[Collection(Carnets.Nom)]
public class ModeAssistantTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("mode");

    public ModeAssistantTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GuidedModeAsksForAChoiceBeforeActing()
    {
        var rappel = AssistantLocal.RappelTravaux(null, autonome: false);

        Assert.Contains("MODE GUIDÉ", rappel, StringComparison.Ordinal);
        Assert.Contains("proposer_choix", rappel, StringComparison.Ordinal);
    }

    [Fact]
    public void AutonomousModeSizesTheWorkAndDoesNotAskWhichTool()
    {
        var rappel = AssistantLocal.RappelTravaux(null, autonome: true);

        Assert.Contains("MODE AUTONOME", rappel, StringComparison.Ordinal);
        Assert.Contains("travail_noter", rappel, StringComparison.Ordinal);
        Assert.DoesNotContain("MODE GUIDÉ", rappel, StringComparison.Ordinal);
    }

    // L'option « fais-le toi-même » est posée par l'hôte à chaque choix. Le modèle n'a donc pas à
    // l'écrire, et on le lui dit — sinon elle apparaît deux fois, ou pas du tout.
    [Fact]
    public void GuidedModeTellsTheModelNotToWriteTheAutonomousOption()
        => Assert.Contains(
            "l'hôte l'ajoute",
            AssistantLocal.RappelTravaux(null, autonome: false),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Un travail accepté et inachevé se reprend, il ne se redemande pas.</summary>
    [Fact]
    public void AnAcceptedUnfinishedWorkOrdersAResume()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux", "lancer"], null);
        FichierTravail.Accepter("Vidéo par texte", null);
        FichierTravail.Cocher("Vidéo par texte", "composer le flux", null);

        var rappel = AssistantLocal.RappelTravaux(null);

        Assert.Contains("REPRISE", rappel, StringComparison.Ordinal);
        Assert.Contains("Vidéo par texte", rappel, StringComparison.Ordinal);

        // La prochaine étape est nommée, pas laissée à déduire d'une liste de cases.
        Assert.Contains("lancer", rappel, StringComparison.Ordinal);
    }

    // Un plan qui attend encore l'accord n'est pas un travail commencé : le reprendre seul
    // court-circuiterait exactement la garde que l'accord représente.
    [Fact]
    public void APlanAwaitingApprovalIsNotResumed()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux"], null);

        Assert.DoesNotContain("REPRISE", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);
    }

    [Fact]
    public void AFinishedWorkIsNotResumed()
    {
        FichierTravail.Noter("Vidéo par texte", ["composer le flux"], null);
        FichierTravail.Accepter("Vidéo par texte", null);
        FichierTravail.Cocher("Vidéo par texte", "composer le flux", null);

        Assert.DoesNotContain("REPRISE", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);
    }

    // Le rappel garde sa marque en tête quoi qu'il arrive : c'est elle qui permet de le retrouver
    // et de le remplacer au tour suivant, et un mode écrit devant la casserait.
    [Fact]
    public void TheMarkStaysFirstWhateverTheMode()
    {
        Assert.StartsWith("[travaux]", AssistantLocal.RappelTravaux(null, autonome: true), StringComparison.Ordinal);
        Assert.StartsWith("[travaux]", AssistantLocal.RappelTravaux(null, autonome: false), StringComparison.Ordinal);
    }
}
