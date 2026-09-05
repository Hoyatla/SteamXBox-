using System.IO.Pipes;
using System.Text;

namespace SenSÉ.App.Console;

/// <summary>
/// Le point d'écoute que le serveur MCP interroge.
/// </summary>
/// <remarks>
/// Un tuyau nommé, une question par ligne, une réponse par ligne. C'est l'idiome que ce produit
/// emploie déjà pour les haptiques ; en inventer un troisième à côté des tuyaux et des fichiers de
/// signal n'apporterait qu'une façon de plus de se tromper.
///
/// <para>
/// <b>En lecture seule, et le serveur MCP ne peut pas en sortir.</b> Ce qui répond est une fonction
/// fournie par l'appelant : le pont ne sait rien du produit, ne tient aucun état et ne peut donc
/// rien lui faire faire. Le jour où une commande sera exposée, elle passera par cette même fonction,
/// avec sa confirmation dessinée par l'environnement — pas par un élargissement discret d'ici.
/// </para>
///
/// <para>
/// Chaque client est servi puis raccroché, et le tuyau est réouvert. Un client MCP est lancé et tué
/// par l'application du modèle à chaque conversation : un pont qui garderait la première connexion
/// serait muet dès la deuxième, ce qui ressemble exactement à un produit qui ne marche pas.
/// </para>
/// </remarks>
public static class McpBridge
{
    /// <summary>Le nom du tuyau, côté produit comme côté serveur MCP.</summary>
    public const string PipeName = "SenSÉ-mcp";

    /// <summary>
    /// Ouvre le point d'écoute et le tient jusqu'à l'arrêt.
    /// </summary>
    /// <param name="repondre">
    /// Ce qui répond à une question. Appelé sur le fil du pont, jamais sur celui des manettes.
    /// </param>
    /// <param name="journal">Pour dire ce qui a été demandé ; un pont muet ne se diagnostique pas.</param>
    public static void Start(Func<string, string> repondre, Action<string>? journal = null)
    {
        var fil = new Thread(() => Boucle(repondre, journal))
        {
            IsBackground = true,
            Name = "mcp-bridge",
        };

        fil.Start();
    }

    private static void Boucle(Func<string, string> repondre, Action<string>? journal)
    {
        while (true)
        {
            try
            {
                // Une seule instance : deux produits qui tourneraient en même temps ne doivent pas
                // se disputer le tuyau, et le second saura qu'il est le second.
                using var tuyau = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                tuyau.WaitForConnection();

                using var lecteur = new StreamReader(tuyau, new UTF8Encoding(false), leaveOpen: true);
                using var ecrivain = new StreamWriter(tuyau, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true,
                };

                if (lecteur.ReadLine() is not { } question)
                {
                    continue;
                }

                journal?.Invoke($"MCP: {question}");

                string reponse;

                try
                {
                    reponse = repondre(question.Trim());
                }
                catch (Exception exception)
                {
                    // Une question qui fait échouer le produit ne doit pas faire tomber le pont, et
                    // encore moins le processus qui pilote les manettes.
                    reponse = $"erreur: {exception.GetType().Name}";
                }

                // Une réponse tient sur une ligne : les sauts sont échappés plutôt que d'inventer un
                // encadrement de messages pour un besoin qui ne l'exige pas encore.
                ecrivain.WriteLine(reponse.Replace("\r", "").Replace("\n", "\\n"));

                // Vidage du tuyau avant de raccrocher, sinon la réponse peut mourir avec la
                // connexion. L'appel n'existe que sur Windows ; ailleurs, la fermeture ordonnée du
                // flux suffit, et le garde est ce qui permet à ce fichier de compiler sans cible
                // de plateforme — le jour où l'hôte n'est plus Windows, il n'y a rien à réécrire.
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        tuyau.WaitForPipeDrain();
                    }
                }
                catch (Exception)
                {
                    // Le client a déjà raccroché : sa réponse est partie, il n'y a rien à sauver.
                }
            }
            catch (Exception)
            {
                // Le pont se rouvre. Il ne doit jamais emporter le produit avec lui.
                Thread.Sleep(200);
            }
        }
    }
}
