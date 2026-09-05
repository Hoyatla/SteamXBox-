using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Les deux écritures d'un choix dans le catalogue de ComfyUI.
/// </summary>
/// <remarks>
/// <b>La panne que le mécanisme d'options dynamiques était censé empêcher.</b> Il existe pour qu'un
/// outil ne se fige pas le jour où les modèles changent ; il s'est figé le jour où la plateforme a
/// changé. ComfyUI migre ses nœuds d'une écriture à l'autre — l'ancienne met les valeurs en première
/// position, à la place du nom de type, la nouvelle les range dans « options ». Nous ne lisions que
/// l'ancienne.
///
/// <para>
/// Le 23 août, le nœud qui charge un dossier d'images était déjà migré. Le panneau n'offrait aucun
/// dossier, « regler_option » répondait « Valeurs possibles : . », et le journal accusait la machine
/// — « ne rend rien sur cette machine » — d'un défaut de lecture qui était le nôtre. La faute serait
/// revenue nœud par nœud, au rythme des migrations, sans jamais ressembler à autre chose qu'une
/// installation incomplète.
/// </para>
/// </remarks>
public class CatalogueFormesTests
{
    /// <summary>L'ancienne écriture : les valeurs à la place du type.</summary>
    [Fact]
    public void TheOldSpellingIsStillRead()
    {
        var lu = Catalogue.Lire("""
        { "CheckpointLoaderSimple": { "input": { "required": {
            "ckpt_name": [ ["a.safetensors", "b.safetensors"], {} ] } } } }
        """);

        Assert.Equal(
            ["a.safetensors", "b.safetensors"],
            lu.Options("CheckpointLoaderSimple.ckpt_name"));
    }

    /// <summary>La nouvelle écriture : « COMBO » puis les valeurs dans « options ».</summary>
    /// <remarks>
    /// C'est la forme exacte rendue par cette installation pour le chargeur de dossier, relevée sur
    /// la machine : <c>["COMBO", {"multiselect": false, "options": ["3d", "test"]}]</c>.
    /// </remarks>
    [Fact]
    public void TheNewSpellingIsReadToo()
    {
        var lu = Catalogue.Lire("""
        { "LoadImageDataSetFromFolder": { "input": { "required": {
            "folder": [ "COMBO", { "tooltip": "The folder to load images from.",
                                   "multiselect": false, "options": ["3d", "test"] } ] } } } }
        """);

        Assert.Equal(["3d", "test"], lu.Options("LoadImageDataSetFromFolder.folder"));
    }

    /// <summary>Un vrai type nommé ne devient pas un choix parce qu'il porte des contraintes.</summary>
    /// <remarks>
    /// Les bornes d'un nombre vivent au même endroit que les valeurs d'un choix. Confondre les deux
    /// ferait proposer « min » et « max » comme s'il s'agissait de valeurs admises.
    /// </remarks>
    [Fact]
    public void ANamedTypeWithConstraintsIsNotAChoice()
    {
        var lu = Catalogue.Lire("""
        { "CreateVideo": { "input": { "required": {
            "fps": [ "FLOAT", { "default": 30.0, "min": 1.0, "max": 120.0 } ] } } } }
        """);

        Assert.Empty(lu.Options("CreateVideo.fps"));
    }
}
