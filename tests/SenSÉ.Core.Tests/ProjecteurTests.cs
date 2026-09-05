using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// L'appariement d'un modèle et de ses yeux.
/// </summary>
/// <remarks>
/// <b>Une panne muette, et c'est ce qui la rend grave.</b> Le modèle et le projecteur étaient choisis
/// séparément — le premier <c>.gguf</c> d'un côté, le premier <c>mmproj-*.gguf</c> de l'autre. Tant
/// qu'un seul modèle vivait dans le dossier, cela tombait juste par accident. Dès qu'il y en a deux,
/// l'ordre alphabétique peut donner le corps de l'un et les yeux de l'autre : le serveur démarre
/// normalement, et ce qu'il croit voir dans une image n'a plus de rapport avec elle.
/// </remarks>
public class ProjecteurTests
{
    private static readonly string[] LesDeuxFamilles =
    [
        "mmproj-Qwen3.5-4B-F16.gguf",
        "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf",
    ];

    /// <summary>Chaque modèle reçoit le projecteur de sa propre famille.</summary>
    /// <remarks>
    /// C'est le cas réel du 23 août : le 8B arrive à côté du 4B déjà installé. En ordre
    /// alphabétique, <c>Qwen3.5</c> précède <c>Qwen3VL</c> — le point vaut moins que la lettre V —
    /// donc le 8B aurait hérité des yeux du 4B.
    /// </remarks>
    [Theory]
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf", "mmproj-Qwen3.5-4B-F16.gguf")]
    [InlineData("Qwen3VL-8B-Instruct-Q4_K_M.gguf", "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf")]
    public void EachModelGetsItsOwnEyes(string modele, string attendu)
        => Assert.Equal(attendu, ServeurModele.Apparier(modele, LesDeuxFamilles));

    /// <summary>La quantification n'entre pas dans l'appariement.</summary>
    /// <remarks>
    /// Elle diffère des deux côtés et pas au même endroit : <c>-Q4_K_M</c> pour le modèle,
    /// <c>-F16</c> ou <c>-Q8_0</c> pour le projecteur. Découper ces suffixes aurait supposé de
    /// connaître la liste des quantifications, qui s'allonge à chaque version de llama.cpp ; le plus
    /// long début commun s'en passe.
    /// </remarks>
    [Theory]
    [InlineData("Qwen3VL-8B-Instruct-Q8_0.gguf")]
    [InlineData("Qwen3VL-8B-Instruct-F16.gguf")]
    [InlineData("Qwen3VL-8B-Instruct-IQ4_XS.gguf")]
    public void TheQuantisationPlaysNoPart(string modele)
        => Assert.Equal(
            "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf",
            ServeurModele.Apparier(modele, LesDeuxFamilles));

    /// <summary>Aucun projecteur du tout : le modèle reste aveugle, sans erreur.</summary>
    [Fact]
    public void WithNoProjectorTheModelSimplyDoesNotSee()
        => Assert.Null(ServeurModele.Apparier("Qwen3VL-8B-Instruct-Q4_K_M.gguf", []));

    /// <summary>Un projecteur étranger n'est pas ramassé faute de mieux.</summary>
    /// <remarks>
    /// Sans plancher, un projecteur sans le moindre rapport gagnerait par défaut dès qu'il est seul
    /// de son espèce — et un modèle chargé avec les yeux d'un autre décrit des images qu'il ne voit
    /// pas. Pas de vue vaut mieux que la mauvaise.
    /// </remarks>
    [Fact]
    public void AnUnrelatedProjectorIsNotPickedUpForWantOfBetter()
        => Assert.Null(ServeurModele.Apparier(
            "Gemma3-4B-it-Q4_K_M.gguf", ["mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf"]));

    /// <summary>Un projecteur sans préfixe reconnaissable est traité tel quel.</summary>
    /// <remarks>
    /// Tous les éditeurs ne préfixent pas de la même façon — certains écrivent <c>mmproj_</c>,
    /// d'autres <c>mmproj.</c>, d'autres rien du tout. Retirer un préfixe absent ne doit pas amputer
    /// le nom, sinon l'appariement échoue sur un fichier parfaitement valide.
    /// </remarks>
    [Theory]
    [InlineData("mmproj_Qwen3VL-8B-Instruct-Q8_0.gguf")]
    [InlineData("mmproj.Qwen3VL-8B-Instruct-Q8_0.gguf")]
    public void OtherPrefixSpellingsStillPair(string projecteur)
        => Assert.Equal(
            projecteur,
            ServeurModele.Apparier("Qwen3VL-8B-Instruct-Q4_K_M.gguf", [projecteur]));

    /// <summary>Deux tailles d'une même famille ne se confondent pas.</summary>
    /// <remarks>
    /// C'est le cas le plus serré, et c'est le contenu réel du dossier : <c>Qwen3.5-4B</c> et
    /// <c>Qwen3.5-9B</c> partagent huit caractères. Seul le onzième les sépare — un chiffre. Une
    /// règle qui se contenterait d'un seuil, au lieu de retenir le <em>plus long</em> début commun,
    /// prendrait le premier venu et donnerait au 9B les yeux du 4B.
    /// </remarks>
    [Theory]
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf", "mmproj-Qwen3.5-4B-F16.gguf")]
    [InlineData("Qwen3.5-9B-Q4_K_M.gguf", "mmproj-Qwen3.5-9B-F16.gguf")]
    public void TwoSizesOfOneFamilyAreNotConfused(string modele, string attendu)
        => Assert.Equal(
            attendu,
            ServeurModele.Apparier(
                modele,
                ["mmproj-Qwen3.5-4B-F16.gguf",
                 "mmproj-Qwen3.5-9B-F16.gguf",
                 "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf"]));

    /// <summary>Un chemin complet s'apparie comme un simple nom de fichier.</summary>
    /// <remarks>
    /// C'est ce que le produit passe réellement : <c>Directory.GetFiles</c> rend des chemins
    /// absolus. Comparer les chemins entiers ferait gagner le début commun du dossier, identique
    /// pour tous, et l'appariement deviendrait arbitraire.
    /// </remarks>
    [Fact]
    public void FullPathsPairLikeBareNames()
    {
        const string dossier = @"D:\SenSÉ\Outils\Modeles\";

        Assert.Equal(
            dossier + "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf",
            ServeurModele.Apparier(
                dossier + "Qwen3VL-8B-Instruct-Q4_K_M.gguf",
                [dossier + "mmproj-Qwen3.5-4B-F16.gguf",
                 dossier + "mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf"]));
    }
}
