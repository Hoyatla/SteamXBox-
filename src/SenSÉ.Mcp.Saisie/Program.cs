using System.Threading.Tasks;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Point d'entree du serveur mcp-saisie : un serveur HTTP sur 127.0.0.1.
/// </summary>
/// <remarks>
/// <b>Argument --port.</b> Le port est obligatoire (ou a 8766 par defaut).
/// SenSÉ.Desktop lance mcp-saisie avec <c>--port 8766</c> (ou un port
/// dynamique si plusieurs instances tournent en parallele, mais pour le
/// MVP un port fixe suffit).
///
/// <para><b>Log sur stderr.</b> Toutes les traces (port en ecoute, connexion
/// entrante, erreurs) vont sur stderr. stdout reste muet pour ne pas
/// interferer avec un hypothetique client stdout (par exemple si quelqu'un
/// branche un wrapper JSON-RPC plus tard).</para>
/// </remarks>
public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        int port = 8766; // defaut
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out var p))
            {
                port = p;
            }
        }

        try
        {
            await Serveur.DemarrerAsync(port);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"mcp-saisie crash: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
