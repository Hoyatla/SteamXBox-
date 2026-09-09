using System.IO;
using System.Windows;
using SenSÉ.Core.Diagnostics;
using SenSÉ.Core.Runtime;

namespace SenSÉ.Desktop.Input;

/// <summary>
/// Runs what a controller asked for, from the other side of the process boundary.
/// </summary>
/// <remarks>
/// The controller runtime drops a file; this notices it, deletes it, and runs the action on the
/// interface thread. Two shortcuts that exist on the keyboard — two taps of Shift for the launcher,
/// two taps of the section key to clear the screen — reach the same code from a gamepad.
///
/// <para>
/// <b>Watched, not polled.</b> A timer looking for two files several times a second would cost
/// nothing measurable, and it is still the wrong shape: this project already shipped a four-times-a-
/// second poll that made every text field on the machine sticky. <c>FileSystemWatcher</c> is told by
/// Windows, so an idle session does no work at all and a press is acted on at once rather than up to
/// an interval late.
/// </para>
/// </remarks>
public sealed class DesktopSignalWatcher : IDisposable
{
    private readonly FileSystemWatcher[] _watchers;
    private readonly Action<string> _run;
    private readonly Action<string>? _log;

    /// <summary>
    /// Les deux endroits où un signal peut arriver : le bon, et celui d'avant.
    /// </summary>
    /// <remarks>
    /// <b>Les deux bouts de ce rendez-vous ne sont pas dans le même exécutable.</b> Le signal est
    /// écrit par le noyau des manettes et lu ici ; republier l'un sans l'autre laisse un écrivain
    /// qui dépose à la racine face à un lecteur qui n'écoute que <c>Debug/signal/</c>, et le bouton
    /// Menu cesse de faire quoi que ce soit — sans message, sans trace, sans rien à quoi
    /// l'utilisateur puisse rattacher la panne.
    ///
    /// <para>
    /// Écouter les deux coûte un second guetteur inactif et supprime la fenêtre entière pendant
    /// laquelle les binaires ne sont pas tous à jour. Ce n'est pas une tolérance à la pollution :
    /// <see cref="Consume"/> efface ce qu'il consomme, donc un ancien noyau qui écrit à la racine
    /// s'y fait nettoyer au passage. La ligne ci-dessous part le jour où plus aucun binaire
    /// d'avant ne tourne.
    /// </para>
    /// </remarks>
    private static string[] Endroits =>
    [
        Path.Combine(CheminDebug.Racine, CheminDebug.SousDossierSignal),
        AppContext.BaseDirectory,
    ];

    /// <param name="run">Given the signal's file name, on the interface thread.</param>
    public DesktopSignalWatcher(Action<string> run, Action<string>? log = null)
    {
        _run = run;
        _log = log;

        // Narrowed to the signal names. The folders also hold the debug log and the binaries —
        // an unfiltered watcher would wake for every line written.
        CheminDebug.AssurerRacine();

        _watchers = [.. Endroits.Select(ou =>
        {
            var guetteur = new FileSystemWatcher(ou, "desktop-*.signal")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };

            guetteur.Created += OnSignal;
            guetteur.Changed += OnSignal;

            return guetteur;
        })];
    }

    /// <summary>Starts listening, after clearing anything left by a previous session.</summary>
    /// <remarks>
    /// The sweep is not housekeeping. A signal left behind by a session that ended before reading it
    /// would fire the moment the environment starts, and the user would see the screen clear itself
    /// for no reason they could connect to anything.
    /// </remarks>
    public void Start()
    {
        foreach (var name in new[] { DesktopSignal.Search, DesktopSignal.ClearScreen })
        {
            Consume(name);
        }

        foreach (var guetteur in _watchers)
        {
            guetteur.EnableRaisingEvents = true;
        }

        _log?.Invoke("controller signals: watching.");
    }

    public void Dispose()
    {
        foreach (var guetteur in _watchers)
        {
            guetteur.EnableRaisingEvents = false;
            guetteur.Dispose();
        }
    }

    private void OnSignal(object sender, FileSystemEventArgs e)
    {
        var name = e.Name;

        if (name is null)
        {
            return;
        }

        // Posted to the interface thread: the watcher calls back on a thread pool thread, and
        // everything these signals lead to touches windows.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Deleted before acting, and only acted on if the delete succeeded. Windows raises both
            // Created and Changed for one write, so without this the action would run twice.
            if (Consume(name))
            {
                _log?.Invoke($"controller signal: {name}.");
                _run(name);
            }
        });
    }

    /// <summary>Removes a signal wherever it landed, and says whether it was there to remove.</summary>
    /// <remarks>
    /// Les deux endroits, dans l'ordre : effacer est ce qui rend l'action unique — Windows lève
    /// <c>Created</c> et <c>Changed</c> pour une seule écriture, et sans cela le geste partirait
    /// deux fois.
    /// </remarks>
    private static bool Consume(string name)
    {
        foreach (var ou in Endroits)
        {
            try
            {
                var path = Path.Combine(ou, name);

                if (!File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }
}
