using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce que le générateur reproche à un flux, tel qu'il le dit lui-même.
/// </summary>
/// <remarks>
/// <b>Le détail était lu, puis jeté.</b> Le bloc <c>error</c> était pris en premier, et comme il
/// porte toujours quelque chose — « Prompt outputs failed validation », qui ne dit rien de plus que
/// « non » — la lecture s'arrêtait là. Le bloc <c>node_errors</c>, où ComfyUI nomme l'entrée fautive
/// et les valeurs qu'il accepterait, n'était jamais atteint.
///
/// <para>
/// Le 23 août, l'assistant a donc reçu la phrase générique et l'a répétée telle quelle à
/// l'utilisateur : il n'avait aucun moyen de savoir quoi corriger. Le générateur, lui, le savait.
/// </para>
/// </remarks>
public class RefusTests
{
    /// <summary>La forme exacte que ComfyUI rend sur une entrée manquante.</summary>
    private const string Manquante = """
    {"error": {"type": "prompt_outputs_failed_validation",
               "message": "Prompt outputs failed validation", "details": ""},
     "node_errors": {"7": {"errors": [
        {"type": "required_input_missing",
         "message": "Required input is missing", "details": "bypass_mode"}]}}}
    """;

    /// <summary>Le message précis survit au message générique.</summary>
    [Fact]
    public void ThePreciseMessageSurvivesTheGenericOne()
    {
        var dit = FluxTravail.Refus(Manquante, 400);

        Assert.Contains("Prompt outputs failed validation", dit, StringComparison.Ordinal);
        Assert.Contains("bypass_mode", dit, StringComparison.Ordinal);
        Assert.Contains("nœud 7", dit, StringComparison.Ordinal);
    }

    /// <summary>Une valeur hors liste est rendue avec les valeurs admises.</summary>
    /// <remarks>
    /// C'est le cas le plus utile de tous : le modèle a nommé un fichier absent du disque, et la
    /// réponse contient la liste de ceux qui existent. Sans elle, il retentera le même nom.
    /// </remarks>
    [Fact]
    public void AValueOutsideTheListComesBackWithTheAllowedOnes()
    {
        var dit = FluxTravail.Refus("""
        {"error": {"message": "Prompt outputs failed validation", "details": ""},
         "node_errors": {"4": {"errors": [
            {"message": "Value not in list",
             "details": "unet_name: 'absent.gguf' not in ['flux1-schnell-Q5_K_S.gguf']"}]}}}
        """, 400);

        Assert.Contains("flux1-schnell-Q5_K_S.gguf", dit, StringComparison.Ordinal);
    }

    /// <summary>Sans rien d'exploitable, le code de réponse reste.</summary>
    /// <remarks>
    /// Une page HTML au lieu d'un document JSON — un serveur mort, un proxy qui s'interpose — ne
    /// doit pas faire disparaître la seule information vraie qu'il reste.
    /// </remarks>
    [Fact]
    public void WithNothingUsableTheStatusCodeRemains()
        => Assert.Contains("503", FluxTravail.Refus("<html>Service Unavailable</html>", 503),
            StringComparison.Ordinal);

    /// <summary>Un nœud sans détail est au moins nommé.</summary>
    [Fact]
    public void ANodeWithoutDetailIsAtLeastNamed()
        => Assert.Contains(
            "nœud 12",
            FluxTravail.Refus("""{"node_errors": {"12": {}}}""", 400),
            StringComparison.Ordinal);
}
