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

    /// <summary>Une liste numérotée fait autant d'étapes qu'elle en porte.</summary>
    /// <remarks>
    /// <b>C'est l'écriture que le modèle choisit réellement.</b> La consigne demande des
    /// points-virgules ; à « Ouvre LibreOffice, écris Bonjour et sauvegarde. Puis ouvre le
    /// dossier », il a rendu les quatre étapes numérotées et séparées par des sauts de ligne. Le
    /// carnet affichait alors <c>0/1</c>, l'assistant cochait son étape unique après la première
    /// action, et les trois autres n'existaient pour personne.
    /// </remarks>
    [Fact]
    public void ANumberedListMakesAsManyStepsAsItCarries()
    {
        var etapes = FichierTravail.Decouper(
            "1. Ouvrir LibreOffice\n2. Écrire \"Bonjour\"\n3. Sauvegarder\n4. Ouvrir le dossier");

        Assert.Equal(
            ["Ouvrir LibreOffice", "Écrire \"Bonjour\"", "Sauvegarder", "Ouvrir le dossier"],
            etapes);
    }

    // Le point-virgule que la consigne demande marche toujours : le correctif ajoute une écriture,
    // il n'en remplace pas une.
    [Fact]
    public void TheSemicolonTheInstructionAsksForStillWorks()
        => Assert.Equal(
            ["choisir", "animer", "monter"],
            FichierTravail.Decouper("choisir; animer; monter"));

    // Les deux à la fois, parce qu'un modèle mélange les deux dans la même réponse.
    [Fact]
    public void BothWritingsAtOnce()
        => Assert.Equal(
            ["a", "b", "c"],
            FichierTravail.Decouper("- a; - b\n* c"));

    /// <summary>Un nombre au milieu d'une étape n'est pas une puce.</summary>
    /// <remarks>
    /// La puce n'est retirée que suivie d'une espace. Sans cette condition, « 3.5 mm » perdrait son
    /// « 3. » et l'étape mentirait sur la mesure qu'elle porte.
    /// </remarks>
    [Fact]
    public void ANumberInsideAStepIsNotABullet()
        => Assert.Equal(
            ["Régler l'épaisseur à 3.5 mm"],
            FichierTravail.Decouper("Régler l'épaisseur à 3.5 mm"));

    // Rien à découper ne rend rien, plutôt qu'une étape vide qui s'afficherait comme une case à
    // cocher sans texte.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";;\n\n;")]
    public void NothingToCutYieldsNothing(string liste)
        => Assert.Empty(FichierTravail.Decouper(liste));

    /// <summary>Un carnet déjà écrit de travers se répare tout seul à la lecture.</summary>
    /// <remarks>
    /// <b>Corriger l'écriture ne suffisait pas : le carnet du jour restait piégé.</b> Il portait une
    /// étape unique dont le texte était les quatre lignes numérotées. Le carnet s'affichait
    /// <c>1/1</c>, <see cref="FichierTravail.Cocher"/> reconnaissait cette étape sur son début et la
    /// cochait entière, le travail passait pour fini — et les trois appels suivants échouaient sur
    /// un carnet qui affirmait pourtant contenir ces étapes.
    /// </remarks>
    [Fact]
    public void ANotebookAlreadyWrittenBadlyHealsItselfOnRead()
    {
        FichierTravail.Noter(
            "LibreOffice",
            ["1. Ouvrir LibreOffice\n2. Écrire « Bonjour »\n3. Sauvegarder\n4. Ouvrir le dossier"],
            null);

        var carnet = FichierTravail.Lire("LibreOffice", null)!;

        Assert.Equal(
            ["Ouvrir LibreOffice", "Écrire « Bonjour »", "Sauvegarder", "Ouvrir le dossier"],
            carnet.Taches.Select(t => t.Texte));
    }

    /// <summary>La coche de l'étape fourre-tout ne vaut que pour la première.</summary>
    /// <remarks>
    /// L'assistant a coché cette étape après avoir fait la première chose qu'elle nommait ; ce geste
    /// ne dit rien des trois autres. Les rouvrir peut faire refaire une étape, les fermer ferait
    /// perdre le travail sans que personne ne s'en aperçoive.
    /// </remarks>
    [Fact]
    public void TheTickOfACatchAllStepCountsOnlyForTheFirst()
    {
        FichierTravail.Noter("LibreOffice", ["1. Ouvrir\n2. Écrire\n3. Sauvegarder"], null);
        FichierTravail.Cocher("LibreOffice", "1. Ouvrir", null);

        var carnet = FichierTravail.Lire("LibreOffice", null)!;

        Assert.True(carnet.Taches[0].Faite);
        Assert.Equal(["Écrire", "Sauvegarder"], FichierTravail.Restantes(carnet));
        Assert.False(carnet.Fini);
    }

    // La réparation est écrite sur le disque, pas seulement rendue : sans cela le carnet
    // s'afficherait juste et se cocherait de travers, ce qui est la pire des deux situations.
    [Fact]
    public void TheRepairIsWrittenDownNotJustReturned()
    {
        FichierTravail.Noter("LibreOffice", ["1. Ouvrir\n2. Écrire"], null);
        FichierTravail.Lire("LibreOffice", null);

        // Le saut de ligne échappé, pas les vrais : le fichier est indenté et en contient
        // légitimement. Ce qui doit avoir disparu, c'est celui qui était DANS le texte d'une étape.
        Assert.DoesNotContain(
            "\\n", File.ReadAllText(FichierTravail.Fichier("LibreOffice")), StringComparison.Ordinal);
    }

    /// <summary>La liste répare aussi, parce que c'est par elle que passe la reprise.</summary>
    /// <remarks>
    /// <c>Lister</c> désérialise sans passer par <c>Lire</c>, et ce sont pourtant ses carnets que
    /// lisent le rappel de la consigne et la reprise après contexte plein. Soigné d'un côté et lu
    /// de travers de l'autre, le carnet aurait menti là où il compte le plus.
    /// </remarks>
    [Fact]
    public void TheListingRepairsToo()
    {
        FichierTravail.Noter("LibreOffice", ["1. Ouvrir\n2. Écrire\n3. Sauvegarder"], null);

        var carnet = FichierTravail.Lister(null).Single(c => c.Titre == "LibreOffice");

        Assert.Equal(["Ouvrir", "Écrire", "Sauvegarder"], carnet.Taches.Select(t => t.Texte));
    }

    // Un carnet bien écrit n'est pas touché : la réparation ne doit pas réécrire tous les fichiers
    // à chaque lecture, ni changer une étape qui contient légitimement une ponctuation.
    [Fact]
    public void AWellWrittenNotebookIsLeftAlone()
    {
        FichierTravail.Noter("Propre", ["choisir", "animer"], null);
        FichierTravail.Cocher("Propre", "choisir", null);

        var carnet = FichierTravail.Lire("Propre", null)!;

        Assert.Equal(["choisir", "animer"], carnet.Taches.Select(t => t.Texte));
        Assert.True(carnet.Taches[0].Faite);
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
