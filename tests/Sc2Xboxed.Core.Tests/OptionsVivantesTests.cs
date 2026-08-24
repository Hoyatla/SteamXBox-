using SteamXBox.Plugins;
using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Ce qui empêche un outil de mourir quand les modèles changent.
/// </summary>
/// <remarks>
/// Un manifeste qui énumère des noms de fichiers gèle l'installation du jour où il a été écrit.
/// Les modèles, eux, changent au gré des besoins : un client en retire un, en ajoute deux, et
/// l'outil réclame toujours un fichier disparu — sans se plaindre au démarrage, puis en échouant
/// au premier clic sur un nom que personne ne reconnaît.
///
/// <para>
/// Ce n'est pas une crainte de principe. Deux flux de cette machine nommaient encore
/// <c>wan2.1_i2v_480p_14B</c>, <c>umt5_xxl</c> et <c>wan_2.1_vae</c>, tous retirés du disque depuis
/// longtemps, et rien ne l'avait signalé.
/// </para>
/// </remarks>
public class OptionsVivantesTests
{
    private static PluginManifest Outil(string defaut) => new()
    {
        Id = "animer",
        Name = "Animer",
        Category = "tool",
        Version = "1.0.0",
        Licence = "Proprietary",
        Glyph = "E786",
        Surface = "panel",
        Remembers = ["modele"],
        Content =
        [
            new PluginContentItem
            {
                Kind = "choice",
                Id = "modele",
                Label = "Modèle",
                Value = defaut,
                From = "CheckpointLoaderSimple.ckpt_name",
                Options = ["repli.safetensors"],
                Hint = "Ce qui est installé ici.",
            },
            new PluginContentItem { Kind = "action", Label = "Animer", Does = "flux", Target = "x" },
        ],
    };

    /// <summary>Ce que la machine répond aujourd'hui.</summary>
    private static IReadOnlyList<string> Installes(string designation)
        => designation == "CheckpointLoaderSimple.ckpt_name"
            ? ["nouveau.safetensors", "autre.safetensors"]
            : [];

    private static PluginContentItem Champ(PluginManifest manifeste)
        => manifeste.Content.First(c => c.Id == "modele");

    [Fact]
    public void TheChoicesComeFromTheMachineRatherThanFromTheManifest()
    {
        var champ = Champ(OptionsVivantes.Resoudre(Outil("nouveau.safetensors"), Installes, null));

        Assert.Equal(["nouveau.safetensors", "autre.safetensors"], champ.Options);
        Assert.Equal("nouveau.safetensors", champ.Value);
    }

    /// <summary>Une valeur par défaut qui n'existe plus glisse sur ce qui existe.</summary>
    /// <remarks>
    /// C'est le cœur du mécanisme. Sans cela, l'outil s'ouvrirait sur un modèle absent et
    /// échouerait au clic en nommant un fichier que l'utilisateur n'a jamais choisi — le message le
    /// plus difficile à relier à quoi que ce soit.
    /// </remarks>
    [Fact]
    public void ADefaultThatIsGoneSlidesOntoWhatIsThere()
    {
        var dit = new List<string>();
        var champ = Champ(OptionsVivantes.Resoudre(
            Outil("wan2.1_i2v_480p_14B.safetensors"), Installes, dit.Add));

        Assert.Equal("nouveau.safetensors", champ.Value);

        // Le remplacement est dit : sinon l'utilisateur voit son réglage changer tout seul, sans
        // trace nulle part de la raison.
        Assert.Contains(dit, m => m.Contains("wan2.1", StringComparison.Ordinal)
            && m.Contains("nouveau.safetensors", StringComparison.Ordinal));
    }

    /// <summary>Le générateur muet ne vide pas le panneau.</summary>
    /// <remarks>
    /// Ouvrir un panneau ne doit pas coûter les deux minutes d'un démarrage de serveur, et surtout
    /// pas quand l'utilisateur venait seulement regarder. Un panneau périmé vaut mieux qu'un
    /// panneau vide.
    /// </remarks>
    [Fact]
    public void ASilentGeneratorLeavesTheManifestValuesInPlace()
    {
        var dit = new List<string>();
        var champ = Champ(OptionsVivantes.Resoudre(Outil("repli.safetensors"), _ => [], dit.Add));

        Assert.Equal(["repli.safetensors"], champ.Options);
        Assert.Contains(dit, m => m.Contains("ne rend rien", StringComparison.Ordinal));
    }

    /// <summary>Le manifeste partagé n'est jamais réécrit.</summary>
    /// <remarks>
    /// Un manifeste chargé sert à la grille, aux panneaux et à l'assistant. Le modifier depuis l'un
    /// d'eux ferait dépendre ce que voient les autres de l'ordre dans lequel on a cliqué — le genre
    /// de couplage qui produit des défauts qu'on ne reproduit jamais deux fois.
    /// </remarks>
    [Fact]
    public void TheSharedManifestIsNeverRewritten()
    {
        var origine = Outil("wan2.1_i2v_480p_14B.safetensors");

        OptionsVivantes.Resoudre(origine, Installes, null);

        Assert.Equal("wan2.1_i2v_480p_14B.safetensors", Champ(origine).Value);
        Assert.Equal(["repli.safetensors"], Champ(origine).Options);
    }

    // Un manifeste sans choix dynamique traverse sans copie ni travail.
    [Fact]
    public void AManifestWithoutDynamicChoicesIsLeftAlone()
    {
        var simple = new PluginManifest { Id = "x", Content = [new PluginContentItem { Kind = "text" }] };

        Assert.Same(simple, OptionsVivantes.Resoudre(simple, Installes, null));
    }

    // Tout ce que porte un champ doit survivre à la copie : une explication perdue en chemin ne se
    // voit nulle part, et c'est le lecteur qui la paie.
    [Fact]
    public void NothingIsLostInTheCopy()
    {
        var champ = Champ(OptionsVivantes.Resoudre(Outil("nouveau.safetensors"), Installes, null));

        Assert.Equal("Modèle", champ.Label);
        Assert.Equal("Ce qui est installé ici.", champ.Hint);
        Assert.Equal("CheckpointLoaderSimple.ckpt_name", champ.From);
    }

    /// <summary>Un choix qui déclare seulement un 'from' est accepté par le catalogue.</summary>
    /// <remarks>
    /// La règle d'avant exigeait des options écrites, ce qui interdisait justement le manifeste
    /// qu'on cherche à rendre possible.
    /// </remarks>
    [Fact]
    public void AChoiceMayDeclareOnlyAFrom()
    {
        var manifeste = Outil("");
        Champ(manifeste).Options = [];

        Assert.Null(PluginCatalog.Validate(manifeste));
    }

    [Theory]
    [InlineData("", "sans options, sans 'from' ni recettes")]
    [InlineData("ckpt_name", "NomDuNoeud.nom_de_l_entree")]
    public void AChoiceThatNamesNothingUsableIsRefused(string from, string attendu)
    {
        var manifeste = Outil("");
        Champ(manifeste).Options = [];
        Champ(manifeste).From = from;

        Assert.Contains(attendu, PluginCatalog.Validate(manifeste) ?? "", StringComparison.Ordinal);
    }

    /// <summary>Une volée de manifestes ne dérange le générateur qu'une seule fois.</summary>
    /// <remarks>
    /// <b>C'est une faute qui ne se voyait pas dans le résultat.</b> Les manifestes rendus étaient
    /// corrects ; seul le nombre d'appels était faux. Il se payait ailleurs : sur cette machine, un
    /// refus de connexion en boucle locale met deux secondes à revenir — mesuré, et identique sur
    /// un port témoin libre, donc c'est un filtre réseau et non le générateur. Quatre outils de
    /// génération ajoutaient ainsi huit secondes à chaque question posée à l'assistant, générateur
    /// éteint, avant même qu'il ne commence à réfléchir.
    /// </remarks>
    [Fact]
    public void AFlightOfManifestsAsksTheMachineOnlyOnce()
    {
        var appels = 0;

        OptionsVivantes.Resoudre(
            [Outil("a.safetensors"), Outil("b.safetensors"), Outil("c.safetensors")],
            () => { appels++; return null; },
            null);

        Assert.Equal(1, appels);
    }

    /// <summary>Sans aucun choix dynamique, la machine n'est pas dérangée du tout.</summary>
    [Fact]
    public void WithNoDynamicChoiceTheMachineIsLeftAlone()
    {
        var fige = Outil("a.safetensors");
        Champ(fige).From = "";

        var appels = 0;

        var rendus = OptionsVivantes.Resoudre([fige], () => { appels++; return null; }, null);

        Assert.Equal(0, appels);
        Assert.Single(rendus);
    }

    /// <summary>Générateur muet : la volée revient entière, avec ses valeurs de manifeste.</summary>
    /// <remarks>
    /// Le repli doit rendre <em>tous</em> les manifestes, pas seulement ceux qui portaient un choix
    /// dynamique : en perdre un ici retirerait un outil des capacités déclarées à l'assistant, qui
    /// répondrait « je ne sais pas faire » sur un outil pourtant installé.
    /// </remarks>
    [Fact]
    public void WhenTheMachineIsSilentTheWholeFlightComesBack()
    {
        var rendus = OptionsVivantes.Resoudre(
            [Outil("a.safetensors"), Outil("b.safetensors")], () => null, null);

        Assert.Equal(
            ["a.safetensors", "b.safetensors"],
            rendus.Select(m => Champ(m).Value));
    }
}
