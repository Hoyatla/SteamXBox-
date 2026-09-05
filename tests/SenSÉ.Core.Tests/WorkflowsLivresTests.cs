using System.Text.Json;
using System.Text.Json.Nodes;
using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Les flux livrés, mis dans la forme que le générateur sait ouvrir.
/// </summary>
/// <remarks>
/// Ce qui est éprouvé ici tient en une phrase : un graphe posé dans le dossier des flux doit
/// s'ouvrir <b>branché</b>. Le piège est qu'un graphe débranché s'ouvre quand même — sans erreur,
/// sans message — et que seul un examen des liens le distingue d'un graphe juste.
/// </remarks>
public class WorkflowsLivresTests
{
    /// <summary>Un graphe minuscule : un chargeur, un échantillonneur, une sauvegarde.</summary>
    private const string Api = """
        {
          "1": { "class_type": "CheckpointLoaderSimple",
                 "inputs": { "ckpt_name": "modele.safetensors" } },
          "2": { "class_type": "KSampler",
                 "inputs": { "model": ["1", 0], "seed": 42, "steps": 20 } },
          "3": { "class_type": "SaveImage",
                 "inputs": { "images": ["2", 0], "filename_prefix": "essai" } }
        }
        """;

    private static JsonObject Convertir(string api = Api)
        => JsonNode.Parse(WorkflowsLivres.Convertir(api))!.AsObject();

    private static JsonObject Noeud(JsonObject graphe, int id)
        => graphe["nodes"]!.AsArray()
            .First(n => n!["id"]!.GetValue<int>() == id)!.AsObject();

    /// <summary>La forme convertie est celle que l'interface lit, pas celle qu'exécute le moteur.</summary>
    /// <remarks>
    /// C'est tout l'objet de la classe : le panneau des flux appelle <c>loadGraphData</c> sans
    /// détecter le format, donc un fichier resté en forme API s'ouvrirait vide.
    /// </remarks>
    [Fact]
    public void TheConvertedGraphIsInTheFormTheInterfaceReads()
    {
        var graphe = Convertir();

        Assert.True(graphe.ContainsKey("nodes"));
        Assert.True(graphe.ContainsKey("links"));
        Assert.Equal(3, graphe["nodes"]!.AsArray().Count);
    }

    /// <summary>Chaque câble du graphe exécuté devient un lien du graphe affiché.</summary>
    [Fact]
    public void EveryWireBecomesALink()
        => Assert.Equal(2, Convertir()["links"]!.AsArray().Count);

    /// <summary>
    /// Un nœud lu avant ses consommateurs garde quand même ses sorties branchées.
    /// </summary>
    /// <remarks>
    /// Le chargeur est écrit en premier dans le fichier, et les câbles qui en partent sont demandés
    /// par des nœuds écrits après lui. Construire les sorties au fil de la lecture laissait donc
    /// débranché tout ce qui charge — c'est-à-dire l'entrée de chaque graphe livré.
    /// </remarks>
    [Fact]
    public void ANodeReadBeforeItsConsumersStillHasItsOutputsWired()
    {
        var sorties = Noeud(Convertir(), 1)["outputs"]!.AsArray();

        Assert.NotEmpty(sorties);
        Assert.NotEmpty(sorties[0]!["links"]!.AsArray());
    }

    /// <summary>Une valeur écrite n'est pas prise pour un câble, ni l'inverse.</summary>
    /// <remarks>
    /// La règle est celle de ComfyUI : un tableau <c>[nœud, rang]</c> est un câble, le reste est une
    /// valeur. Elle tient sans catalogue, donc générateur éteint.
    /// </remarks>
    [Fact]
    public void WrittenValuesAreNotMistakenForWires()
    {
        var sampler = Noeud(Convertir(), 2);

        Assert.Single(sampler["inputs"]!.AsArray());
        Assert.Equal("model", sampler["inputs"]![0]!["name"]!.GetValue<string>());
        Assert.Contains(42, sampler["widgets_values"]!.AsArray().Select(v => v?.GetValue<int>() ?? 0));
    }

    /// <summary>
    /// La case « après génération » que l'interface ajoute d'elle-même est comptée.
    /// </summary>
    /// <remarks>
    /// Elle n'existe dans aucune déclaration de nœud : l'interface l'insère juste après la graine.
    /// L'oublier ne casse rien visiblement — cela décale d'un cran tous les réglages suivants, et le
    /// nombre d'étapes prend la place du mode de graine.
    /// </remarks>
    [Fact]
    public void TheSeedControlSlotTheInterfaceAddsIsAccountedFor()
    {
        var valeurs = Noeud(Convertir(), 2)["widgets_values"]!.AsArray();

        Assert.Equal(3, valeurs.Count);
        Assert.Equal("fixed", valeurs[1]!.GetValue<string>());
    }

    /// <summary>Le graphe s'ouvre lisible : ce qui charge à gauche, ce qui enregistre à droite.</summary>
    [Fact]
    public void TheGraphOpensLaidOutRatherThanInAHeap()
    {
        var graphe = Convertir();
        var gauche = Noeud(graphe, 1)["pos"]![0]!.GetValue<int>();
        var droite = Noeud(graphe, 3)["pos"]![0]!.GetValue<int>();

        Assert.True(droite > gauche, "la sauvegarde doit être posée après le chargement");
    }

    /// <summary>Un graphe bouclé ne fait pas descendre la conversion sans fin.</summary>
    [Fact]
    public void ALoopedGraphDoesNotRunAway()
    {
        const string boucle = """
            { "1": { "class_type": "A", "inputs": { "x": ["2", 0] } },
              "2": { "class_type": "B", "inputs": { "y": ["1", 0] } } }
            """;

        Assert.Equal(2, Convertir(boucle)["nodes"]!.AsArray().Count);
    }

    [Fact]
    public void AGraphWithoutClassTypeIsRefusedRatherThanWrittenWrong()
        => Assert.Throws<JsonException>(
            () => WorkflowsLivres.Convertir("""{ "1": { "inputs": {} } }"""));

    /// <summary>Tous les flux livrés se convertissent, et pas seulement celui qu'on a en tête.</summary>
    /// <remarks>
    /// L'épreuve lit le dossier plutôt qu'une liste : un flux ajouté demain est couvert sans que
    /// personne ait à penser à l'inscrire ici.
    /// </remarks>
    [Fact]
    public void EveryShippedGraphConverts()
    {
        var dossier = Flux();

        if (dossier is null)
        {
            return;
        }

        var vus = 0;

        foreach (var fichier in Directory.EnumerateFiles(dossier, "*.json"))
        {
            var graphe = JsonNode.Parse(WorkflowsLivres.Convertir(File.ReadAllText(fichier)))!;

            Assert.True(
                graphe["nodes"]!.AsArray().Count > 0,
                $"{Path.GetFileName(fichier)} s'ouvrirait vide");

            vus++;
        }

        Assert.True(vus > 0, "aucun flux livré n'a été lu");
    }

    /// <summary>Le dossier des flux livrés, en remontant depuis les binaires de l'épreuve.</summary>
    private static string? Flux()
    {
        var ici = new DirectoryInfo(AppContext.BaseDirectory);

        while (ici is not null)
        {
            var flux = Path.Combine(ici.FullName, "Flux");

            if (Directory.Exists(flux) && Directory.EnumerateFiles(flux, "*.json").Any())
            {
                return flux;
            }

            ici = ici.Parent;
        }

        return null;
    }
}
