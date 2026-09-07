using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le carnet de l'assistant, qui compense un contexte trop petit.
/// </summary>
/// <remarks>
/// Le modèle travaille sur huit mille jetons, partagés avec la conversation, la déclaration de tous
/// les outils et son propre raisonnement — et une seule image lui en coûte mille. Une demande en
/// dix étapes n'y tient pas : au huitième tour, le début a disparu. Écrire le plan et le relire
/// coûte quelques dizaines de jetons au lieu de tout garder.
///
/// <para>
/// Ce qui est éprouvé ici est ce qui doit être sûr : qu'un carnet réécrit ne perde pas le travail
/// accompli, et qu'un carnet ne disparaisse ni trop tôt ni jamais.
/// </para>
/// </remarks>
[Collection(Carnets.Nom)]
public class FichierTravailTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("carnet");

    public FichierTravailTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void APlanIsWrittenAndReadBack()
    {
        FichierTravail.Noter("Animer trois photos", ["choisir les photos", "animer", "monter"], null);

        var relu = FichierTravail.Lire("Animer trois photos", null);

        Assert.NotNull(relu);
        Assert.Equal(3, relu!.Taches.Count);
        Assert.Equal("choisir les photos", relu.Taches[0].Texte);
        Assert.All(relu.Taches, t => Assert.False(t.Faite));
    }

    /// <summary>Réécrire un plan ne perd pas ce qui était fait.</summary>
    /// <remarks>
    /// L'assistant reprécise son plan en cours de route — c'est même souhaitable. Si réécrire
    /// remettait tout à zéro, une demande révisée recommencerait le travail déjà accompli, et
    /// l'utilisateur verrait une génération de cinq minutes relancée pour rien.
    /// </remarks>
    [Fact]
    public void RewritingAPlanKeepsWhatWasDone()
    {
        FichierTravail.Noter("Montage", ["animer", "monter"], null);
        FichierTravail.Cocher("Montage", "animer", null);

        var revu = FichierTravail.Noter("Montage", ["animer", "monter", "agrandir"], null);

        Assert.True(revu.Taches[0].Faite);
        Assert.False(revu.Taches[1].Faite);
        Assert.Equal(3, revu.Taches.Count);
    }

    /// <summary>Cocher accepte une reformulation proche.</summary>
    /// <remarks>
    /// L'assistant recopie en reformulant. Exiger le mot à mot ferait échouer un cochage
    /// parfaitement clair, et le carnet dirait « rien de fait » sur un travail avancé.
    /// </remarks>
    [Theory]
    [InlineData("animer les photos")]
    [InlineData("animer")]
    public void TickingAcceptsACloseWording(string dit)
    {
        FichierTravail.Noter("Séquence", ["animer les photos", "monter la vidéo"], null);

        var coche = FichierTravail.Cocher("Séquence", dit, null);

        Assert.NotNull(coche);
        Assert.True(coche!.Taches[0].Faite);
    }

    [Fact]
    public void TickingSomethingThatIsNotThereSaysSo()
    {
        FichierTravail.Noter("Séquence", ["animer"], null);

        Assert.Null(FichierTravail.Cocher("Séquence", "repeindre la cuisine", null));
        Assert.Null(FichierTravail.Cocher("Carnet inconnu", "animer", null));
    }

    [Fact]
    public void AFinishedNotebookSaysItIsFinished()
    {
        FichierTravail.Noter("Deux choses", ["a", "b"], null);
        FichierTravail.Cocher("Deux choses", "a", null);

        Assert.False(FichierTravail.Lire("Deux choses", null)!.Fini);

        FichierTravail.Cocher("Deux choses", "b", null);

        Assert.True(FichierTravail.Lire("Deux choses", null)!.Fini);
    }

    /// <summary>Un carnet ne s'efface que si on le demande.</summary>
    /// <remarks>
    /// Tout cocher ne referme rien : c'est l'utilisateur qui juge du résultat, pas le modèle. Celui
    /// de ce produit a déjà annoncé un générateur lancé qui ne l'était pas.
    /// </remarks>
    [Fact]
    public void FinishingEveryStepDoesNotDeleteTheNotebook()
    {
        FichierTravail.Noter("Tout fait", ["a"], null);
        FichierTravail.Cocher("Tout fait", "a", null);

        Assert.NotNull(FichierTravail.Lire("Tout fait", null));
        Assert.True(FichierTravail.Effacer("Tout fait", null));
        Assert.Null(FichierTravail.Lire("Tout fait", null));
    }

    /// <summary>Un mois sans ouverture, et le carnet disparaît.</summary>
    /// <remarks>
    /// La seule preuve d'abandon qu'une machine puisse constater seule. Comptée depuis la dernière
    /// ouverture et non depuis la création : un travail repris chaque semaine ne vieillit jamais.
    /// </remarks>
    [Fact]
    public void ANotebookNobodyReopensForAMonthGoesAway()
    {
        FichierTravail.Noter("Vieux", ["a"], null);
        FichierTravail.Noter("Recent", ["b"], null);

        Vieillir("Vieux", DateTime.UtcNow - FichierTravail.Oubli - TimeSpan.FromDays(1));

        Assert.Equal(1, FichierTravail.Purger(null));
        Assert.Null(FichierTravail.Lire("Vieux", null));
        Assert.NotNull(FichierTravail.Lire("Recent", null));
    }

    // Juste sous la limite, il reste : un carnet de vingt-neuf jours est encore un travail en cours.
    [Fact]
    public void JustUnderTheLimitItStays()
    {
        FichierTravail.Noter("Limite", ["a"], null);
        Vieillir("Limite", DateTime.UtcNow - FichierTravail.Oubli + TimeSpan.FromDays(1));

        Assert.Equal(0, FichierTravail.Purger(null));
        Assert.NotNull(FichierTravail.Lire("Limite", null));
    }

    /// <summary>Relire un carnet le rajeunit.</summary>
    /// <remarks>
    /// C'est ce qui distingue « abandonné » de « long ». Sans cela, un travail qu'on reprend chaque
    /// semaine finirait effacé au bout d'un mois alors qu'il est vivant.
    /// </remarks>
    [Fact]
    public void ReopeningANotebookMakesItYoungAgain()
    {
        FichierTravail.Noter("Long", ["a"], null);
        Vieillir("Long", DateTime.UtcNow - FichierTravail.Oubli - TimeSpan.FromDays(1));

        FichierTravail.Lire("Long", null, toucher: true);

        Assert.Equal(0, FichierTravail.Purger(null));
        Assert.NotNull(FichierTravail.Lire("Long", null));
    }

    /// <summary>Un titre écrit par un modèle ne dérape pas hors du dossier.</summary>
    /// <remarks>
    /// Le titre vient du modèle, donc indirectement de l'utilisateur. Rien n'empêcherait une barre
    /// oblique ou deux points d'y arriver, et le nom de fichier en serait fait.
    /// </remarks>
    [Theory]
    [InlineData("../../windows/system32")]
    [InlineData("C:\\Windows\\note")]
    [InlineData("!!!")]
    public void ATitleFromAModelCannotEscapeTheFolder(string titre)
    {
        var fichier = FichierTravail.Fichier(titre);

        Assert.Equal(
            Path.GetFullPath(FichierTravail.Dossier),
            Path.GetFullPath(Path.GetDirectoryName(fichier)!));
    }

    [Fact]
    public void TheSummaryShowsWhatIsDone()
    {
        FichierTravail.Noter("Trois", ["a", "b", "c"], null);
        FichierTravail.Cocher("Trois", "b", null);

        var resume = FichierTravail.Resumer(FichierTravail.Lire("Trois", null)!);

        Assert.Contains("(1/3)", resume, StringComparison.Ordinal);
        Assert.Contains("[x] b", resume, StringComparison.Ordinal);
        Assert.Contains("[ ] a", resume, StringComparison.Ordinal);
    }

    /// <summary>Un carnet neuf attend l'accord, et le dit.</summary>
    /// <remarks>
    /// <b>Un carnet neuf est prêt à servir, il n'attend plus.</b> L'accord préalable protégeait
    /// d'un plan lancé sur une intention mal comprise ; il faisait surtout approuver une seconde
    /// fois une demande que l'utilisateur venait de formuler, et arrêtait l'assistant au milieu
    /// d'un travail commandé. Ce qui protège vraiment est ailleurs : rien ne s'installe, rien ne
    /// s'écrase, rien ne se referme sans qu'il le dise.
    /// </remarks>
    [Fact]
    public void ANewNotebookIsReadyToUse()
    {
        var note = FichierTravail.Noter("Animer trois photos", ["a", "b"], null);

        Assert.DoesNotContain(
            "EN ATTENTE", FichierTravail.Resumer(note), StringComparison.Ordinal);
    }

    /// <summary>Ce qui reste à faire est nommé, et c'est ce qui garde un carnet ouvert.</summary>
    /// <remarks>
    /// La fenêtre s'en sert pour dire ce qu'on perd en refermant, et <c>travail_terminer</c> pour
    /// refuser de refermer. Le 7 septembre 2026 un travail s'était rangé dans Finis avec sa
    /// dernière étape non faite : rien ne regardait.
    /// </remarks>
    [Fact]
    public void WhatIsLeftIsNamed()
    {
        FichierTravail.Noter("Animer trois photos", ["a", "b"], null);
        FichierTravail.Cocher("Animer trois photos", "a", null);

        var carnet = FichierTravail.Lire("Animer trois photos", null)!;

        Assert.Equal(["b"], FichierTravail.Restantes(carnet));
    }

    /// <summary>Un carnet tout coché n'a plus rien à retenir.</summary>
    [Fact]
    public void NothingIsLeftOnAFinishedNotebook()
    {
        FichierTravail.Noter("Animer trois photos", ["a"], null);
        FichierTravail.Cocher("Animer trois photos", "a", null);

        var carnet = FichierTravail.Lire("Animer trois photos", null)!;

        Assert.Empty(FichierTravail.Restantes(carnet));
    }

    /// <summary>Vieillit un carnet en réécrivant sa date d'ouverture.</summary>
    private static void Vieillir(string titre, DateTime quand)
    {
        var fichier = FichierTravail.Fichier(titre);
        var json = File.ReadAllText(fichier);
        var travail = System.Text.Json.JsonSerializer.Deserialize<Travail>(json)!;

        travail.Touche = quand;

        File.WriteAllText(fichier, System.Text.Json.JsonSerializer.Serialize(travail));
    }
}
