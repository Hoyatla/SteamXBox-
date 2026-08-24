using SteamXBox.Tools.Modeles;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// La déclaration de jeux réellement livrée, lue comme le produit la lira.
/// </summary>
/// <remarks>
/// Elle est du texte, et rien dans la compilation ne la regarde : une virgule de trop et l'écran
/// s'ouvre vide, sans que personne l'apprenne avant un client. Les épreuves qui suivent portent sur
/// le fichier du dépôt, pas sur un exemple écrit pour l'occasion.
/// </remarks>
public class JeuxLivresTests
{
    private static IReadOnlyList<JeuModeles> Livres()
    {
        var ou = new DirectoryInfo(AppContext.BaseDirectory);

        while (ou is not null && !Directory.Exists(Path.Combine(ou.FullName, "Plugins")))
        {
            ou = ou.Parent;
        }

        Assert.True(ou is not null, "racine du dépôt introuvable");

        var fichier = Path.Combine(ou!.FullName, "Modeles", "jeux-modeles.json");

        Assert.True(File.Exists(fichier), $"déclaration absente : {fichier}");

        return JeuxModeles.Lire(File.ReadAllText(fichier));
    }

    [Fact]
    public void TheShippedDeclarationIsReadable()
    {
        var jeux = Livres();

        Assert.NotEmpty(jeux);
        Assert.All(jeux, j => Assert.NotEmpty(j.Fichiers));
        Assert.All(jeux, j => Assert.NotEqual("", j.Nom));
    }

    /// <summary>Chaque jeu dit sous quelle licence il vient.</summary>
    /// <remarks>
    /// Un modèle sous licence non commerciale est utilisable pour essayer et interdit dans un
    /// produit vendu, et rien ne l'en distingue à l'usage. La licence n'est donc pas une mention
    /// facultative : c'est la seule chose qui permette de trancher avant d'y bâtir un outil.
    /// </remarks>
    [Fact]
    public void EverySetSaysUnderWhichLicenceItComes()
        => Assert.All(Livres(), j => Assert.NotEqual("", j.Licence));

    /// <summary>Chaque fichier vient d'un dépôt en clair et atterrit sous les modèles.</summary>
    /// <remarks>
    /// La déclaration décide de ce qui est téléchargé et d'où cela s'écrit. Une adresse en
    /// <c>http</c> laisserait un intermédiaire substituer le fichier, et un chemin remontant
    /// écrirait n'importe où sur la machine sous couvert d'installer un modèle.
    /// </remarks>
    [Fact]
    public void EveryFileComesOverHttpsAndStaysUnderTheModelFolder()
        => Assert.All(Livres().SelectMany(j => j.Fichiers), f =>
        {
            Assert.StartsWith("https://", f.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("..", f.Vers, StringComparison.Ordinal);
            Assert.True(f.Octets > 0, $"{f.Vers} n'a pas de taille");
        });

    /// <summary>Aucun jeu ne passe par un dépôt à accès restreint.</summary>
    /// <remarks>
    /// Constaté en installant : le dépôt d'origine de Flux est fermé, et <c>curl</c> y a reçu une
    /// page d'erreur de 140 octets à la place d'un VAE de 335 Mo. Le téléchargement rendait zéro,
    /// le fichier existait, et rien n'aurait dit pourquoi le modèle refusait de se charger. Les
    /// dépôts fermés se reconnaissent mal d'avance ; celui-là est nommé parce qu'il a mordu.
    /// </remarks>
    [Fact]
    public void NoFileComesFromTheGatedFluxRepository()
        => Assert.All(Livres().SelectMany(j => j.Fichiers), f =>
            Assert.DoesNotContain(
                "black-forest-labs/FLUX.1-schnell/resolve", f.Url, StringComparison.OrdinalIgnoreCase));

    /// <summary>Les identifiants sont uniques.</summary>
    /// <remarks>
    /// Deux jeux du même identifiant se retrouveraient à écrire dans les mêmes fichiers, et
    /// « installé » désignerait celui des deux que la lecture a rendu en premier.
    /// </remarks>
    [Fact]
    public void TheIdentifiersAreUnique()
    {
        var jeux = Livres();

        Assert.Equal(jeux.Count, jeux.Select(j => j.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Il existe au moins un jeu pour une carte modeste.</summary>
    /// <remarks>
    /// Le produit est destiné à des machines qu'on ne choisit pas. Un catalogue dont le plus léger
    /// réclame déjà douze gigaoctets ne proposerait rien à la moitié des clients — et c'est
    /// exactement la façon dont on se retrouve à ne tourner que sur la machine de son auteur.
    /// </remarks>
    [Fact]
    public void SomethingIsOfferedToAModestCard()
    {
        // Huit gigaoctets de mémoire vidéo, dont une part au bureau : une carte courante.
        Assert.NotNull(JeuxModeles.Recommander(Livres(), 6500, "texte-image"));
    }

    /// <summary>Chaque jeu dit à quoi il sert, sous une forme comparable.</summary>
    /// <remarks>
    /// Sans rôle, la recommandation compare des jeux qui ne font pas la même chose : elle
    /// conseillait un modèle image-vidéo à qui voulait dessiner à partir d'une phrase, au seul
    /// motif qu'il était le plus lourd. Un jeu sans rôle retomberait dans ce sac commun.
    /// </remarks>
    [Fact]
    public void EverySetDeclaresItsRole()
        => Assert.All(Livres(), j => Assert.NotEqual("", j.Role));

    /// <summary>Chaque palier de machine a quelque chose à proposer.</summary>
    /// <remarks>
    /// Les titres montrés à l'utilisateur — poste familial, PC de jeu, carte exigeante — promettent
    /// un choix pour chacun. Un palier vide afficherait un titre sous lequel il n'y a rien, ce qui
    /// se lit comme un défaut du produit plutôt que comme une absence de modèle.
    /// </remarks>
    [Fact]
    public void EveryTierHasSomethingToOffer()
    {
        var paliers = Livres().Select(j => j.Palier).Distinct().ToList();

        Assert.Contains(Palier.Bureau, paliers);
        Assert.Contains(Palier.Jeu, paliers);
        Assert.Contains(Palier.Expert, paliers);
    }

    /// <summary>Le produit sait faire deux choses, et un modèle est proposé pour chacune.</summary>
    [Fact]
    public void EachThingTheProductCanDoHasASet()
    {
        var roles = Livres().Select(j => j.Role).Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("texte-image", roles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("image-video", roles, StringComparer.OrdinalIgnoreCase);
    }
}
