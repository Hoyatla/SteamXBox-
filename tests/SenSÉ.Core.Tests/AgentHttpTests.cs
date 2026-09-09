using System.Net.Http;
using SenSÉ.Tools.Search;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le nom sous lequel le produit se présente, et pourquoi il n'a pas d'accent.
/// </summary>
/// <remarks>
/// <b>Ces épreuves existent parce qu'un défaut a dormi jusqu'à l'utilisateur.</b>
/// <c>SearxngClient</c> se présentait comme « SenSÉ », accent compris, dans un champ statique.
/// Une valeur d'en-tête HTTP ne peut pas porter de caractère non-ASCII : .NET le vérifie à l'ajout
/// et lève — et depuis un champ statique, cela ressort en <c>TypeInitializationException</c> au
/// premier usage de la classe.
///
/// <para>
/// Rien ne l'avait attrapé parce que la classe n'était jamais touchée : elle ne sert que si une
/// instance de recherche est configurée, et aucune ne l'avait jamais été. Le jour où une instance
/// a été installée, la toute première recherche a rendu « The type initializer for
/// 'SenSÉ.Desktop.Search.SearxngClient' threw an exception » — une phrase dans laquelle rien ne
/// mène à un accent dans un en-tête.
/// </para>
/// </remarks>
public class AgentHttpTests
{
    /// <summary>Le nom du produit tient dans un en-tête HTTP.</summary>
    /// <remarks>
    /// C'est l'épreuve qui aurait évité la panne : elle fait exactement ce que faisait le champ
    /// statique — ajouter la valeur à de vrais en-têtes, avec la validation de .NET.
    /// </remarks>
    [Fact]
    public void TheProductsNameFitsInAnHttpHeader()
    {
        using var client = new HttpClient();

        // Add valide ; TryAddWithoutValidation ne validerait rien et laisserait passer le defaut.
        client.DefaultRequestHeaders.Add("User-Agent", AgentHttp.Nom);

        Assert.Contains(AgentHttp.Nom, client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
    }

    /// <summary>L'agent de navigateur aussi : il part dans le même genre d'en-tête.</summary>
    [Fact]
    public void TheBrowserAgentFitsToo()
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Add("User-Agent", AgentHttp.Navigateur);

        Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent.ToString());
    }

    /// <summary>Aucun caractère non-ASCII, quelle que soit la façon dont on l'écrit.</summary>
    /// <remarks>
    /// La borne est dite ici en toutes lettres : quelqu'un qui « corrigerait » le nom en y remettant
    /// l'accent — c'est le nom du produit, après tout — reverrait la panne, et cette épreuve la
    /// nomme avant lui.
    /// </remarks>
    [Theory]
    [InlineData(nameof(AgentHttp.Nom))]
    [InlineData(nameof(AgentHttp.Navigateur))]
    public void NothingOutsideAsciiEverGoesIntoAHeader(string champ)
    {
        var valeur = champ == nameof(AgentHttp.Nom) ? AgentHttp.Nom : AgentHttp.Navigateur;

        Assert.All(valeur, lettre => Assert.InRange(lettre, (char)0x20, (char)0x7E));
    }

    // Un agent vide ferait refuser la requete par une partie des serveurs, ce qui est le probleme
    // que ce nom existe pour eviter.
    [Fact]
    public void TheAgentIsNeverEmpty()
    {
        Assert.NotEmpty(AgentHttp.Nom);
        Assert.NotEmpty(AgentHttp.Navigateur);
    }
}
