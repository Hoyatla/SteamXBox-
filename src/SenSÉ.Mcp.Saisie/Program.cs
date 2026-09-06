using System.Threading.Tasks;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Point d'entree du serveur mcp-saisie : un client MCP sur stdio
/// (JSON-RPC 2.0 newline-delimited).
/// </summary>
/// <remarks>
/// <b>STA + async, pas de WPF au boot.</b> Le thread main est STA pour
/// WPF, mais on ne cree pas l'Application WPF ici : <see cref="Serveur"/>
/// la cree paresseusement au premier appel a ModeExclusif. Avantage :
/// les Console.OpenStandardInput/Output du Serveur marchent sur le main
/// thread, sans etre deranges par un app.Run() qui bloquerait ailleurs.
///
/// <para><b>Pas de Console.Error dans le Main.</b> Un binaire self-contained
/// WinExe-like n'a pas forcement de console ; on ne fait pas d'ecriture
/// au demarrage, on laisse Serveur.DemarrerAsync envoyer la notification
/// "ready" sur stdout (qui est pipe vers le parent).</para>
/// </remarks>
public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        await Serveur.DemarrerAsync().ConfigureAwait(false);
        return 0;
    }
}