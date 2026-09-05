using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que la machine porte comme modèles, tel que l'assistant le voit.
/// </summary>
/// <remarks>
/// <b>Le défaut mesuré le 24 août.</b> L'assistant cherche « load checkpoint », trouve
/// <c>CheckpointLoader</c>, lit sa liste et conclut que la machine n'a qu'un modèle. Elle en avait
/// cinq : SVD dans <c>checkpoints</c>, MiniMax H3 dans <c>diffusion_models</c>, Flux dans
/// <c>unet</c>. Trois dossiers, trois nœuds de chargement, et rien qui les relie pour qui ne le
/// sait pas d'avance. Il a tourné une demi-heure sur le seul modèle qu'il pouvait voir pendant que
/// l'utilisateur lui en proposait deux autres.
///
/// <para>
/// Le catalogue ci-dessous reproduit cette machine : les mêmes trois chargeurs, les mêmes noms de
/// fichiers, et le <c>config_name</c> en <c>.yaml</c> de <c>CheckpointLoader</c> qui ne doit
/// surtout pas passer pour un modèle.
/// </para>
/// </remarks>
public class ModelesInstallesTests
{
    private static readonly Catalogue Machine = Catalogue.Lire("""
        {
          "CheckpointLoader": {
            "input": { "required": {
              "config_name": [["v1-inference.yaml", "v2-inference.yaml"], {}],
              "ckpt_name": [["svd_xt.safetensors"], {}] } },
            "output": ["MODEL", "CLIP", "VAE"],
            "output_node": false,
            "category": "model/loaders",
            "description": ""
          },
          "CheckpointLoaderSimple": {
            "input": { "required": { "ckpt_name": [["svd_xt.safetensors"], {}] } },
            "output": ["MODEL", "CLIP", "VAE"],
            "output_node": false,
            "category": "model/loaders",
            "description": "Loads a diffusion model checkpoint."
          },
          "UNETLoader": {
            "input": { "required": {
              "unet_name": [["minimax_h3_fl2va_pruned_int8_convrot.safetensors"], {}] } },
            "output": ["MODEL"],
            "output_node": false,
            "category": "model/loaders",
            "description": "Loads a diffusion model."
          },
          "UnetLoaderGGUF": {
            "input": { "required": { "unet_name": [["flux1-schnell-Q5_K_S.gguf"], {}] } },
            "output": ["MODEL"],
            "output_node": false,
            "category": "bootleg",
            "description": "Loads a GGUF diffusion model."
          },
          "VAELoader": {
            "input": { "required": {
              "vae_name": [["ae.safetensors", "minimax_h3_video_vae_fp16.safetensors", "pixel_space"], {}] } },
            "output": ["VAE"],
            "output_node": false,
            "category": "model/loaders",
            "description": ""
          },
          "KSampler": {
            "input": { "required": {
              "sampler_name": [["euler", "ddim"], {}],
              "steps": ["INT", {"min": 1, "max": 100}] } },
            "output": ["LATENT"],
            "output_node": false,
            "category": "model/sampling",
            "description": "Denoises."
          }
        }
        """);

    private static readonly string Vu = CatalogueLecture.Modeles(Machine);

    /// <summary>Les trois dossiers sont vus, pas seulement celui des checkpoints.</summary>
    [Fact]
    public void EveryInstalledModelIsSeen()
    {
        Assert.Contains("svd_xt.safetensors", Vu, StringComparison.Ordinal);
        Assert.Contains("minimax_h3_fl2va_pruned_int8_convrot.safetensors", Vu, StringComparison.Ordinal);
        Assert.Contains("flux1-schnell-Q5_K_S.gguf", Vu, StringComparison.Ordinal);
        Assert.Contains("minimax_h3_video_vae_fp16.safetensors", Vu, StringComparison.Ordinal);
    }

    /// <summary>Chaque modèle est nommé avec le nœud qui sait le charger.</summary>
    /// <remarks>
    /// C'est la moitié utile : savoir que MiniMax existe ne sert à rien sans savoir qu'il se charge
    /// par <c>UNETLoader</c> et non par un chargeur de checkpoint.
    /// </remarks>
    [Fact]
    public void EachModelComesWithItsLoader()
    {
        Assert.Contains("UNETLoader.unet_name", Vu, StringComparison.Ordinal);
        Assert.Contains("UnetLoaderGGUF.unet_name", Vu, StringComparison.Ordinal);
        Assert.Contains("VAELoader.vae_name", Vu, StringComparison.Ordinal);
    }

    // Un .yaml de configuration n'est pas un poids de modèle. Le laisser passer ferait proposer
    // « v1-inference.yaml » comme modèle à charger, et l'erreur ne se verrait qu'à l'exécution.
    [Fact]
    public void ConfigurationFilesAreNotModels()
    {
        Assert.DoesNotContain("v1-inference.yaml", Vu, StringComparison.Ordinal);
        Assert.DoesNotContain("v2-inference.yaml", Vu, StringComparison.Ordinal);
    }

    // Ni un nom d'échantillonneur, qui n'est un fichier de rien.
    [Fact]
    public void PlainWordsAreNotModels()
    {
        Assert.DoesNotContain("euler", Vu, StringComparison.Ordinal);
        Assert.DoesNotContain("KSampler", Vu, StringComparison.Ordinal);
    }

    /// <summary>Deux nœuds qui offrent la même liste ne la font pas écrire deux fois.</summary>
    /// <remarks>
    /// <c>CheckpointLoader</c> et <c>CheckpointLoaderSimple</c> voient le même dossier. Sur la
    /// machine réelle, une dizaine de nœuds voient le même dossier de VAE : les énumérer tous
    /// répéterait les fichiers sans rien apprendre, et mangerait le contexte qu'on essaie
    /// d'économiser.
    /// </remarks>
    [Fact]
    public void LoadersSharingAFolderShareOneEntry()
    {
        var fois = Vu.Split("svd_xt.safetensors").Length - 1;

        Assert.Equal(1, fois);
        Assert.Contains("CheckpointLoader", Vu, StringComparison.Ordinal);
        Assert.Contains("aussi :", Vu, StringComparison.Ordinal);
    }

    // La liste dit ce qu'un nœud rend : c'est ce qui permet de câbler sans redemander le détail.
    [Fact]
    public void WhatALoaderProducesIsStated()
        => Assert.Contains("→ MODEL, CLIP, VAE", Vu, StringComparison.Ordinal);

    [Fact]
    public void AMachineWithoutModelsSaysSoRatherThanShowingNothing()
    {
        var vide = Catalogue.Lire("""
            { "KSampler": { "input": { "required": { "steps": ["INT", {}] } },
              "output": ["LATENT"], "output_node": false, "category": "x", "description": "" } }
            """);

        Assert.Contains("Aucun modèle installé", CatalogueLecture.Modeles(vide), StringComparison.Ordinal);
    }
}
