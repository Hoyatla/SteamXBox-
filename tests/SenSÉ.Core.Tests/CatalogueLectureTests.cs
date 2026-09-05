using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Comment le modèle découvre ce que le générateur sait faire.
/// </summary>
/// <remarks>
/// Le catalogue installé pèse 1084 classes de nœuds pour 1,4 Mo, soit de l'ordre de quarante fois
/// le contexte entier du modèle. Le montrer en bloc n'est pas coûteux, c'est impossible : ces deux
/// gestes — chercher court, puis lire un seul schéma — sont la condition d'existence de la
/// composition, pas un raffinement.
/// </remarks>
public class CatalogueLectureTests
{
    private static readonly Catalogue Connu = Catalogue.Lire("""
        {
          "KSampler": {
            "input": { "required": {
              "model": ["MODEL", {}],
              "steps": ["INT", {"default": 20, "min": 1, "max": 10000}],
              "sampler_name": [["euler", "ddim", "dpmpp_2m"], {}] },
              "optional": { "denoise": ["FLOAT", {"min": 0.0, "max": 1.0}] } },
            "output": ["LATENT"],
            "output_node": false,
            "category": "model/sampling",
            "description": "Uses the provided model to denoise the latent image.",
            "search_aliases": ["sampler", "txt2img", "generate"]
          },
          "CheckpointLoaderSimple": {
            "input": { "required": {
              "ckpt_name": [["svd_xt.safetensors", "flux.safetensors"], {}] } },
            "output": ["MODEL", "CLIP", "VAE"],
            "output_node": false,
            "category": "loaders",
            "description": "Loads a diffusion model checkpoint."
          },
          "SaveVideo": {
            "input": { "required": {
              "video": ["VIDEO", {}],
              "codec": ["COMFY_DYNAMICCOMBO_V3", {}] } },
            "output": [],
            "output_node": true,
            "category": "image/video",
            "description": "Saves the video to the output folder."
          }
        }
        """);

    private static IReadOnlyList<string> Noms(string besoin)
        => CatalogueLecture.Chercher(Connu, besoin, 12).Select(n => n.Nom).ToList();

    [Fact]
    public void ANodeIsFoundByItsName()
        => Assert.Contains("KSampler", Noms("ksampler"));

    /// <summary>Les alias rattrapent l'écart entre l'intention et le nom de classe.</summary>
    /// <remarks>
    /// <c>KSampler</c> ne contient ni « generate » ni « txt2img », et c'est pourtant ce qu'on
    /// cherche quand on veut fabriquer une image. Sans les alias, la recherche ne relie rien —
    /// c'est le seul pont entre les mots d'une demande et les noms d'une bibliothèque.
    /// </remarks>
    [Fact]
    public void ANodeIsFoundByWhatItIsForRatherThanByItsName()
    {
        Assert.Contains("KSampler", Noms("txt2img"));
        Assert.Contains("KSampler", Noms("generate an image"));
    }

    [Fact]
    public void ANodeIsFoundByItsDescription()
        => Assert.Contains("CheckpointLoaderSimple", Noms("loads a checkpoint"));

    // Le plus proche d'abord : un nom qui correspond vaut plus qu'une description qui effleure.
    [Fact]
    public void TheClosestComesFirst()
        => Assert.Equal("SaveVideo", Noms("save video")[0]);

    // Les mots de deux lettres sont dans la description de presque tout et donneraient la même note
    // à mille nœuds.
    [Fact]
    public void ShortWordsDoNotDragEverythingIn()
    {
        Assert.Empty(Noms("to of a"));
        Assert.Empty(Noms(""));
    }

    /// <summary>Le résumé donne de quoi choisir, et dit ce qu'il faut faire ensuite.</summary>
    /// <remarks>
    /// Sans cette consigne, le modèle écrit un flux directement à partir des noms, en devinant les
    /// entrées — et devine faux, parce que rien dans un nom ne dit comment on l'emploie.
    /// </remarks>
    [Fact]
    public void TheSummaryOffersAChoiceAndSaysWhatToDoNext()
    {
        var resume = CatalogueLecture.Resumer(CatalogueLecture.Chercher(Connu, "save video", 12), "save video");

        Assert.Contains("SaveVideo", resume, StringComparison.Ordinal);
        Assert.Contains("[enregistre]", resume, StringComparison.Ordinal);
        Assert.Contains("Demande le détail", resume, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingNothingSaysSoAndSuggestsEnglish()
        => Assert.Contains("anglais", CatalogueLecture.Resumer([], "sauvegarder la vidéo"),
            StringComparison.Ordinal);

    /// <summary>Le détail porte tout ce qui manque pour écrire le nœud, et rien d'autre.</summary>
    [Fact]
    public void TheDetailCarriesEverythingNeededToWriteTheNode()
    {
        var detail = CatalogueLecture.Detailler(Connu.Noeud("KSampler")!);

        // Un câble doit se distinguer d'une valeur, et sa syntaxe être rappelée : c'est l'erreur
        // naturelle de qui n'a jamais vu un graphe.
        Assert.Contains("model : câble de type MODEL", detail, StringComparison.Ordinal);
        Assert.Contains("rang de sortie", detail, StringComparison.Ordinal);

        Assert.Contains("steps : INT, de 1 à 10000", detail, StringComparison.Ordinal);
        Assert.Contains("sampler_name : un de euler, ddim, dpmpp_2m", detail, StringComparison.Ordinal);
        Assert.Contains("facultatives", detail, StringComparison.Ordinal);
        Assert.Contains("rang 0 = LATENT", detail, StringComparison.Ordinal);
    }

    /// <summary>Les valeurs admises sont la liste réelle des fichiers installés.</summary>
    /// <remarks>
    /// C'est le seul endroit d'où elle peut venir. Un modèle de langage qui écrit un nom de modèle
    /// de mémoire écrit celui d'une autre machine — et l'erreur ne se voit qu'après plusieurs
    /// minutes de chargement.
    /// </remarks>
    [Fact]
    public void TheAllowedValuesAreTheFilesActuallyInstalled()
        => Assert.Contains("svd_xt.safetensors, flux.safetensors",
            CatalogueLecture.Detailler(Connu.Noeud("CheckpointLoaderSimple")!),
            StringComparison.Ordinal);

    [Fact]
    public void ANodeThatSavesSaysSo()
        => Assert.Contains("enregistre le résultat",
            CatalogueLecture.Detailler(Connu.Noeud("SaveVideo")!), StringComparison.Ordinal);

    // Certaines listes comptent des dizaines d'entrées — quarante-quatre échantillonneurs sur cette
    // machine. Les déverser toutes épuiserait le contexte en trois lectures.
    [Fact]
    public void AVeryLongListIsCutAndCounted()
    {
        var beaucoup = string.Join(", ", Enumerable.Range(0, 40).Select(i => $"\"m{i}\""));

        var large = Catalogue.Lire($$"""
            { "Loader": {
                "input": { "required": { "nom": [[{{beaucoup}}], {}] } },
                "output": ["MODEL"], "output_node": false,
                "category": "loaders", "description": "" } }
            """);

        var detail = CatalogueLecture.Detailler(large.Noeud("Loader")!);

        Assert.Contains("(40 en tout)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("m39", detail, StringComparison.Ordinal);
    }
}
