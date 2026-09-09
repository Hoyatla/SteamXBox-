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

    /// <summary>La consigne porte le correctif du biais mesuré.</summary>
    /// <remarks>
    /// L'unique erreur du banc était « Explique-moi la différence entre RAM et VRAM » envoyée au
    /// codage : pour un modèle de code, une question technique ressemble à du code. Perdre cette
    /// ligne rendrait l'erreur sans que rien ne le signale.
    /// </remarks>
    [Fact]
    public void TheInstructionCarriesTheFixForTheMeasuredBias()
    {
        var consigne = "";

        Aiguilleur.Decider("Explique-moi une notion.", Voies, (c, _) => { consigne = c; return "dialogue"; });

        Assert.Contains("en prose", consigne, StringComparison.Ordinal);
        Assert.Contains("technique", consigne, StringComparison.Ordinal);
    }

    /// <summary>La consigne n'annonce que les voies servies.</summary>
    [Fact]
    public void TheInstructionAnnouncesOnlyTheLanesServed()
    {
        var consigne = "";
        string[] restreintes = ["codage", "dialogue"];

        Aiguilleur.Decider("Une demande quelconque.", restreintes, (c, _) => { consigne = c; return "dialogue"; });

        Assert.Contains("codage, dialogue", consigne, StringComparison.Ordinal);
        Assert.DoesNotContain("video", consigne, StringComparison.Ordinal);
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
