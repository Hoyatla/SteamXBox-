using System.Diagnostics;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Desktop;

/// <summary>
/// Les trois serveurs qui donnent à l'Assistant prise sur la machine : pc-agent pour UI Automation,
/// mcp-saisie pour la souris et le clavier, mcp-cdp pour les webapps.
/// </summary>
/// <remarks>
/// <b>Ils vivent avec la fenêtre de l'Assistant, pas avec l'environnement.</b> C'est la règle que
/// <c>ServeurModele</c> énonce déjà pour le modèle de langage — « il vit
/// tant que la fenêtre de l'assistant est ouverte, et il s'arrête quand elle se ferme » — et il n'y
/// avait aucune raison que ces trois-là y échappent. Ils n'ont pas d'autre client : personne
/// d'autre dans SenSÉ n'appelle UIA, n'injecte une frappe ou ne pilote un navigateur.
///
/// <para>Les garder ouverts pour toute la session, comme c'était le cas, laissait trois processus
/// et un navigateur Edge tourner pendant des heures pour une conversation refermée — dont un
/// mcp-saisie capable d'injecter des frappes et un Edge en écoute sur un port de débogage. Une
/// capacité qui ne sert plus n'a pas à rester offerte.</para>
///
/// <para><b>Idempotent des deux côtés.</b> On ouvre et on referme la fenêtre de l'Assistant
/// souvent ; <see cref="Demarrer"/> ne relance pas ce qui tourne déjà, et <see cref="Arreter"/> ne
/// se plaint pas de ce qui est déjà parti. Le verrou sérialise les deux : rien n'interdit de
/// refermer la fenêtre pendant que le démarrage est en cours.</para>
/// </remarks>
internal static class ServeursMcp
{
    private static readonly object _verrou = new();

    private static Process? _pcAgent;
    private static Process? _saisie;
    private static Process? _cdp;

    /// <summary>Démarre les trois serveurs. Sans effet sur ceux qui tournent déjà.</summary>
    public static void Demarrer()
    {
        lock (_verrou)
        {
            DemarrerPcAgent();
            DemarrerSaisie();
            DemarrerCdp();
        }
    }

    /// <summary>Arrête les trois serveurs et tout ce qu'ils ont lancé.</summary>
    /// <remarks>
    /// <c>entireProcessTree</c> emporte ce que le serveur a lui-même ouvert — le navigateur d'Edge
    /// derrière mcp-cdp, notamment, qui autrement survivrait à son pilote. Le job de
    /// <see cref="JobEnfants"/> reste le filet en dessous : il rattrape le cas où l'environnement
    /// est tué sans que ceci s'exécute.
    /// </remarks>
    public static void Arreter()
    {
        lock (_verrou)
        {
            _pcAgent = Terminer(_pcAgent, "pc-agent");
            _saisie = Terminer(_saisie, "mcp-saisie");
            _cdp = Terminer(_cdp, "mcp-cdp");
        }
    }

    private static Process? Terminer(Process? processus, string nom)
    {
        if (processus is null) return null;

        try
        {
            if (!processus.HasExited)
            {
                UiLog.Info($"arret de {nom} (PID {processus.Id}) : la fenetre de l'assistant est fermee.");
                processus.Kill(entireProcessTree: true);
            }
            processus.Dispose();
        }
        catch (Exception ex)
        {
            UiLog.Failure($"arret de {nom}", ex);
        }

        return null;
    }

    private static bool Vivant(Process? processus)
    {
        try { return processus is not null && !processus.HasExited; }
        catch { return false; }
    }

    private static void DemarrerPcAgent()
    {
        if (Vivant(_pcAgent)) return;

        try
        {
            var cheminPcAgent = System.IO.Path.Combine(AppContext.BaseDirectory, "Outils", "DebugAgent", "pc-agent.exe");
            var cheminToken = System.IO.Path.Combine(AppContext.BaseDirectory, "Outils", "DebugAgent", "token.txt");
            if (!System.IO.File.Exists(cheminPcAgent) || !System.IO.File.Exists(cheminToken))
            {
                UiLog.Warn($"pc-agent non demarre (binaire: {cheminPcAgent}, token: {cheminToken})");
                return;
            }

            var token = System.IO.File.ReadAllText(cheminToken).Trim();
            var psi = new ProcessStartInfo
            {
                FileName = cheminPcAgent,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--bind");
            psi.ArgumentList.Add("127.0.0.1");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add("8765");
            psi.ArgumentList.Add("--token");
            psi.ArgumentList.Add(token);
            psi.ArgumentList.Add("--roots");
            psi.ArgumentList.Add("C:\\\\");

            var proc = Process.Start(psi);
            if (proc is not null)
            {
                _pcAgent = proc;
                JobEnfants.Inscrire(proc);

                // Le PID sert a AssistantDebug pour ceder au pc-agent le droit de mettre une
                // fenetre au premier plan (AllowSetForegroundWindow). Windows ne l'accorde qu'au
                // processus qui possede deja ce plan, et c'est SenSÉ.Desktop quand l'utilisateur
                // vient de parler a l'Assistant : lui seul peut donc le transmettre.
                Environment.SetEnvironmentVariable(
                    "SENSE_DEBUG_AGENT_PID", proc.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                UiLog.Info($"pc-agent demarre: PID {proc.Id}, port 8765, token dans {cheminToken}");
            }
            else
            {
                UiLog.Warn("pc-agent demarre: Process.Start a renvoye null");
            }

            // Env vars mises a disposition de tous les subprocess SenSE.
            Environment.SetEnvironmentVariable("SENSE_DEBUG_AGENT_URL", "http://127.0.0.1:8765");
            Environment.SetEnvironmentVariable("SENSE_DEBUG_AGENT_TOKEN", token);
        }
        catch (Exception ex)
        {
            UiLog.Failure("demarrage du pc-agent", ex);
        }
    }

    private static void DemarrerSaisie()
    {
        if (Vivant(_saisie)) return;

        try
        {
            var cheminSaisie = System.IO.Path.Combine(AppContext.BaseDirectory, "Outils", "McpSaisie", "SenSÉ.Mcp.Saisie.exe");
            if (!System.IO.File.Exists(cheminSaisie))
            {
                UiLog.Warn($"mcp-saisie non demarre (binaire introuvable: {cheminSaisie})");
                return;
            }

            var psiSaisie = new ProcessStartInfo
            {
                FileName = cheminSaisie,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psiSaisie.ArgumentList.Add("--port");
            psiSaisie.ArgumentList.Add("8766");

            var procSaisie = Process.Start(psiSaisie);
            if (procSaisie is not null)
            {
                _saisie = procSaisie;
                JobEnfants.Inscrire(procSaisie);
                BrancherJournal(procSaisie, "mcp-saisie");
                Environment.SetEnvironmentVariable("SENSE_SAISIE_URL", "http://127.0.0.1:8766");
                SenSÉ.Tools.Assistant.AssistantSaisie.Demarrer("http://127.0.0.1:8766");
                UiLog.Info($"mcp-saisie demarre: PID {procSaisie.Id}, port 8766");
            }
            else
            {
                UiLog.Warn("mcp-saisie: Process.Start a renvoye null");
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure("demarrage de mcp-saisie", ex);
        }
    }

    private static void DemarrerCdp()
    {
        if (Vivant(_cdp)) return;

        try
        {
            var cheminCdp = System.IO.Path.Combine(AppContext.BaseDirectory, "Outils", "Cdp", "SenSÉ.Mcp.Cdp.exe");
            if (!System.IO.File.Exists(cheminCdp))
            {
                UiLog.Warn($"mcp-cdp non demarre (binaire introuvable: {cheminCdp})");
                return;
            }

            // Log du chemin Edge/Chrome detecte pour debugging.
            var cheminBrowser = SenSÉ.Mcp.Bus.BrowserLauncher.TrouverExe(out var nomBrowser);
            if (string.IsNullOrEmpty(cheminBrowser))
            {
                UiLog.Warn("mcp-cdp: aucun navigateur Chromium-compatible trouve (Edge/Chrome/Chromium). mcp-cdp va quand meme essayer.");
            }
            else
            {
                UiLog.Info($"mcp-cdp: navigateur detecte = {nomBrowser} ({cheminBrowser})");
            }

            var psiCdp = new ProcessStartInfo
            {
                FileName = cheminCdp,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psiCdp.ArgumentList.Add("--port");
            psiCdp.ArgumentList.Add("9224");

            var procCdp = Process.Start(psiCdp);
            if (procCdp is not null)
            {
                _cdp = procCdp;
                JobEnfants.Inscrire(procCdp);
                BrancherJournal(procCdp, "mcp-cdp");
                Environment.SetEnvironmentVariable("SENSE_CDP_URL", "http://127.0.0.1:9224");
                SenSÉ.Tools.Assistant.AssistantCdp.Demarrer("http://127.0.0.1:9224");
                UiLog.Info($"mcp-cdp demarre: PID {procCdp.Id}, port 9224 (CDP debug sur 9223)");
            }
            else
            {
                UiLog.Warn("mcp-cdp: Process.Start a renvoye null");
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure("demarrage de mcp-cdp", ex);
        }
    }

    /// <summary>
    /// Detourne la sortie d'erreur d'un serveur MCP vers un fichier lisible, a cote des
    /// autres journaux.
    /// </summary>
    /// <remarks>
    /// <b>Pourquoi ce detour.</b> mcp-saisie et mcp-cdp tracent tout sur stderr : port en
    /// ecoute, connexion entrante, duree de chaque requete, boucle de sonde CDP, crash.
    /// Sans redirection, l'enfant herite du handle stderr de son parent — et SenSÉ.Desktop
    /// etant une WinExe sans console, ce handle n'existe pas. Chaque ligne partait donc
    /// dans le vide, y compris celles ajoutees expres pour comprendre les
    /// « HttpRequestException » de l'Assistant. Une trace qu'on ne peut pas lire n'est pas
    /// une trace.
    ///
    /// <para>Le fichier s'ouvre en ajout et porte un en-tete par session, pour qu'on suive
    /// plusieurs demarrages sans perdre le precedent. Les lignes qui annoncent une erreur
    /// remontent en plus dans le journal de l'environnement, ou elles voisinent avec le
    /// « mcp-saisie demarre » correspondant.</para>
    ///
    /// <para>BeginErrorReadLine est indispensable, et pas un confort : rediriger un tuyau
    /// sans le lire le remplit, et l'enfant finit par bloquer sur son propre
    /// Console.Error.WriteLine. Rediriger sans lire est pire que ne pas rediriger.</para>
    /// </remarks>
    private static void BrancherJournal(Process processus, string nom)
    {
        // Sous Debug/debug/, avec les autres journaux, et non a la racine du produit ou ils
        // voisinaient avec les binaires.
        CheminDebug.AssurerRacine();

        var chemin = CheminDebug.DebugLog(nom);
        var verrou = new object();

        try
        {
            System.IO.File.AppendAllText(chemin,
                $"{Environment.NewLine}=== {nom} PID {processus.Id} demarre le {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch (Exception ex)
        {
            UiLog.Failure($"ouverture du journal {nom}", ex);
            return;
        }

        processus.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null) return;

            try
            {
                lock (verrou)
                {
                    System.IO.File.AppendAllText(chemin,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {args.Data}{Environment.NewLine}");
                }
            }
            catch
            {
                // Un journal qui ne peut pas s'ecrire ne doit pas arreter le serveur qu'il
                // observe : disque plein, fichier tenu par un editeur, peu importe.
            }

            if (args.Data.Contains("erreur", StringComparison.OrdinalIgnoreCase)
                || args.Data.Contains("crash", StringComparison.OrdinalIgnoreCase))
            {
                UiLog.Warn($"{nom}: {args.Data}");
            }
        };

        processus.BeginErrorReadLine();
    }
}
