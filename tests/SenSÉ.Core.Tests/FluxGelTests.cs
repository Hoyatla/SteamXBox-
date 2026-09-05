using SenSÉ.Plugins;
using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// La règle qui empêche un outil de rouiller quand les modèles changent.
/// </summary>
/// <remarks>
/// Elle a deux usages et une seule écriture : une épreuve qui garde les outils livrés, et un
/// avertissement au chargement pour ceux qu'un client installe lui-même. Deux implémentations
/// auraient fini par ne plus dire la même chose, et c'est celle qui ment qu'on aurait crue.
///
/// <para>
/// Ce qui est éprouvé ici est la règle nue, sur des fichiers écrits pour l'occasion — pas sur le
/// dépôt. Une épreuve qui ne casse que le jour où quelqu'un modifie un manifeste livré ne dit rien
/// de ce que la règle fera devant l'outil d'un inconnu.
/// </para>
/// </remarks>
public class FluxGelTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("gel");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Un flux qui nomme un modèle et une image.</summary>
    private string Flux()
    {
        var ou = Path.Combine(_bac.FullName, "essai.json");

        File.WriteAllText(ou, """
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "wan2.1_i2v_480p_14B.safetensors" } },
              "2": { "class_type": "LoadImage", "inputs": { "image": "exemple.png" } },
              "3": { "class_type": "SaveImage",
                     "inputs": { "images": ["1", 0], "filename_prefix": "sortie" } }
            }
            """);

        return ou;
    }

    private PluginManifest Outil(string cible, params PluginContentItem[] champs)
    {
        var manifeste = new PluginManifest { Id = "tiers", Name = "Outil d'un tiers" };

        manifeste.Content.AddRange(champs);
        manifeste.Content.Add(new PluginContentItem
        {
            Kind = "action",
            Label = "Lancer",
            Does = PluginActions.Flux,
            Target = Flux() + cible,
        });

        return manifeste;
    }

    private static IReadOnlyList<string> Examiner(PluginManifest outil)
        => FluxGel.Examiner(outil, c => c).Select(g => g.ToString()).ToList();

    /// <summary>Un manifeste qui recouvre tout ne dit rien.</summary>
    /// <remarks>
    /// Les deux façons correctes de recouvrir : un champ qui lit ses valeurs sur la machine, et un
    /// fichier que l'utilisateur désigne lui-même. Ni l'un ni l'autre ne peut rouiller.
    /// </remarks>
    [Fact]
    public void AManifestThatCoversEverythingIsSilent()
        => Assert.Empty(Examiner(Outil(
            "|1.ckpt_name={modele}|2.image={image}",
            new PluginContentItem
            {
                Kind = "choice", Id = "modele", From = "CheckpointLoaderSimple.ckpt_name",
            },
            new PluginContentItem { Kind = "file", Id = "image" })));

    /// <summary>Une entrée que la cible ne touche pas est signalée, avec ce qu'il faut écrire.</summary>
    [Fact]
    public void AnInputTheTargetNeverTouchesIsReported()
    {
        var dits = Examiner(Outil(
            "|2.image={image}", new PluginContentItem { Kind = "file", Id = "image" }));

        var dit = Assert.Single(dits);

        Assert.Contains("wan2.1_i2v_480p_14B.safetensors", dit, StringComparison.Ordinal);
        Assert.Contains("1.ckpt_name={un_champ}", dit, StringComparison.Ordinal);
    }

    /// <summary>Recouvrir avec un choix écrit à la main ne fait que déplacer le gel.</summary>
    /// <remarks>
    /// C'est la moitié de la règle qu'on oublierait volontiers : l'entrée est bien substituée, donc
    /// tout a l'air en ordre — mais les valeurs viennent du manifeste, qui vieillira exactement
    /// comme le flux.
    /// </remarks>
    [Fact]
    public void AHandWrittenChoiceOnlyMovesTheFreeze()
    {
        var dits = Examiner(Outil(
            "|1.ckpt_name={modele}|2.image={image}",
            new PluginContentItem
            {
                Kind = "choice", Id = "modele", Options = ["wan2.1_i2v_480p_14B.safetensors"],
            },
            new PluginContentItem { Kind = "file", Id = "image" }));

        Assert.Contains("fige ses valeurs", Assert.Single(dits), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueWrittenStraightIntoTheTargetIsReported()
        => Assert.Contains(
            "en dur",
            Assert.Single(Examiner(Outil(
                "|1.ckpt_name=autre.safetensors|2.image={image}",
                new PluginContentItem { Kind = "file", Id = "image" }))),
            StringComparison.Ordinal);

    [Fact]
    public void ATargetThatNamesAFieldWhichDoesNotExistIsReported()
        => Assert.Contains(
            "n'est pas un champ",
            Assert.Single(Examiner(Outil(
                "|1.ckpt_name={inconnu}|2.image={image}",
                new PluginContentItem { Kind = "file", Id = "image" }))),
            StringComparison.Ordinal);

    // Un outil qui ne lance aucun flux n'a rien à figer.
    [Fact]
    public void AToolWithoutAFlowIsNotExamined()
    {
        var manifeste = new PluginManifest { Id = "autre" };

        manifeste.Content.Add(new PluginContentItem
        {
            Kind = "action", Does = PluginActions.Video, Target = "x|y|z|a|b|c",
        });

        Assert.Empty(Examiner(manifeste));
    }

    /// <summary>Un flux absent n'est pas l'affaire de cette règle.</summary>
    /// <remarks>
    /// L'arbitre le dira au lancement, en termes bien plus précis. Un avertissement de plus au
    /// démarrage ne ferait que noyer celui qui compte.
    /// </remarks>
    [Fact]
    public void AMissingFlowIsSomebodyElsesProblem()
    {
        var manifeste = new PluginManifest { Id = "vide" };

        manifeste.Content.Add(new PluginContentItem
        {
            Kind = "action", Does = PluginActions.Flux, Target = @"D:\nulle\part\rien.json",
        });

        Assert.Empty(Examiner(manifeste));
    }

    // La phrase doit porter tout ce qu'il faut pour agir sans rien rouvrir : l'outil, le fichier, le
    // nœud, l'entrée, la valeur, et le geste.
    [Fact]
    public void TheSentenceCarriesEverythingNeededToAct()
    {
        var dit = Assert.Single(Examiner(Outil(
            "|2.image={image}", new PluginContentItem { Kind = "file", Id = "image" })));

        Assert.Contains("tiers", dit, StringComparison.Ordinal);
        Assert.Contains("essai.json", dit, StringComparison.Ordinal);
        Assert.Contains("1.ckpt_name", dit, StringComparison.Ordinal);
    }
}
