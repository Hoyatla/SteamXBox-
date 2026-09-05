using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Le format des notifications JSON-RPC 2.0, telles que le standard MCP
/// les definit pour les messages serveur-vers-client (notifications/...).
/// </summary>
/// <remarks>
/// <b>Pas une requete.</b> Une notification n'attend pas de reponse : le
/// client accuse reception implicitement en traitant l'evenement. C'est
/// pour ca que "id" est absent.
///
/// <para><b>Methodes reservees.</b> Le standard en definit trois :
/// notifications/message, notifications/progress, notifications/...
/// Plus tout ce que les clients et serveurs se mettent d'accord.
/// On utilise ici "notifications/evenement" pour pousser un Evenement
/// sur le bus.</para>
/// </remarks>
public static class NotificationMcp
{
    /// <summary>Construit une notification JSON-RPC 2.0 pour un evenement du bus.</summary>
    public static string Depuis(string idNotification, Evenement evenement)
    {
        var noeud = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = $"notifications/{evenement.Source}.{evenement.Type}",
            ["params"] = new JsonObject
            {
                ["id"] = idNotification,
                ["horodatage"] = evenement.Horodatage.ToString("o"),
                ["donnees"] = evenement.Donnees is null
                    ? null
                    : JsonSerializer.SerializeToNode(evenement.Donnees),
            },
        };
        return noeud.ToJsonString();
    }
}

/// <summary>
/// Represente un echange JSON-RPC 2.0 minimal (request ou response).
/// </summary>
public sealed class EchangeJsonRpc
{
    public string? JsonRpc { get; set; } = "2.0";
    public string? Method { get; set; }
    public string? Id { get; set; }
    public JsonNode? Params { get; set; }
    public JsonNode? Result { get; set; }
    public JsonNode? Error { get; set; }

    public bool EstRequete => Method is not null && Id is not null;
    public bool EstNotification => Method is not null && Id is null;
    public bool EstReponse => Result is not null || Error is not null;

    public static string ReponseSucces(string id, JsonNode? result)
    {
        var n = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };
        return n.ToJsonString();
    }

    public static string ReponseErreur(string? id, int code, string message)
    {
        var n = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        return n.ToJsonString();
    }
}