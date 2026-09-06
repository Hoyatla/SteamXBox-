using System.Threading.Tasks;

namespace SenSÉ.Mcp.Cdp;

/// <summary>
/// Point d'entree du serveur mcp-cdp : detecte le navigateur, lance
/// Edge/Chrome avec --remote-debugging-port, connecte CDP, puis
/// ecoute les requetes HTTP loopback pour dispatch aux Capacites.
/// </summary>
public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        int port = 9224; // port du serveur HTTP interne
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
            await Console.Error.WriteLineAsync($"mcp-cdp crash: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
