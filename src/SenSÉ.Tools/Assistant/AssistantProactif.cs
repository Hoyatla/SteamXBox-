using SenSÉ.Mcp.Bus;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Declenche l'Assistant sans demande utilisateur, sur la base d'un
/// evenement pousse sur l'EventBus.
/// </summary>
/// <remarks>
/// <b>Strict minimum pour cette premiere iteration.</b> Un seul tour
/// du modele, pas de boucle, pas d'outils MCP dans la reponse. Le but
/// est de montrer la reactivite de l'Assistant a un evenement
/// externe, pas de le laisser piloter la souris des le premier
/// livrable.
///
/// <para><b>La consigne est differente de celle de Repondre.</b> Pas de
/// salutation, pas de demande de confirmation pour une action
/// irreversible, action directe. C'est une consigne de reaction, pas
/// de conversation.</para>
///
/// <para><b>La reponse est prefixee par "[L'Assistant a fait X de
/// lui-meme]".</b> C'est l'engagement que la conversation garde la
/// trace de tout ce que l'Assistant a fait de sa propre initiative,
/// pour que l'utilisateur puisse revenir en arriere.</para>
/// </remarks>
public static class AssistantProactif
{
    private const string ConsigneProactive =
        "Tu es l'Assistant SenSÉ. L'utilisateur n'a rien demande : "
        + "tu reagis a un evenement detecte par un observateur. "
        + "Agis, ne demande pas de permission pour lire ou observer. "
        + "Pour toute action destructive ou externe (supprimer, envoyer, "
        + "eteindre), propose, ne fais pas. "
        + "Reponds court, en francais.";

    /// <summary>
    /// Declenche l'Assistant sur un evenement. Ajoute la raison au fil,
    /// fait UN tour du modele, et rend la reponse prefixee.
    /// </summary>
    public static async Task<string> ProvoquerAsync(
        AssistantLocal assistant,
        Evenement evenement,
        Action<string>? journal = null,
        CancellationToken arret = default)
    {
        if (assistant is null) throw new ArgumentNullException(nameof(assistant));
        if (evenement is null) throw new ArgumentNullException(nameof(evenement));

        var raison = FormaterRaison(evenement);
        journal?.Invoke($"proactif: {evenement.Source}.{evenement.Type} ({raison})");

        // Un tour. Pas de boucle, pas d'outils MCP dans cette premiere
        // iteration. La reponse est prefixee pour que l'utilisateur
        // sache que l'Assistant a agi de sa propre initiative.
        var reponse = await assistant.RepondreProactifAsync(raison, arret).ConfigureAwait(false);
        var prefixe = $"[L'Assistant a fait quelque chose de lui-meme : {evenement.Source}.{evenement.Type}]";
        return $"{prefixe}\n\n{reponse}";
    }

    private static string FormaterRaison(Evenement e)
    {
        var baseTexte = $"Evenement {e.Source}.{e.Type} detecte";
        if (e.Donnees is null || e.Donnees.Count == 0)
        {
            return baseTexte;
        }

        var parties = new List<string> { baseTexte, "Details :" };
        foreach (var (cle, valeur) in e.Donnees)
        {
            parties.Add($"- {cle} : {valeur}");
        }
        return string.Join("\n", parties);
    }
}