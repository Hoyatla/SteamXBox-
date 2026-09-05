using System.Text.Json.Nodes;
using SenSÉ.Plugins;
using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que le modèle voit d'un outil, et donc ce qu'il peut en faire.
/// </summary>
/// <remarks>
/// <b>La déclaration est l'interface.</b> Le modèle ne lit pas le manifeste, ne voit pas le
/// panneau, n'essaie pas pour voir : il lit cette déclaration et décide. Une erreur ici ne casse
/// rien — elle rend simplement un outil inatteignable, en silence, et le symptôme est un assistant
/// qui répond poliment à côté.
///
/// <para>
/// C'est exactement ce qui est arrivé. Chaque champ était déclaré obligatoire, y compris ceux qui
/// portaient une valeur par défaut ; « anime cette image » demandait alors sept valeurs d'un coup,
/// dont un chemin de fichier impossible à deviner. Le modèle se rabattait sur la seule action qui
/// n'exigeait rien — ouvrir l'outil — et l'utilisateur en concluait, à juste titre, que l'assistant
/// ne savait rien créer.
/// </para>
/// </remarks>
public class AssistantDeclarationTests
{
    private static PluginManifest Outil() => new()
    {
        Id = "animer",
        Name = "Animer une image",
        Category = "tool",
        Version = "1.0.0",
        Licence = "Proprietary",
        Glyph = "E786",
        Surface = "panel",
        Hint = "Fabrique une courte vidéo à partir d'une image fixe.",
        Content =
        [
            new PluginContentItem { Kind = "file", Id = "image", Label = "Image", Options = ["png"] },
            new PluginContentItem
            {
                Kind = "choice", Id = "images", Label = "Longueur", Value = "14", Options = ["14", "25"],
            },
            new PluginContentItem
            {
                Kind = "number", Id = "etapes", Label = "Étapes", Value = "20", Min = 10, Max = 40,
            },
            new PluginContentItem { Kind = "action", Label = "Animer", Does = "flux", Target = "x" },
        ],
    };

    private static JsonObject Fonction(string nom)
    {
        var declares = AssistantLocal.Declarer([Outil()], []);

        var trouve = declares
            .OfType<JsonObject>()
            .Select(d => d["function"] as JsonObject)
            .FirstOrDefault(f => f?["name"]?.GetValue<string>() == nom);

        Assert.True(trouve is not null, $"aucune fonction déclarée sous le nom « {nom} »");

        return trouve!;
    }

    private static IReadOnlyList<string> Obligatoires(JsonObject fonction)
        => (fonction["parameters"]?["required"] as JsonArray ?? [])
            .Select(n => n?.GetValue<string>() ?? "")
            .ToList();

    /// <summary>Un champ qui a une valeur par défaut n'est pas réclamé au modèle.</summary>
    /// <remarks>
    /// L'exécution retombe déjà sur la valeur du manifeste pour tout champ omis. Les déclarer
    /// obligatoires n'ajoutait donc aucune sûreté : cela ne faisait qu'élever la barre à franchir
    /// avant le premier appel.
    /// </remarks>
    [Fact]
    public void AFieldWithADefaultIsNotDemanded()
    {
        var obligatoires = Obligatoires(Fonction("animer"));

        Assert.DoesNotContain("images", obligatoires);
        Assert.DoesNotContain("etapes", obligatoires);
    }

    // Le fichier, lui, reste obligatoire : c'est la seule chose que le modèle doit réellement
    // obtenir de l'utilisateur avant de pouvoir travailler.
    [Fact]
    public void TheFileToWorkOnIsStillDemanded()
        => Assert.Equal(["image"], Obligatoires(Fonction("animer")));

    /// <summary>Le modèle est prévenu qu'un chemin se demande et ne s'invente pas.</summary>
    /// <remarks>
    /// Sans cette phrase il en fabrique un de forme plausible. Le fichier n'existe pas, l'outil
    /// échoue, et l'échec nomme un fichier que l'utilisateur n'a jamais mentionné — impossible à
    /// relier à sa demande.
    /// </remarks>
    [Fact]
    public void ThePathIsToBeAskedForRatherThanInvented()
    {
        var decrit = Fonction("animer")["parameters"]?["properties"]?["image"]?["description"]
            ?.GetValue<string>() ?? "";

        Assert.Contains("invente", decrit, StringComparison.OrdinalIgnoreCase);
    }

    // Les valeurs admises d'un choix voyagent avec la déclaration : c'est ce qui a permis au modèle
    // de trouver seul le nom d'un réglage, sans qu'aucun code ne le lui apprenne.
    [Fact]
    public void TheAllowedValuesTravelWithTheDeclaration()
    {
        var valeurs = Fonction("animer")["parameters"]?["properties"]?["images"]?["enum"] as JsonArray;

        Assert.NotNull(valeurs);
        Assert.Equal(["14", "25"], valeurs!.Select(v => v?.GetValue<string>()));
    }

    // Le bouton d'un panneau n'est pas un réglage : le déclarer donnerait au modèle un paramètre
    // « Animer » à remplir, qui ne veut rien dire.
    [Fact]
    public void TheActionButtonIsNotASetting()
    {
        var champs = Fonction("animer")["parameters"]?["properties"] as JsonObject;

        Assert.NotNull(champs);
        Assert.Equal(["image", "images", "etapes"], champs!.Select(p => p.Key));
    }
}
