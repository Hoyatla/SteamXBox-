using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// L'aiguilleur : qui traite quoi, et à quel prix.
/// </summary>
/// <remarks>
/// Rien ici ne démarre de serveur. L'appel au modèle est injecté, ce qui permet d'éprouver la
/// seule chose qui se teste vraiment : ce que l'aiguilleur fait de la demande <i>avant</i> de le
/// consulter, et de sa réponse <i>après</i>.
///
/// <para>
/// Ce que le banc du 9 septembre 2026 a établi et que ces épreuves protègent : les trois exemples
/// de la consigne ne sont pas décoratifs — sans eux le même modèle tombait de 7/8 à 3/6 et
/// répondait parfois à la demande au lieu de l'aiguiller.
/// </para>
/// </remarks>
public class AiguilleurTests
{
    private static readonly string[] Voies =
        ["codage", "dialogue", "image", "transcription", "video"];

    /// <summary>Un fichier son se tranche sans réveiller personne.</summary>
    /// <remarks>
    /// <b>Ce qui se décide gratuitement ne doit pas coûter une génération.</b> À 232 ms l'appel,
    /// une règle sûre vaut mieux qu'un aller-retour — et celle-ci ne peut pas se tromper.
    /// </remarks>
    [Fact]
    public void ASoundFileIsDecidedWithoutTheModel()
    {
        var consulte = false;

        var choix = Aiguilleur.Decider(
            "Transcris C:/sons/reunion.wav en texte.",
            Voies,
            (_, _) => { consulte = true; return "dialogue"; });

        Assert.Equal("transcription", choix.Voie);
        Assert.False(consulte);
    }

    [Fact]
    public void AnExplicitScriptRequestIsDecidedWithoutTheModel()
    {
        var consulte = false;

        var choix = Aiguilleur.Decider(
            "Écris-moi un script qui renomme des fichiers.",
            Voies,
            (_, _) => { consulte = true; return "dialogue"; });

        Assert.Equal("codage", choix.Voie);
        Assert.False(consulte);
    }

    /// <summary>Ce que les règles ne tranchent pas va au modèle.</summary>
    [Fact]
    public void WhatTheRulesLeaveOpenGoesToTheModel()
    {
        var vu = "";

        var choix = Aiguilleur.Decider(
            "Anime cette photo pendant cinq secondes.",
            Voies,
            (_, demande) => { vu = demande; return "video"; });

        Assert.Equal("video", choix.Voie);
        Assert.Equal("Anime cette photo pendant cinq secondes.", vu);
    }

    /// <summary>Une voie hors liste est refusée, jamais rattrapée au plus proche.</summary>
    /// <remarks>
    /// Un aiguilleur qui devine ce qu'on a voulu dire finit par envoyer une vidéo au
    /// transcripteur, et l'erreur ne se voit qu'après plusieurs minutes de travail.
    /// </remarks>
    [Fact]
    public void ALaneOutsideTheListIsRefusedNotGuessed()
    {
        var choix = Aiguilleur.Decider("Fais quelque chose.", Voies, (_, _) => "vidéos");

        Assert.Equal("dialogue", choix.Voie);
        Assert.Contains("hors liste", choix.Raison, StringComparison.Ordinal);
    }

    /// <summary>Une voie absente de la machine n'est jamais proposée.</summary>
    /// <remarks>
    /// L'aiguilleur ne connaît que ce que la table déclare. Sans cela il enverrait au
    /// transcripteur une demande que rien n'écoute, et le modèle apprendrait à s'entêter.
    /// </remarks>
    [Fact]
    public void ALaneTheMachineDoesNotServeIsNeverChosen()
    {
        // Ni transcription ni video sur cette machine.
        string[] restreintes = ["codage", "dialogue"];

        var parLaRegle = Aiguilleur.Decider("Transcris reunion.wav.", restreintes, (_, _) => "dialogue");
        var parLeModele = Aiguilleur.Decider("Anime cette photo.", restreintes, (_, _) => "video");

        Assert.Equal("dialogue", parLaRegle.Voie);
        Assert.Equal("dialogue", parLeModele.Voie);
    }

    /// <summary>Chaque voie est définie, et pas seulement nommée.</summary>
    /// <remarks>
    /// <b>Une liste de mots nus s'est effondrée en service.</b> Le 9 septembre 2026, deux demandes
    /// d'actualités de suite sont parties à <c>transcription</c> ; remis au banc, le même aiguilleur
    /// ne faisait plus que <b>4 sur 10</b> et répondait « transcription » à presque tout — y compris
    /// « Quelle heure est-il à Tokyo ? ». Le modèle ne se trompait pas de jugement, il se trompait
    /// de vocabulaire : rien ne lui disait que <i>transcription</i> ne désigne que du son.
    ///
    /// <para>
    /// Une ligne de définition par voie a rendu <b>12 sur 12 en 359 ms</b>, contre 346 ms pour la
    /// liste nue — la justesse double pour treize millisecondes. Ce que chaque phrase achète est
    /// mesuré, d'où leur présence ici : la borne de <c>transcription</c> vaut à elle seule cinq des
    /// huit erreurs.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLaneIsDefinedAndNotMerelyNamed()
    {
        var consigne = "";

        Aiguilleur.Decider("Explique-moi une notion.", Voies, (c, _) => { consigne = c; return "dialogue"; });

        // Le biais mesure : une question technique n'est pas du code, et la prose va au dialogue.
        Assert.Contains("en prose", consigne, StringComparison.Ordinal);
        Assert.Contains("Pas les questions sur l'informatique", consigne, StringComparison.Ordinal);

        // La borne qui a corrige cinq erreurs sur huit.
        Assert.Contains("JAMAIS un document", consigne, StringComparison.Ordinal);

        // Chaque voie servie porte sa definition, pas seulement son nom.
        foreach (var voie in Voies)
        {
            Assert.Contains("- " + voie + " :", consigne, StringComparison.Ordinal);
        }
    }

    /// <summary>La consigne n'annonce que les voies servies.</summary>
    [Fact]
    public void TheInstructionAnnouncesOnlyTheLanesServed()
    {
        var consigne = "";
        string[] restreintes = ["codage", "dialogue"];

        Aiguilleur.Decider("Une demande quelconque.", restreintes, (c, _) => { consigne = c; return "dialogue"; });

        Assert.Contains("- codage :", consigne, StringComparison.Ordinal);
        Assert.Contains("- dialogue :", consigne, StringComparison.Ordinal);

        // Ni la voie, ni sa definition : une voie absente ne doit laisser aucune trace, sans quoi
        // le modele apprend un mot qu'il ne peut pas rendre.
        Assert.DoesNotContain("video", consigne, StringComparison.Ordinal);
        Assert.DoesNotContain("transcription", consigne, StringComparison.Ordinal);
    }

    /// <summary>Un aiguilleur qui tombe ne fait pas tomber la demande.</summary>
    /// <remarks>
    /// Le repli sur le dialogue est le comportement d'avant l'aiguillage : jamais une régression,
    /// alors qu'une exception remontée coûterait à l'utilisateur une demande qu'il devrait
    /// réécrire.
    /// </remarks>
    [Fact]
    public void ARouterThatFallsDoesNotTakeTheRequestWithIt()
    {
        var choix = Aiguilleur.Decider(
            "Une demande quelconque.",
            Voies,
            (_, _) => throw new HttpRequestException("serveur absent"));

        Assert.Equal("dialogue", choix.Voie);
    }

    // Sans aiguilleur branche, on ne devine pas : on dialogue, comme avant.
    [Fact]
    public void WithNoRouterAtAllTheDefaultIsDialogue()
    {
        Assert.Equal("dialogue", Aiguilleur.Decider("Bonjour.", Voies).Voie);
        Assert.Equal("dialogue", Aiguilleur.Decider("", Voies, (_, _) => "image").Voie);
        Assert.Equal("dialogue", Aiguilleur.Decider("Bonjour.", [], (_, _) => "image").Voie);
    }

    /// <summary>La réponse est nettoyée avant d'être lue.</summary>
    /// <remarks>
    /// Un modèle rend « codage. », « Codage » ou « codage\n » aussi volontiers que le mot nu.
    /// Refuser ces trois-là enverrait au dialogue des demandes correctement aiguillées.
    /// </remarks>
    [Theory]
    [InlineData("codage")]
    [InlineData("Codage")]
    [InlineData("  codage.  ")]
    [InlineData("\"codage\"\n")]
    public void TheAnswerIsCleanedBeforeItIsRead(string dit)
        => Assert.Equal("codage", Aiguilleur.Decider("Une demande.", Voies, (_, _) => dit).Voie);

    // Les trois exemples du banc sont rendus a l'appelant : sans eux le modele tombait de 7/8 a
    // 3/6, et repondait parfois a la demande au lieu de l'aiguiller.
    [Fact]
    public void TheThreeExamplesFromTheBenchAreOffered()
    {
        Assert.Equal(3, Aiguilleur.Exemples.Count);
        Assert.All(Aiguilleur.Exemples, e => Assert.Contains(e.Voie, Voies));
    }
}
