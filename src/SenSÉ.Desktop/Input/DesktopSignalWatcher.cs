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
    private readonly FileSystemWatcher _watcher;
    private readonly Action<string> _run;
    private readonly Action<string>? _log;

    /// <summary>Le seul endroit où un signal arrive.</summary>
    /// <remarks>
    /// <b>Il y en a eu deux, le temps que tous les binaires se rejoignent.</b> Les deux bouts de ce
    /// rendez-vous ne sont pas dans le même exécutable — le noyau des manettes écrit, l'environnement
    /// lit — et republier l'un sans l'autre laissait un écrivain déposant à la racine face à un
    /// lecteur n'écoutant que <c>Debug/signal/</c> : le bouton Menu cessait de faire quoi que ce
    /// soit, sans message et sans trace. Le second guetteur couvrait cette fenêtre.
    ///
    /// <para>
    /// Elle est refermée : noyau, claviers et interface sont republiés, et plus rien n'écrit à la
    /// racine. Un guetteur de compatibilité qu'on garde « au cas où » finit par être la raison pour
    /// laquelle personne ne remarque qu'un écrivain est resté en arrière.
    /// </para>
    /// </remarks>
    private static string Endroit => Path.Combine(CheminDebug.Racine, CheminDebug.SousDossierSignal);

    /// <param name="run">Given the signal's file name, on the interface thread.</param>
    public DesktopSignalWatcher(Action<string> run, Action<string>? log = null)
    {
        _run = run;
        _log = log;

        // Narrowed to the signal names. The folder also holds the debug log, which is written
        // constantly — an unfiltered watcher would wake for every line of it.
        CheminDebug.AssurerRacine();

        _watcher = new FileSystemWatcher(Endroit, "desktop-*.signal")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        _watcher.Created += OnSignal;
        _watcher.Changed += OnSignal;
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

        _watcher.EnableRaisingEvents = true;
        _log?.Invoke("controller signals: watching.");
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
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

    /// <summary>Removes a signal, and says whether it was there to remove.</summary>
    /// <remarks>
    /// Effacer est ce qui rend l'action unique : Windows lève <c>Created</c> et <c>Changed</c> pour
    /// une seule écriture, et sans cela le geste partirait deux fois.
    /// </remarks>
    private static bool Consume(string name)
    {
        try
        {
            var path = Path.Combine(Endroit, name);

            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
