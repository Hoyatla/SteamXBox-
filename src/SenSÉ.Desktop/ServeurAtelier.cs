using System.Diagnostics;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Desktop;

/// <summary>
/// Démarre l'Atelier comme subprocess HTTP sur 127.0.0.1:8770, avec l'environnement.
/// </summary>
/// <remarks>
/// <b>Pourquoi ce démarrage, et pourquoi ici.</b> L'Atelier était jusque-là lancé
/// par un clic sur la tuile — donc absent tant que l'utilisateur n'avait pas
/// pensé à l'ouvrir. L'Assistant en a besoin pour créer et exécuter des graphes,
/// et il ne peut pas demander à l'utilisateur d'ouvrir un outil avant chaque
/// intervention. Le démarrage avec l'environnement rend l'Atelier disponible
/// sans intervention, et la fenêtre reste à la demande : l'utilisateur l'ouvre
/// quand il veut voir ou éditer.
///
/// <para><b>Mode headless.</b> L'Atelier est lancé avec <c>--no-window</c> :
/// la fenêtre WPF ne s'ouvre pas toute seule au boot. Le serveur HTTP et le
/// catalogue de nœuds sont initialisés comme en mode normal, mais
/// <c>app.Run()</c> tourne sans fenêtre. Le verbe HTTP <c>fenetre/ouvrir</c>
/// crée la fenêtre à la demande, sur le Dispatcher WPF du subprocess, et la
/// ferme (Hide, pas Close) quand l'utilisateur la referme. L'Atelier meurt
/// avec l'environnement, via <see cref="JobEnfants"/>.</para>
///
/// <para><b>Pourquoi pas dans <see cref="ServeursMcp"/>.</b> Les trois serveurs
/// MCP vivent avec la fenêtre de l'Assistant. L'Atelier, lui, n'est pas un
/// outil de l'Assistant — c'est un outil de l'environnement, utile aussi à
/// d'autres composants (Modèles, Activité, et l'utilisateur directement via
/// la tuile).</para>
/// </remarks>
internal static class ServeurAtelier
{
    private const int PortDefaut = 8770;
    private const string UrlBase = "http://127.0.0.1:8770";

    private static readonly object _verrou = new();
    private static Process? _atelier;

    /// <summary>Le port sur lequel l'Atelier écoute.</summary>
    public static int Port => PortDefaut;

    /// <summary>L'URL de base du serveur HTTP de l'Atelier.</summary>
    public static string Url => UrlBase;

    /// <summary>Démarre l'Atelier en mode headless sur 127.0.0.1:8770. Idempotent.</summary>
    public static void Demarrer()
    {
        lock (_verrou)
        {
            if (Vivant(_atelier)) return;

            try
            {
                var cheminAtelier = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "Outils", "Atelier", "SenSÉ.Atelier.exe");
                if (!System.IO.File.Exists(cheminAtelier))
                {
                    UiLog.Warn($"atelier non démarré (binaire introuvable: {cheminAtelier})");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = cheminAtelier,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                psi.ArgumentList.Add("--port");
                psi.ArgumentList.Add(PortDefaut.ToString(System.Globalization.CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--no-window");

                var proc = Process.Start(psi);
                if (proc is not null)
                {
                    _atelier = proc;
                    JobEnfants.Inscrire(proc);
                    BrancherJournal(proc, "atelier");

                    // Env var mise à disposition de tous les subprocess SenSÉ qui en auraient besoin
                    // sans dépendre de la classe statique (ex: un futur plugin qui parle à l'Atelier).
                    Environment.SetEnvironmentVariable("SENSE_ATELIER_URL", UrlBase);

                    // Le client HTTP de l'Assistant, créé paresseusement à la première Capacite.
                    // Si l'Assistant n'est jamais ouvert, c'est un no-op.
                    SenSÉ.Tools.Assistant.AssistantAtelier.Demarrer(UrlBase);

                    UiLog.Info($"atelier démarré: PID {proc.Id}, port {PortDefaut} (mode headless)");
                }
                else
                {
                    UiLog.Warn("atelier: Process.Start a renvoyé null");
                }
            }
            catch (Exception ex)
            {
                UiLog.Failure("démarrage de l'atelier", ex);
            }
        }
    }

    /// <summary>Le subprocess tourne-t-il encore ?</summary>
    public static bool EstDemarre
    {
        get
        {
            lock (_verrou) return Vivant(_atelier);
        }
    }

    /// <summary>Arrête l'Atelier. Idempotent. Le filet de JobEnfants reste en dessous.</summary>
    public static void Arreter()
    {
        lock (_verrou)
        {
            _atelier = Terminer(_atelier, "atelier");
        }
    }

    private static bool Vivant(Process? processus)
    {
        try { return processus is not null && !processus.HasExited; }
        catch { return false; }
    }

    private static Process? Terminer(Process? processus, string nom)
    {
        if (processus is null) return null;

        try
        {
            if (!processus.HasExited)
            {
                UiLog.Info($"arrêt de {nom} (PID {processus.Id}) : l'environnement se ferme.");
                processus.Kill(entireProcessTree: true);
            }
            processus.Dispose();
        }
        catch (Exception ex)
        {
            UiLog.Failure($"arrêt de {nom}", ex);
        }

        return null;
    }

    /// <summary>
    /// Détourne la sortie d'erreur de l'Atelier vers un fichier lisible, à côté
    /// des autres journaux. Réutilise le pattern de BrancherJournal dans ServeursMcp
    /// parce que l'Atelier trace lui aussi sur stderr, et que sans redirection
    /// les lignes partiraient dans le vide (WinExe sans console).
    /// </summary>
    private static void BrancherJournal(Process processus, string nom)
    {
        // Sous Debug/debug/, comme ServeursMcp : « a cote des autres journaux » etait deja
        // l'intention ecrite ici, la racine du produit n'etait pas cet endroit.
        SenSÉ.Core.Diagnostics.CheminDebug.AssurerRacine();

        var chemin = SenSÉ.Core.Diagnostics.CheminDebug.DebugLog(nom);
        var verrou = new object();

        try
        {
            System.IO.File.AppendAllText(chemin,
                $"{Environment.NewLine}=== {nom} PID {processus.Id} démarré le {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
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
                // Un journal qui ne peut pas s'écrire ne doit pas arrêter le serveur qu'il observe.
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
