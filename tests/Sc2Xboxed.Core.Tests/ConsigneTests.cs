using SteamXBox.Tools.Assistant;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// La consigne que le modèle reçoit, et les travaux qu'elle porte.
/// </summary>
/// <remarks>
/// <b>Le travail est un principe de fonctionnement, pas un outil parmi d'autres.</b> Une capacité
/// « relis ton carnet » supposait qu'un modèle de quatre milliards de paramètres pense à l'appeler.
/// Il n'y pense pas de façon fiable, et l'oubli ne se voit qu'après coup — quand il recommence une
/// étape déjà faite, c'est-à-dire quand il relance une génération de cinq minutes.
///
/// <para>
/// Écrire les travaux ouverts dans la consigne à chaque tour coûte une centaine de jetons et
/// supprime le problème au lieu de l'espérer résolu. Le modèle ne peut plus répondre sans avoir
/// sous les yeux ce qui est en cours.
/// </para>
/// </remarks>
[Collection(Carnets.Nom)]
public class ConsigneTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("consigne");

    public ConsigneTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Sans travail ouvert, la consigne le dit.</summary>
    /// <remarks>
    /// Le silence serait ambigu : le modèle ne saurait pas s'il n'y a rien en cours ou si on ne le
    /// lui a pas dit, et dans le doute il rattacherait une demande neuve à un travail imaginaire.
    /// </remarks>
    [Fact]
    public void WithNoOpenWorkTheInstructionSaysSo()
        => Assert.Contains(
            "Aucun travail ouvert", AssistantLocal.RappelTravaux(null), StringComparison.Ordinal);

    /// <summary>Les travaux ouverts sont sous ses yeux, avec ce qui est déjà coché.</summary>
    [Fact]
    public void OpenWorkIsPutInFrontOfIt()
    {
        FichierTravail.Noter("Animer trois photos", ["choisir", "animer", "monter"], null);
        FichierTravail.Cocher("Animer trois photos", "choisir", null);

        var consigne = AssistantLocal.Regles + AssistantLocal.RappelTravaux(null);

        Assert.Contains("Animer trois photos", consigne, StringComparison.Ordinal);
        Assert.Contains("[x] choisir", consigne, StringComparison.Ordinal);
        Assert.Contains("[ ] animer", consigne, StringComparison.Ordinal);
    }

    /// <summary>Faire est le cas normal ; noter est l'exception.</summary>
    /// <remarks>
    /// <b>La règle disait le contraire, et cela se voyait à l'usage.</b> « Deux étapes ou plus,
    /// oui » déclenchait un carnet sur presque tout : à « j'aimerais faire des vidéos », l'assistant
    /// répondait par un plan de cinq lignes et une demande d'accord, au lieu de demander quelle
    /// vidéo. L'utilisateur payait un tour d'attente pour apprendre ce qu'il savait déjà.
    /// </remarks>
    [Fact]
    public void DoingIsTheNormalCaseAndNotingTheException()
    {
        Assert.Contains("FAIS, PLUTÔT QUE DE NOTER", AssistantLocal.Regles, StringComparison.Ordinal);
        Assert.Contains("n'écris aucun carnet", AssistantLocal.Regles, StringComparison.Ordinal);
    }

    /// <summary>Un carnet couvre tout le fil, pas le dernier message.</summary>
    /// <remarks>
    /// Une demande s'étoffe en discutant : le dossier arrive au troisième message, le format au
    /// cinquième. Un carnet écrit sur le seul dernier message perdrait tout ce qui a été dit avant —
    /// et c'est précisément parce que la demande s'est étoffée qu'un carnet devenait nécessaire.
    /// </remarks>
    [Fact]
    public void ANotebookCoversTheWholeThread()
        => Assert.Contains(
            "depuis le début de la discussion",
            AssistantLocal.Regles,
            StringComparison.Ordinal);

    /// <summary>Trois issues sont offertes, pas deux.</summary>
    /// <remarks>
    /// Commencer, modifier, abandonner. Sans la deuxième, l'utilisateur qui voulait corriger une
    /// étape n'a que le choix de tout abandonner ou d'accepter un plan qu'il sait imparfait — et il
    /// accepte, parce que refaire coûte plus cher que subir.
    /// </remarks>
    [Fact]
    public void ThreeOutcomesAreOfferedNotTwo()
    {
        Assert.Contains("COMMENCER, MODIFIER ou ABANDONNER", AssistantLocal.Regles, StringComparison.Ordinal);
        Assert.Contains("réécris le carnet", AssistantLocal.Regles, StringComparison.Ordinal);
    }

    /// <summary>Rattacher au mauvais travail se demande, jamais ne se devine.</summary>
    /// <remarks>
    /// Mélanger deux sujets dans un carnet ne se voit pas tout de suite : personne ne s'en aperçoit
    /// avant que le carnet ne devienne illisible, et il est alors trop tard pour le démêler.
    /// </remarks>
    [Fact]
    public void AttachingToTheWrongWorkIsAskedNeverGuessed()
    {
        Assert.Contains("correspond déjà, continue-le", AssistantLocal.Regles, StringComparison.Ordinal);
        Assert.Contains("DEMANDE plutôt que de deviner", AssistantLocal.Regles, StringComparison.Ordinal);
    }

    /// <summary>L'assistant est celui du produit entier, pas d'un domaine.</summary>
    /// <remarks>
    /// SteamXBox recevra des outils de texte, de tableur, de courrier, de jeu. Une consigne qui
    /// parlerait des images aurait fait répondre « je ne sais pas faire » à la première demande
    /// d'un autre domaine, alors que l'outil serait installé et déclaré.
    /// </remarks>
    [Fact]
    public void TheAssistantBelongsToTheWholeProduct()
    {
        var consigne = AssistantLocal.Regles + AssistantLocal.RappelTravaux(null);

        Assert.Contains("tout entier", consigne, StringComparison.Ordinal);
        Assert.Contains("déclarant lui-même ce qu'il sait faire", consigne, StringComparison.Ordinal);
    }

    // Deux ou trois gestes ne méritent pas de carnet : noter chaque demande ferait un dossier de
    // carnets d'une ligne, et ferait attendre l'utilisateur pour rien.
    [Fact]
    public void AFewGesturesNeedNoNotebook()
        => Assert.Contains(
            "deux ou trois gestes",
            AssistantLocal.Regles,
            StringComparison.Ordinal);

    /// <summary>Plusieurs travaux ouverts apparaissent tous.</summary>
    /// <remarks>
    /// C'est la situation où le choix se pose vraiment : deux carnets, une demande ambiguë, et la
    /// consigne qui dit de demander plutôt que de deviner.
    /// </remarks>
    [Fact]
    public void SeveralOpenWorksAllAppear()
    {
        FichierTravail.Noter("Montage vidéo", ["a"], null);
        FichierTravail.Noter("Courrier du mois", ["b"], null);

        var consigne = AssistantLocal.Regles + AssistantLocal.RappelTravaux(null);

        Assert.Contains("Montage vidéo", consigne, StringComparison.Ordinal);
        Assert.Contains("Courrier du mois", consigne, StringComparison.Ordinal);
    }
}
