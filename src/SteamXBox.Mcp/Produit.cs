using System.IO.Pipes;
using System.Text;

namespace SteamXBox.Mcp;

/// <summary>
/// Le client du tuyau : ce qui parle à SteamXBox en cours d'exécution.
/// </summary>
/// <remarks>
/// Le pendant de <c>McpBridge</c>, côté serveur MCP. Une question par ligne, une réponse par ligne,
/// sur le tuyau nommé que le noyau ouvre au démarrage.
///
/// <para>
/// <b>Un produit arrêté n'est pas une panne.</b> Il se dit, et la même règle vaut ici que pour
/// HidHide et LibreOffice : ce qui manque est nommé, jamais tu. Un modèle qui reçoit « SteamXBox
/// n'est pas lancé » sait quoi répondre à l'utilisateur ; un modèle qui reçoit une erreur de tuyau
/// ne sait rien.
/// </para>
///
/// <para>
/// Le délai est court et assumé. Ce serveur répond à un modèle qui attend, et une question qui reste
/// deux secondes sans réponse a déjà coûté la conversation : mieux vaut dire que le produit ne
/// répond pas que de faire patienter.
/// </para>
/// </remarks>
internal static class Produit
{
    /// <summary>Le même nom que du côté du noyau.</summary>
    private const string Tuyau = "steamxbox-mcp";

    private const int Delai = 1500;

    /// <summary>Pose une question au produit, ou dit pourquoi elle n'a pas pu être posée.</summary>
    public static string Demander(string question)
    {
        try
        {
            using var tuyau = new NamedPipeClientStream(".", Tuyau, PipeDirection.InOut);

            try
            {
                tuyau.Connect(Delai);
            }
            catch (Exception)
            {
                return "SteamXBox n'est pas lancé : cette information n'est disponible que quand le produit tourne.";
            }

            using var ecrivain = new StreamWriter(tuyau, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };

            using var lecteur = new StreamReader(tuyau, new UTF8Encoding(false), leaveOpen: true);

            ecrivain.WriteLine(question);

            // Les sauts de ligne sont échappés par le pont, puisqu'une réponse tient sur une ligne.
            return lecteur.ReadLine()?.Replace("\\n", "\n") ?? "SteamXBox a raccroché sans répondre.";
        }
        catch (Exception exception)
        {
            return $"Le produit n'a pas répondu : {exception.GetType().Name}.";
        }
    }
}
