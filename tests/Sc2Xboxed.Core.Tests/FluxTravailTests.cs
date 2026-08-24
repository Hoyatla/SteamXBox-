using System.Text.Json;
using SteamXBox.Plugins;
using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Le verbe qui fait d'un flux de génération un outil réglable.
/// </summary>
/// <remarks>
/// Ce qui est éprouvé ici est la lecture de la cible et la pose des réglages : tout ce qui se passe
/// avant le réseau. La soumission au serveur ne l'est pas, et c'est assumé — elle demanderait un
/// ComfyUI qui tourne, plusieurs gigaoctets de modèles et plusieurs minutes par cas.
///
/// <para>
/// La part qui casse en silence est de toute façon celle-ci. Un flux soumis avec de mauvaises
/// valeurs aboutit : le fichier arrive, il a l'air correct, et rien ne dit que le choix de
/// l'utilisateur n'a pas été appliqué. Une soumission qui échoue, elle, se voit tout de suite.
/// </para>
/// </remarks>
public class FluxTravailTests
{
    /// <summary>Un flux réduit, de la même forme que ceux du serveur.</summary>
    private const string Flux = """
        {
          "2": { "class_type": "LoadImage", "inputs": { "image": "example.png" } },
          "4": { "class_type": "SVD_img2vid_Conditioning",
                 "inputs": { "init_image": ["2", 0], "video_frames": 14, "fps": 6 } },
          "6": { "class_type": "KSampler",
                 "inputs": { "seed": 42, "steps": 20, "cfg": 3.0, "add_noise": true } }
        }
        """;

    [Fact]
    public void FluxIsPartOfTheVocabulary()
        => Assert.Contains(PluginActions.Flux, PluginActions.Known);

    // Un flux se règle avec ce que l'utilisateur a désigné : une image, une longueur. Une tuile n'a
    // pas de panneau pour les désigner, donc une tuile qui nommerait ce verbe serait un bouton qui
    // ne pourrait jamais rien faire.
    [Fact]
    public void FluxNeedsAPanelAndATarget()
    {
        Assert.True(PluginActions.NeedsPanel(PluginActions.Flux));
        Assert.True(PluginActions.NeedsTarget(PluginActions.Flux));
    }

    [Fact]
    public void TheTargetIsAPathThenSettings()
    {
        var lecture = FluxTravail.Lire(@"C:\flux.json|4.video_frames=25|!2.image=chat.png");

        Assert.Equal("", lecture.Faute);
        Assert.Equal(@"C:\flux.json", lecture.Chemin);
        Assert.Equal(2, lecture.Reglages.Count);

        Assert.Equal(new ReglageFlux("4", "video_frames", "25", false), lecture.Reglages[0]);
        Assert.Equal(new ReglageFlux("2", "image", "chat.png", true), lecture.Reglages[1]);
    }

    /// <summary>Une barre verticale tapée par l'utilisateur reste dans son invite.</summary>
    /// <remarks>
    /// La cible sépare ses réglages par des barres, et le panneau y substitue du texte libre : une
    /// invite parfaitement ordinaire — « un chat roux | style aquarelle » — ouvrait donc un réglage
    /// que personne n'avait écrit, et le travail ne partait pas. Le panneau échappe désormais ce
    /// qu'il substitue (voir <c>Segments</c>) ; ce qui est vérifié ici est l'autre moitié de la
    /// règle, celle qui la relit.
    /// </remarks>
    [Fact]
    public void AVerticalBarTypedByTheUserStaysInsideItsValue()
    {
        var lecture = FluxTravail.Lire(
            @"C:\flux.json|6.text=un chat roux \| style aquarelle|4.video_frames=25");

        Assert.Equal("", lecture.Faute);
        Assert.Equal(2, lecture.Reglages.Count);

        Assert.Equal(
            new ReglageFlux("6", "text", "un chat roux | style aquarelle", false),
            lecture.Reglages[0]);

        Assert.Equal(new ReglageFlux("4", "video_frames", "25", false), lecture.Reglages[1]);
    }

    // Un réglage mal écrit dans un manifeste doit se voir. Ignoré, il donne une génération qui
    // aboutit avec les valeurs d'origine : personne ne saurait que le choix n'a pas été appliqué.
    [Theory]
    [InlineData(@"C:\flux.json|4.video_frames")]
    [InlineData(@"C:\flux.json|video_frames=25")]
    [InlineData(@"C:\flux.json|4.=25")]
    public void AnUnreadableSettingIsRefusedRatherThanSkipped(string cible)
        => Assert.NotEqual("", FluxTravail.Lire(cible).Faute);

    /// <summary>Le type vient du flux : un entier reste entier.</summary>
    /// <remarks>
    /// ComfyUI refuse <c>20.0</c> là où il attend un nombre de pas. Comme un manifeste n'a que des
    /// chaînes à offrir, c'est la valeur déjà inscrite dans le flux qui dit de quel type il s'agit.
    /// </remarks>
    [Fact]
    public void AnIntegerStaysAnInteger()
    {
        var pose = Poser(("6", "steps", "30"));

        Assert.Equal(JsonValueKind.Number, pose.ValueKind);
        Assert.Equal("30", pose.GetRawText());
    }

    [Fact]
    public void ADecimalStaysADecimal()
    {
        var pose = Poser(("6", "cfg", "4.5"));

        Assert.Equal(4.5, pose.GetDouble());
        Assert.Contains(".", pose.GetRawText(), StringComparison.Ordinal);
    }

    // Un entier écrit dans un champ décimal garde le point : le flux dit que c'est un décimal, et
    // c'est le flux qui a raison.
    [Fact]
    public void AWholeNumberWrittenIntoADecimalKeepsItsKind()
        => Assert.Equal(5d, Poser(("6", "cfg", "5")).GetDouble());

    [Fact]
    public void AStringIsPlacedAsWritten()
        => Assert.Equal("chat.png", Poser(("2", "image", "chat.png")).GetString());

    [Fact]
    public void FrenchYesMeansTrue()
    {
        Assert.Equal(JsonValueKind.False, Poser(("6", "add_noise", "false")).ValueKind);
        Assert.Equal(JsonValueKind.True, Poser(("6", "add_noise", "oui")).ValueKind);
        Assert.Equal(JsonValueKind.True, Poser(("6", "add_noise", "vrai")).ValueKind);
    }

    // Un champ que l'utilisateur laisse vide garde la valeur du flux. C'est ainsi que le flux
    // fournit lui-même ses valeurs par défaut, sans que le manifeste ait à les recopier — et à les
    // laisser diverger.
    [Fact]
    public void AnEmptyValueLeavesTheFlowAlone()
        => Assert.Equal(14, Poser(("4", "video_frames", "")).GetInt32());

    [Fact]
    public void ARequiredSettingLeftEmptyStopsEverything()
    {
        FluxTravail.Appliquer(Flux, [new ReglageFlux("2", "image", "", true)], out var faute);

        Assert.NotNull(faute);
        Assert.Contains("2.image", faute, StringComparison.Ordinal);
    }

    /// <summary>Un manifeste qui a glissé par rapport à son flux se voit au premier clic.</summary>
    /// <remarks>
    /// C'est la protection contre la dérive : le flux est un fichier que quelqu'un peut rouvrir dans
    /// l'éditeur, renuméroter et réenregistrer, pendant que le manifeste, lui, continue de nommer
    /// les anciens nœuds. La faute nomme le nœud manquant, ce qui suffit à corriger la cible.
    /// </remarks>
    [Theory]
    [InlineData("99", "steps", "nœud")]
    [InlineData("6", "etapes", "entrée")]
    public void ASettingThatTheFlowDoesNotHaveIsNamed(string noeud, string entree, string attendu)
    {
        FluxTravail.Appliquer(Flux, [new ReglageFlux(noeud, entree, "5", false)], out var faute);

        Assert.NotNull(faute);
        Assert.Contains(attendu, faute, StringComparison.Ordinal);
    }

    // Un tableau, dans un flux, est un câble : ["2", 0] veut dire « la sortie 0 du nœud 2 ». Y poser
    // une valeur débrancherait le graphe, et l'erreur qui suivrait parlerait d'un type manquant à
    // l'autre bout — très loin de la vraie cause.
    [Fact]
    public void AWireIsNotASetting()
    {
        FluxTravail.Appliquer(Flux, [new ReglageFlux("4", "init_image", "chat.png", false)], out var faute);

        Assert.NotNull(faute);
        Assert.Contains("câble", faute, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWhereANumberIsExpectedIsRefused()
    {
        FluxTravail.Appliquer(Flux, [new ReglageFlux("6", "steps", "beaucoup", false)], out var faute);

        Assert.NotNull(faute);
        Assert.Contains("nombre", faute, StringComparison.Ordinal);
    }

    /// <summary>Le format de l'éditeur est reconnu et nommé.</summary>
    /// <remarks>
    /// C'est la confusion que tout le monde fait une fois : « Save » enregistre le graphe de
    /// l'interface, « Save (API format) » celui que le serveur accepte. Le serveur, lui, répond une
    /// erreur de validation qui ne parle jamais de format, et on cherche la faute dans le flux
    /// pendant une demi-heure.
    /// </remarks>
    [Fact]
    public void TheEditorFormatIsRecognisedAndNamed()
    {
        FluxTravail.Appliquer("""{"nodes":[],"links":[]}""", [], out var faute);

        Assert.NotNull(faute);
        Assert.Contains("API", faute, StringComparison.Ordinal);
    }

    /// <summary>Un fichier désigné est recopié là où le serveur ira le chercher.</summary>
    /// <remarks>
    /// Un nœud <c>LoadImage</c> ne connaît pas les chemins : il nomme un fichier dans le dossier
    /// <c>input</c> du serveur. Le préfixe évite d'écraser un fichier que l'utilisateur y aurait
    /// déjà déposé sous le même nom.
    /// </remarks>
    [Fact]
    public void ADesignatedFileIsCopiedUnderAPrefixedName()
    {
        var bac = Directory.CreateTempSubdirectory("flux");

        try
        {
            var source = Path.Combine(bac.FullName, "chat.png");
            var vers = Path.Combine(bac.FullName, "entrees");
            File.WriteAllText(source, "image");

            var sortis = FluxTravail.Deposer([new ReglageFlux("2", "image", source, true)], vers, null, out var depot);

            Assert.Null(depot);
            Assert.Equal("steamxbox-chat.png", sortis[0].Valeur);
            Assert.True(File.Exists(Path.Combine(vers, "steamxbox-chat.png")));
        }
        finally
        {
            bac.Delete(recursive: true);
        }
    }

    /// <summary>Un fichier déjà déposé n'est pas recopié.</summary>
    /// <remarks>
    /// <b>Constaté à l'usage, et c'est ce qui a cassé une session.</b> Relancer sur la même image
    /// pendant qu'une génération tournait faisait échouer la copie : le serveur tenait le fichier
    /// ouvert, et Windows refusait l'écrasement. Or recopier n'apportait rien — le fichier était
    /// déjà là et identique. La seule chose que la copie produisait, c'était l'échec.
    /// </remarks>
    [Fact]
    public void AFileAlreadyDepositedIsNotCopiedAgain()
    {
        var bac = Directory.CreateTempSubdirectory("flux");

        try
        {
            var source = Path.Combine(bac.FullName, "chat.png");
            var vers = Path.Combine(bac.FullName, "entrees");
            File.WriteAllText(source, "image");

            FluxTravail.Deposer([new ReglageFlux("2", "image", source, true)], vers, null, out _);

            var depose = Path.Combine(vers, "steamxbox-chat.png");
            var quand = new FileInfo(depose).LastWriteTimeUtc;

            // Le dossier est verrouillé comme le ferait le serveur qui lit l'image : toute copie
            // par-dessus échouerait. La bonne réponse est de ne pas copier du tout.
            using (var tenu = new FileStream(depose, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var sortis = FluxTravail.Deposer(
                    [new ReglageFlux("2", "image", source, true)], vers, null, out var faute);

                Assert.Null(faute);
                Assert.Equal("steamxbox-chat.png", sortis[0].Valeur);
            }

            Assert.Equal(quand, new FileInfo(depose).LastWriteTimeUtc);
        }
        finally
        {
            bac.Delete(recursive: true);
        }
    }

    /// <summary>Un dépôt impossible arrête tout, au lieu de laisser passer le chemin local.</summary>
    /// <remarks>
    /// <b>La faute qui a coûté une session de test.</b> La version d'avant gardait la valeur
    /// d'origine et soumettait quand même ; le serveur recevait un chemin Windows complet, qu'un
    /// nœud de chargement ne sait pas résoudre. Il répondait « Invalid image file » en nommant un
    /// fichier qui existe pourtant, et rien ne reliait ce refus à une copie manquée trois secondes
    /// plus tôt.
    /// </remarks>
    [Fact]
    public void ADepositThatCannotHappenStopsEverything()
    {
        var bac = Directory.CreateTempSubdirectory("flux");

        try
        {
            var source = Path.Combine(bac.FullName, "chat.png");
            File.WriteAllText(source, "image");

            // Un fichier là où le dossier des entrées devrait être : la création du dossier échoue,
            // donc la copie aussi.
            var vers = Path.Combine(bac.FullName, "entrees");
            File.WriteAllText(vers, "pas un dossier");

            var sortis = FluxTravail.Deposer(
                [new ReglageFlux("2", "image", source, true)], vers, null, out var faute);

            Assert.NotNull(faute);
            Assert.Contains("chat.png", faute, StringComparison.Ordinal);

            // Et surtout : le chemin local n'est jamais rendu comme s'il était utilisable.
            Assert.DoesNotContain(sortis, r => r.Valeur == source);
        }
        finally
        {
            bac.Delete(recursive: true);
        }
    }

    // Une valeur qui n'est pas un fichier traverse sans être touchée : « 25 » reste « 25 ».
    [Fact]
    public void AValueThatIsNotAFileIsLeftAlone()
    {
        var sortis = FluxTravail.Deposer([new ReglageFlux("4", "video_frames", "25", false)], "", null, out _);

        Assert.Equal("25", sortis[0].Valeur);
    }

    /// <summary>Pose un réglage et rend ce que le flux porte ensuite.</summary>
    private static JsonElement Poser((string Noeud, string Entree, string Valeur) reglage)
    {
        var sorti = FluxTravail.Appliquer(
            Flux,
            [new ReglageFlux(reglage.Noeud, reglage.Entree, reglage.Valeur, false)],
            out var faute);

        Assert.Null(faute);

        using var document = JsonDocument.Parse(sorti);

        return document.RootElement
            .GetProperty(reglage.Noeud)
            .GetProperty("inputs")
            .GetProperty(reglage.Entree)
            .Clone();
    }
}
