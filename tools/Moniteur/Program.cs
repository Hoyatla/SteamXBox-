using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamXBox.Moniteur;

/// <summary>
/// Watches every top-level window and records what changes, while SteamXBox is used.
/// </summary>
/// <remarks>
/// Written because examining the scene afterwards had run out of answers. Three hypotheses were
/// raised and measured away — a handle leak that was noise, an accessibility poll that does not
/// exist, SteamXBox setting flags it never sets — each because the reading was taken minutes after
/// the fault instead of during it.
///
/// <para>
/// <b>What it cannot do, stated plainly.</b> Windows exposes no way to learn which process called
/// <c>SetWindowPos</c> on a window: there is no log and no attribution. The only route to a culprit
/// would be injecting code into other processes, which this project forbids. So this narrows the gap
/// in time instead — twenty milliseconds — until a coincidence stops being believable.
/// </para>
///
/// <para>
/// The first version watched five things chosen by me, which meant it carried my blind spots. This
/// one watches every window and reports every change it can see, whether or not I expected it.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Fast enough that cause and effect stay next to each other in the log.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(20);

    /// <summary>A key held longer than this is stuck rather than pressed.</summary>
    private static readonly TimeSpan TooLong = TimeSpan.FromSeconds(1);

    /// <summary>How much ordinary history is kept, to be printed around an incident.</summary>
    private const int HistoryLines = 400;

    private static StreamWriter _log = StreamWriter.Null;
    private static readonly Queue<string> History = new();

    private static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var path = Path.Combine(AppContext.BaseDirectory, $"moniteur-{DateTime.Now:yyyy-MM-dd-HHmm}.log");
        _log = new StreamWriter(path, append: true) { AutoFlush = true };

        Loud($"Moniteur SteamXBox — {Path.GetFileName(path)}");
        Loud($"Échantillonnage toutes les {Beat.TotalMilliseconds:0} ms. Fermez cette fenêtre pour arrêter.");
        Loud("");

        var windows = Survey();

        foreach (var window in windows.Values.Where(w => w.Topmost))
        {
            Loud($"topmost au départ : {window.Title}");
        }

        Loud($"{windows.Count} fenêtre(s) au départ.");
        Loud("");

        var keys = new Dictionary<int, DateTime>();
        var reported = new HashSet<int>();
        var shellWell = true;
        var foreground = "";

        while (true)
        {
            Thread.Sleep(Beat);

            windows = WatchWindows(windows);
            WatchKeys(keys, reported);
            shellWell = WatchShell(shellWell);
            foreground = WatchForeground(foreground);
        }
    }

    // ---- Windows ----

    private sealed record Seen(string Title, string Class, int Pid, bool Topmost, long ExStyle, bool Visible);

    /// <summary>Every top-level window and the state worth comparing.</summary>
    private static Dictionary<IntPtr, Seen> Survey()
    {
        var found = new Dictionary<IntPtr, Seen>();
        var handle = GetTopWindow(IntPtr.Zero);

        while (handle != IntPtr.Zero)
        {
            var title = TitleOf(handle);

            if (title.Length > 0)
            {
                var ex = GetWindowLongPtrW(handle, -20).ToInt64();
                GetWindowThreadProcessId(handle, out var pid);

                found[handle] = new Seen(title, ClassOf(handle), pid, (ex & 0x8) != 0, ex, IsWindowVisible(handle));
            }

            handle = GetWindow(handle, 2);
        }

        return found;
    }

    /// <summary>
    /// Reports every window that appeared, vanished or changed.
    /// </summary>
    /// <remarks>
    /// Everything, not a chosen list. A style change nobody predicted is exactly what an instrument
    /// built around a hypothesis fails to see, and the hypotheses have been wrong three times.
    /// </remarks>
    private static Dictionary<IntPtr, Seen> WatchWindows(Dictionary<IntPtr, Seen> before)
    {
        var now = Survey();

        foreach (var (handle, window) in now)
        {
            if (!before.TryGetValue(handle, out var was))
            {
                Quiet($"fenêtre ouverte   {Describe(window)}");
                continue;
            }

            if (window.Topmost != was.Topmost)
            {
                Loud($"{(window.Topmost ? "TOPMOST POSÉ  " : "topmost retiré")}  {Describe(window)}");
                Incident();
                continue;
            }

            if (window.ExStyle != was.ExStyle)
            {
                Loud($"STYLE CHANGÉ   {Describe(window)}  0x{was.ExStyle:X} -> 0x{window.ExStyle:X}");
                Incident();
                continue;
            }

            if (window.Visible != was.Visible)
            {
                Quiet($"fenêtre {(window.Visible ? "affichée" : "masquée ")}  {Describe(window)}");
            }
        }

        foreach (var window in before.Where(w => !now.ContainsKey(w.Key)))
        {
            Quiet($"fenêtre fermée    {Describe(window.Value)}");
        }

        return now;
    }

    private static string Describe(Seen window)
        => $"pid {window.Pid,6} {window.Class,-26} « {Cut(window.Title, 52)} »";

    // ---- Keys ----

    private static void WatchKeys(Dictionary<int, DateTime> down, HashSet<int> reported)
    {
        foreach (var (code, name) in Watched)
        {
            var isDown = (GetAsyncKeyState(code) & 0x8000) != 0;

            if (!isDown)
            {
                if (down.Remove(code) && reported.Remove(code))
                {
                    Loud($"RELÂCHÉ  {name}");
                }

                continue;
            }

            if (!down.TryGetValue(code, out var since))
            {
                down[code] = DateTime.Now;
                Quiet($"touche enfoncée   {name}");
                continue;
            }

            if (DateTime.Now - since > TooLong && reported.Add(code))
            {
                Loud($"COINCÉ   {name} depuis {(DateTime.Now - since).TotalSeconds:F1} s");
                Incident();
            }
        }
    }

    // ---- The shell ----

    /// <summary>
    /// Notices the shell freezing.
    /// </summary>
    /// <remarks>
    /// Asked of its windows rather than of the process. <c>Process.Responding</c> only tests the
    /// main message loop, and during a real freeze it answered "well" while the window was frozen —
    /// a wasted measurement that cost an hour.
    /// </remarks>
    private static bool WatchShell(bool wasWell)
    {
        var hung = ShellWindows().Any(IsHungAppWindow);

        if (hung == wasWell)
        {
            Loud(hung ? "SHELL FIGÉ" : "shell répond de nouveau");

            if (hung)
            {
                Incident();
            }
        }

        return !hung;
    }

    private static string WatchForeground(string before)
    {
        var now = ForegroundTitle();

        if (now != before && now.Length > 0)
        {
            Quiet($"premier plan      {Cut(now, 60)}");
        }

        return now;
    }

    // ---- Recording ----

    /// <summary>Something worth seeing: printed, logged, and kept in the history.</summary>
    private static void Loud(string line) => Write(line, loud: true);

    /// <summary>Ordinary traffic: logged and kept, but not shouted at the console.</summary>
    private static void Quiet(string line) => Write(line, loud: false);

    private static void Write(string line, bool loud)
    {
        var stamped = line.Length == 0 ? "" : $"[{DateTime.Now:HH:mm:ss.fff}] {line}";

        _log.WriteLine(stamped);

        if (loud)
        {
            Console.WriteLine(stamped);
        }

        History.Enqueue(stamped);

        while (History.Count > HistoryLines)
        {
            History.Dequeue();
        }
    }

    /// <summary>
    /// Writes down everything around a fault, so nothing has to be reconstructed afterwards.
    /// </summary>
    /// <remarks>
    /// The whole Z order, the processes present, and the history already collected. The question
    /// asked of every incident so far has been "what was happening just before", and the answer had
    /// to be hunted through a file each time.
    /// </remarks>
    private static void Incident()
    {
        _log.WriteLine("    ┌─ contexte ─────────────────────────────────────────");

        foreach (var line in History.TakeLast(40))
        {
            _log.WriteLine($"    │ {line}");
        }

        _log.WriteLine("    ├─ ordre Z ──────────────────────────────────────────");

        var rank = 0;

        foreach (var (_, window) in Survey().Where(w => w.Value.Visible))
        {
            _log.WriteLine($"    │ {++rank,3} {(window.Topmost ? "TOPMOST" : "       ")} {Describe(window)}");
        }

        _log.WriteLine("    ├─ processus SteamXBox ──────────────────────────────");

        foreach (var process in Process.GetProcesses()
                     .Where(p => p.ProcessName.Contains("SteamXBox", StringComparison.OrdinalIgnoreCase)
                                 || p.ProcessName.Contains("Sc2Xboxed", StringComparison.OrdinalIgnoreCase)))
        {
            _log.WriteLine($"    │ {process.ProcessName} (pid {process.Id})");
        }

        _log.WriteLine("    └────────────────────────────────────────────────────");
        _log.Flush();
    }

    // ---- Win32 ----

    private static IEnumerable<IntPtr> ShellWindows()
    {
        foreach (var name in new[] { "Shell_TrayWnd", "CabinetWClass", "Progman" })
        {
            var handle = FindWindowW(name, null);

            if (handle != IntPtr.Zero)
            {
                yield return handle;
            }
        }
    }

    private static string ForegroundTitle() => TitleOf(GetForegroundWindow());

    private static string TitleOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return "";
        }

        var text = new StringBuilder(200);
        GetWindowTextW(handle, text, 200);

        return text.ToString();
    }

    private static string ClassOf(IntPtr handle)
    {
        var text = new StringBuilder(120);
        GetClassNameW(handle, text, 120);

        return text.ToString();
    }

    private static string Cut(string text, int length)
        => text.Length > length ? text[..length] : text;

    private static readonly (int Code, string Name)[] Watched =
    [
        (0xA2, "Ctrl gauche"), (0xA3, "Ctrl droit"),
        (0xA4, "Alt gauche"), (0xA5, "Alt droit"),
        (0xA0, "Maj gauche"), (0xA1, "Maj droit"),
        (0x5B, "Windows gauche"), (0x5C, "Windows droit"),
        (0x01, "Bouton gauche"), (0x02, "Bouton droit"), (0x04, "Bouton milieu"),
        (0x09, "Tab"), (0x1B, "Échap"), (0x0D, "Entrée"), (0xBF, "§"),
    ];

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr handle, out int pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtrW(IntPtr handle, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr handle, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string className, string? windowName);
}
