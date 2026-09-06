using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Trouve un navigateur Chromium-compatible deja installe (Edge, Chrome,
/// Chromium) et le lance avec <c>--remote-debugging-port=PORT</c>.
/// </summary>
/// <remarks>
/// <b>Vit dans SenSÉ.Mcp.Bus, pas dans mcp-cdp.</b> SenSÉ.Desktop a besoin
/// d'appeler <see cref="TrouverExe"/> pour logger le chemin du navigateur
/// au boot, AVANT de lancer le subprocess mcp-cdp. Comme mcp-cdp est un
/// executable (et pas une library), le mettre en ProjectReference dans
/// SenSÉ.Desktop produit un conflit NETSDK (exe non-autonome reference
/// par exe autonome). D'ou le deplacement dans Bus.
///
/// <para><b>User-data-dir par defaut sous %LOCALAPPDATA%.</b>
/// <c>C:\Program Files\</c> n'est pas writable en non-admin, et Edge ne
/// peut pas y creer son profil. On force donc
/// <c>%LOCALAPPDATA%\SenSE\CdpUserData\</c>.</para>
///
/// <para><b>--remote-allow-origins=*.</b> Sans ce flag, Edge/Chrome
/// recents refusent les connexions CDP loopback avec un warning CORS.</para>
/// </remarks>
public static class BrowserLauncher
{
    /// <summary>Trouve un executable de navigateur compatible CDP. Renvoie null si rien.</summary>
    public static string? TrouverExe(out string browserName)
    {
        Console.Error.WriteLine("BrowserLauncher: recherche d'un navigateur Chromium-compatible...");
        var pathsTestes = new System.Collections.Generic.List<string>();

        // Le Chromium du projet passe avant tout le reste.
        //
        // Ce qui suit n'est qu'un repli : on y herite du navigateur de la machine, de sa version et
        // de ses mises a jour silencieuses. Le pilotage par CDP repose sur des details de protocole
        // qui bougent d'une version a l'autre, donc un navigateur qu'on choisit est un comportement
        // qu'on peut tenir, celui de la machine est une surprise a chaque demarrage. Voir
        // ChromiumEmbarque pour l'installer.
        var duProjet = ChromiumEmbarque.Exe();
        pathsTestes.Add(ChromiumEmbarque.Dossier());
        if (duProjet is not null)
        {
            Console.Error.WriteLine($"BrowserLauncher: Chromium du projet a {duProjet}");
            browserName = "Chromium du projet";
            return duProjet;
        }
        Console.Error.WriteLine(
            "BrowserLauncher: Chromium du projet absent — repli sur le navigateur de la machine. "
            + "Pour l'installer : SenSÉ.Mcp.Cdp.exe --installer-chromium");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Edge stable est le defaut (deja sur Windows 10/11)
            var edge = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe");
            pathsTestes.Add(edge);
            if (File.Exists(edge))
            {
                Console.Error.WriteLine($"BrowserLauncher: trouve Microsoft Edge a {edge}");
                browserName = "Microsoft Edge";
                return edge;
            }
            // Edge 32-bit fallback
            var edge86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "Edge", "Application", "msedge.exe");
            pathsTestes.Add(edge86);
            if (File.Exists(edge86))
            {
                Console.Error.WriteLine($"BrowserLauncher: trouve Microsoft Edge (x86) a {edge86}");
                browserName = "Microsoft Edge (x86)";
                return edge86;
            }
            // Edge per-user (WindowsApps) en dernier recours
            var edgeUser = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "Application", "msedge.exe");
            pathsTestes.Add(edgeUser);
            if (File.Exists(edgeUser))
            {
                Console.Error.WriteLine($"BrowserLauncher: trouve Microsoft Edge (user) a {edgeUser}");
                browserName = "Microsoft Edge (user)";
                return edgeUser;
            }
            // Chrome si Edge n'est pas la
            var chrome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe");
            pathsTestes.Add(chrome);
            if (File.Exists(chrome))
            {
                Console.Error.WriteLine($"BrowserLauncher: trouve Google Chrome a {chrome}");
                browserName = "Google Chrome";
                return chrome;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var candidates = new[]
            {
                "/usr/bin/chromium",
                "/usr/bin/chromium-browser",
                "/usr/bin/google-chrome",
                "/snap/bin/chromium",
            };
            foreach (var c in candidates)
            {
                pathsTestes.Add(c);
                if (File.Exists(c))
                {
                    Console.Error.WriteLine($"BrowserLauncher: trouve {c}");
                    browserName = c;
                    return c;
                }
            }
        }
        browserName = "";
        Console.Error.WriteLine("BrowserLauncher: AUCUN navigateur trouve. Paths testes :");
        foreach (var p in pathsTestes)
        {
            Console.Error.WriteLine($"  - {p}");
        }
        return null;
    }

    /// <summary>Lance le navigateur avec --remote-debugging-port=port. Bloque jusqu'au demarrage.</summary>
    public static Process Lancer(int port = 9223, string? exePath = null, string? userDataDir = null)
    {
        string nomNav = "";
        if (exePath is null) exePath = TrouverExe(out nomNav);
        if (exePath is null)
        {
            throw new InvalidOperationException(
                "aucun navigateur Chromium-compatible trouve (Edge, Chrome, ou chromium attendu)");
        }
        Console.Error.WriteLine($"BrowserLauncher: exe={exePath}, port={port}, browser={nomNav}");

        // C:\Program Files\ est non-writable en non-admin ; on met le profil
        // sous %LOCALAPPDATA% pour qu'Edge puisse y ecrire cookies/cache.
        userDataDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SenSÉ", "CdpUserData");
        Directory.CreateDirectory(userDataDir);
        Console.Error.WriteLine($"BrowserLauncher: user-data-dir={userDataDir}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--remote-debugging-port=" + port);
        psi.ArgumentList.Add("--user-data-dir=" + userDataDir);
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        psi.ArgumentList.Add("--disable-background-networking");
        // Accepte les connexions loopback sans warning de securite (sinon Edge
        // peut refuser les requetes CDP avec une erreur de CORS).
        psi.ArgumentList.Add("--remote-allow-origins=*");
        var navigateur = Process.Start(psi) ?? throw new InvalidOperationException("lancement du navigateur a renvoye null");

        // Vider les deux tuyaux, sans rien en faire.
        //
        // Rediriger la sortie d'un enfant sans jamais la lire est un piege a part : le
        // tampon du tube fait quelques kilo-octets, et l'enfant BLOQUE en ecriture des
        // qu'il est plein. Edge ecrit beaucoup sur stderr, en continu — le navigateur
        // finissait donc par se figer quelques minutes apres le demarrage, CDP avec lui,
        // sans qu'aucune erreur ne l'annonce nulle part.
        //
        // Deux sorties possibles : ne plus rediriger du tout, ou lire en permanence. On
        // lit, parce que ne pas rediriger ferait remonter le bavardage d'Edge dans le
        // journal de mcp-cdp, ou il noierait les lignes qui comptent.
        navigateur.OutputDataReceived += (_, _) => { };
        navigateur.ErrorDataReceived += (_, _) => { };
        navigateur.BeginOutputReadLine();
        navigateur.BeginErrorReadLine();

        return navigateur;
    }
}
