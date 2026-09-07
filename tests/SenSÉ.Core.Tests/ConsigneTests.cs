using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

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

    /// <summary>Le plan s'énonce puis démarre, et reste corrigible.</summary>
    /// <remarks>
    /// Ce test demandait les trois issues « COMMENCER, MODIFIER ou ABANDONNER » posées avant
    /// d'agir. La question a été retirée : elle faisait approuver une seconde fois une demande
    /// déjà formulée, et arrêtait l'assistant au milieu d'un travail commandé.
    ///
    /// <para>Ce qui devait survivre, et que ce test garde : le plan est <b>énoncé</b>, donc
    /// lisible avant d'être subi, et il reste <b>corrigible</b> en cours de route. Sans cette
    /// seconde moitié, l'utilisateur qui veut changer une étape n'aurait plus qu'à tout laisser
    /// filer.</para>
    /// </remarks>
    [Fact]
    public void ThePlanIsAnnouncedThenStartedAndStaysCorrectable()
    {
        Assert.Contains("ÉNONCE le plan", AssistantLocal.Regles, StringComparison.Ordinal);
        Assert.Contains("réécris le carnet", AssistantLocal.Regles, StringComparison.Ordinal);
    }

    /// <summary>Ce qui ne part jamais de la seule initiative du modèle reste énuméré.</summary>
    /// <remarks>
    /// C'est ce qui remplace l'accord préalable : au lieu d'une permission demandée pour tout, une
    /// liste courte d'actes irréversibles pour lesquels il faut la demander. Perdre cette liste
    /// rendrait le retrait de l'accord dangereux au lieu de simplement plus rapide.
    /// </remarks>
    [Fact]
    public void TheIrreversibleActsStillNeedTheUser()
    {
        foreach (var acte in new[] { "installer un logiciel", "écraser un fichier", "fermer une application", "refermer un carnet" })
        {
            Assert.Contains(acte, AssistantLocal.Regles, StringComparison.Ordinal);
        }
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
    /// SenSÉ recevra des outils de texte, de tableur, de courrier, de jeu. Une consigne qui
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
