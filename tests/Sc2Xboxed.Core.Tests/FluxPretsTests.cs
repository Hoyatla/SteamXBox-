using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Ce qui évite à l'assistant de recomposer un graphe qui existe déjà.
/// </summary>
/// <remarks>
/// <b>Mesuré le 24 août.</b> On demande une vidéo ; l'assistant compose un graphe de zéro en dix-sept
/// appels d'outil, se fait rattraper dix fautes par le vérificateur, obtient enfin un flux valide, et
/// réclame trente-six gigaoctets sur une carte qui en a douze. Un graphe Wan écrit, validé et taillé
/// pour cette carte dormait dans le dossier des flux ; rien ne le lui disait.
/// </remarks>
public class FluxPretsTests
{
    private const string Wan = """
        {
          "1": { "class_type": "UnetLoaderGGUF", "inputs": { "unet_name": "Wan2.2-HighNoise-Q4.gguf" } },
          "2": { "class_type": "CLIPLoaderGGUF", "inputs": { "clip_name": "umt5-Q4.gguf", "type": "wan" } },
          "3": { "class_type": "LoadImage", "inputs": { "image": "example.png" } },
          "4": { "class_type": "SaveVideo", "inputs": { "video": ["3", 0], "filename_prefix": "wan" } }
        }
        """;

    private static bool Tout(string _) => true;

    [Fact]
    public void AFlowNamesItsWeightsAndItsNodeCount()
    {
        var pret = FluxPrets.Examiner("Wan22.json", Wan, Tout);

        Assert.NotNull(pret);
        Assert.Equal("Wan22.json", pret!.Nom);
        Assert.Equal(4, pret.Noeuds);
        Assert.Equal(["Wan2.2-HighNoise-Q4.gguf", "umt5-Q4.gguf"], pret.Modeles);
        Assert.True(pret.Lancable);
    }

    /// <summary>L'image d'exemple d'un LoadImage n'est pas un modèle manquant.</summary>
    /// <remarks>
    /// Elle est remplacée au lancement par celle que l'utilisateur désigne. La compter écarterait
    /// tous les flux image-vers-vidéo, c'est-à-dire précisément ceux qu'on veut proposer.
    /// </remarks>
    [Fact]
    public void TheSampleImageIsNotTakenForAModel()
        => Assert.DoesNotContain(
            "example.png", FluxPrets.Examiner("Wan22.json", Wan, Tout)!.Modeles);

    [Fact]
    public void AMissingWeightMakesTheFlowUnrunnable()
    {
        var pret = FluxPrets.Examiner("Wan22.json", Wan, nom => nom.StartsWith("umt5", StringComparison.Ordinal));

        Assert.False(pret!.Lancable);
        Assert.Equal(["Wan2.2-HighNoise-Q4.gguf"], pret.Manquants);
    }

    // Le format de l'éditeur porte « nodes » et « links » et n'est pas soumettable : le proposer
    // enverrait le modèle lancer quelque chose que le serveur refuse.
    [Fact]
    public void TheEditorFormatIsNotOffered()
        => Assert.Null(FluxPrets.Examiner(
            "editeur.json", """{ "nodes": [], "links": [], "version": 1 }""", Tout));

    [Fact]
    public void UnreadableJsonIsIgnoredRatherThanThrowing()
        => Assert.Null(FluxPrets.Examiner("casse.json", "{ pas du json", Tout));

    [Fact]
    public void TheSummaryLeadsWithWhatCanRunAndNamesWhatCannot()
    {
        var lancable = FluxPrets.Examiner("Wan22.json", Wan, Tout)!;
        var absent = FluxPrets.Examiner("Autre.json", Wan, _ => false)!;

        var texte = FluxPrets.Resumer([lancable, absent]);

        Assert.Contains("Wan22.json", texte, StringComparison.Ordinal);
        Assert.Contains("Non lançables ici", texte, StringComparison.Ordinal);
        Assert.Contains("Autre.json", texte, StringComparison.Ordinal);

        // Le lançable est nommé avant celui qui ne l'est pas : c'est l'ordre de lecture d'un modèle
        // qui doit choisir vite.
        Assert.True(
            texte.IndexOf("Wan22.json", StringComparison.Ordinal)
            < texte.IndexOf("Autre.json", StringComparison.Ordinal));
    }

    [Fact]
    public void NoFlowAtAllSaysSoRatherThanStayingSilent()
        => Assert.Contains("Aucun flux prêt", FluxPrets.Resumer([]), StringComparison.Ordinal);
}
