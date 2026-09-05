using System.Text.Json;
using System.Text.Json.Serialization;

namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Ce qu'un observateur pousse sur l'EventBus pour declencher l'Assistant
/// ou informer d'un changement d'etat sans demande utilisateur.
/// </summary>
public sealed class Evenement
{
    /// <summary>Source de l'evenement : "fichiers", "systeme", "scheduler", "mcp-saisie", etc.</summary>
    public string Source { get; set; } = "";

    /// <summary>Type : "fichier.cree", "fichier.modifie", "processus.crash", "timer", etc.</summary>
    public string Type { get; set; } = "";

    /// <summary>Donnees propres a l'evenement (chemin, PID, message libre).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Donnees { get; set; }

    /// <summary>Horodatage UTC. Positionne par l'EventBus a la reception, pas par l'observateur.</summary>
    [JsonIgnore]
    public DateTime Horodatage { get; set; } = DateTime.UtcNow;
}