using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Cdp;

/// <summary>
/// Trouve un navigateur Chromium-compatible deja installe (Edge, Chrome,
/// Chromium) et le lance avec <c>--remote-debugging-port=PORT</c>.
/// </summary>
/// <remarks>
/// <b>Pas de CEF bundle.</b> On parle directement au navigateur deja
/// present sur la machine via Chrome DevTools Protocol. Edge est le
/// defaut sur Windows 10+ et est Chromium-compatible (memes flags et
/// memes API CDP). Chrome est le fallback. Sur Linux, on cherche
/// chromium / chromium-browser / google-chrome.
///
/// <para><b>User-data-dir dedie.</b> Pour eviter de polluer le profil
/// de l'utilisateur, on lance le navigateur avec un <c>--user-data-dir</c>
/// separe, sous <c>Outils/CdpUserData/</c>. C'est la convention CDP
/// recommandee pour ne pas deranger les sessions existantes.</para>
///
/// <para><b>--remote-allow-origins=*.</b> Sans ce flag, Edge/Chrome
/// recents peuvent refuser les connexions CDP loopback avec un warning
/// CORS. On autorise tout (<c>*</c>) parce qu'on est en 127.0.0.1
/// (confiance implicite du local).</para>
///
/// <para><b>Fenetre visible.</b> On ne passe pas <c>--headless</c>,
/// parce que l'Assistant doit voir ce qu'il pilote (et l'utilisateur
/// aussi, pour debugger).</para>
/// </remarks>
public static class BrowserLauncher
{
    /// <summary>Trouve un executable de navigateur compatible CDP. Renvoie null si rien.</summary>
    public static string? TrouverExe(out string browserName)
    {
        Console.Error.WriteLine("BrowserLauncher: recherche d'un navigateur Chromium-compatible...");
        var pathsTestes = new System.Collections.Generic.List<string>();
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

        userDataDir ??= Path.Combine(AppContext.BaseDirectory, "Outils", "CdpUserData");
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
        return Process.Start(psi) ?? throw new InvalidOperationException("lancement du navigateur a renvoye null");
    }
}
