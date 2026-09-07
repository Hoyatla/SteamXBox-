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

    /// <summary>Où les relevés se posent : un dossier à eux, à côté du produit.</summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Un relevé par lancement, nommé à la minute, écrit à la
    /// racine du produit : vingt-cinq fichiers et trente-trois mégaoctets s'y étaient accumulés,
    /// mêlés aux exécutables et aux scripts. Une sortie d'outil qui encombre l'endroit où l'on vient
    /// chercher l'outil.
    ///
    /// <para>
    /// « Moniteur » plutôt que « Monitor debug » : ces relevés ne viennent pas que du lanceur de
    /// débogage — la tuile du moniteur en produit autant — et les dossiers du produit se nomment
    /// déjà d'un mot français sans espace, comme <c>Outils</c>, <c>Modeles</c> et <c>Travaux</c>.
    /// </para>
    ///
    /// <para>
    /// Le dossier ne se crée pas toujours : le produit peut vivre dans Program Files sans que la
    /// session ait de quoi y écrire. On le dit et on reste à la racine plutôt que de refuser de
    /// démarrer — un moniteur qui ne se lance pas est un moniteur qui n'observe rien, et c'est
    /// précisément quand quelque chose va mal qu'on le lance.
    /// </para>
    /// </remarks>
    private static string Journaux()
    {
        var dossier = Path.Combine(AppContext.BaseDirectory, "Moniteur");

        try
        {
            Directory.CreateDirectory(dossier);

            return dossier;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Dossier « Moniteur » impossible ({exception.Message}) : relevé à la racine.");

            return AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// Met la console du moniteur au-dessus de tout, y compris de l'incrustation plein écran.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige, et ce n'en était pas un.</b> « Le moniteur se ferme quand je
    /// lance le lanceur de débogage. » Mesuré le 24 août : il ne se fermait pas. Le processus vit,
    /// sa console existe, elle est visible au sens de Windows — et l'incrustation de SteamXBox
    /// occupe 3 440 × 1 392 pixels à partir de l'origine, prend le premier plan une seconde après,
    /// et l'enterre. Relevé : le moniteur à 312,312 en 993 × 519, le bureau du produit à 0,0 sur
    /// tout l'écran, aucun des deux topmost — c'est l'ordre d'empilement qui décide, et le dernier
    /// arrivé gagne.
    ///
    /// <para>
    /// Un instrument qu'on ne peut pas lire pendant qu'on s'en sert ne sert à rien, et rien ne
    /// coûte plus cher que déboguer l'outil de débogage. Il flotte donc au-dessus, ce qui est la
    /// place d'un instrument : on le déplace ou on le réduit si on veut voir dessous.
    /// </para>
    ///
    /// <para>
    /// Il s'exclut en retour de ses propres relevés — voir <see cref="Survey"/>. Sans cela, le seul
    /// outil capable de dire qui met une fenêtre au-dessus des autres commencerait son journal en
    /// se dénonçant lui-même.
    /// </para>
    /// </remarks>
    private static void Flotter()
    {
        const uint SansBouger = 0x0002 | 0x0001;   // SWP_NOMOVE | SWP_NOSIZE
        var topmost = new IntPtr(-1);              // HWND_TOPMOST

        var console = GetConsoleWindow();

        if (console != IntPtr.Zero)
        {
            SetWindowPos(console, topmost, 0, 0, 0, 0, SansBouger);
        }
    }

    /// <summary>Le guet du pointeur, demandé plutôt que subi.</summary>
    /// <remarks>
    /// <b>Il coûtait à toute la machine, en permanence, pour une question qu'on ne pose presque
    /// jamais.</b> <see cref="PointerPump"/> installe un crochet bas niveau : Windows remet chaque
    /// événement souris à chaque processus crocheté et l'attend — trois cents millisecondes par
    /// défaut — avant que l'événement n'atteigne l'application sous le curseur.
    ///
    /// <para>
    /// Signalé le 7 septembre 2026 : l'assistant génère, le bureau cesse de répondre, et le
    /// processeur reste entre huit et trente pour cent. Pas de la saturation — ce fil ordonnancé en
    /// retard, et chaque mouvement de souris qui l'attend. Tuer ce processus a rendu le bureau
    /// fluide, ce qui l'a désigné.
    /// </para>
    ///
    /// <para>
    /// <b>Le crochet n'est pourtant pas de trop : il est la réponse.</b> Mesuré le même jour :
    /// l'entrée brute — celle qui a remplacé le crochet clavier du bureau — ne rapporte pas la
    /// souris injectée. Inscription acceptée, aucun événement sur dix injections. Or « injecté sans
    /// que le curseur bouge » est précisément ce que cet outil existe pour voir. Convertir l'aurait
    /// rendu muet en silence, ce qui est pire qu'un outil lent.
    /// </para>
    ///
    /// <para>
    /// Reste donc à ne pas le poser quand personne ne l'a demandé. Trois exemplaires orphelins ont
    /// été trouvés dans la même journée, parent mort, crochet vivant depuis des heures. Sans le
    /// drapeau, un moniteur égaré n'observe plus le pointeur et ne coûte plus rien à personne ; les
    /// fenêtres, les touches et le shell restent surveillés comme avant.
    /// </para>
    /// </remarks>
    private const string DrapeauPointeur = "--pointeur";

    private static void Main(string[] arguments)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Flotter();

        var path = Path.Combine(Journaux(), $"moniteur-{DateTime.Now:yyyy-MM-dd-HHmm}.log");
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

        var pointeur = Array.Exists(
            arguments, a => string.Equals(a, DrapeauPointeur, StringComparison.OrdinalIgnoreCase));

        if (pointeur)
        {
            StartPointerWatch();
        }
        else
        {
            Loud($"pointeur : non surveillé. Relancez avec « {DrapeauPointeur} » pour distinguer "
                 + "l'injecté du physique — au prix d'un crochet que toute la machine paie.");
            Loud("");
        }

        while (true)
        {
            Thread.Sleep(Beat);

            windows = WatchWindows(windows);
            WatchKeys(keys, reported);
            shellWell = WatchShell(shellWell);
            foreground = WatchForeground(foreground);
            if (pointeur) { WatchPointer(); }
        }
    }

    // ---- Windows ----

    private sealed record Seen(string Title, string Class, int Pid, bool Topmost, long ExStyle, bool Visible);

    /// <summary>Every top-level window and the state worth comparing.</summary>
    private static Dictionary<IntPtr, Seen> Survey()
    {
        var found = new Dictionary<IntPtr, Seen>();
        var handle = GetTopWindow(IntPtr.Zero);
        var soi = Environment.ProcessId;

        while (handle != IntPtr.Zero)
        {
            var title = TitleOf(handle);

            if (title.Length > 0)
            {
                GetWindowThreadProcessId(handle, out var aQui);

                // Ses propres fenêtres ne sont pas des observations.
                //
                // Le moniteur se met au-dessus de tout pour rester lisible — voir Flotter — donc il
                // se verrait lui-même passer topmost. Sur un instrument dont l'unique métier est de
                // répondre « qui a mis cette fenêtre au-dessus des autres », se dénoncer soi-même
                // au premier relevé serait pire qu'inutile : c'est une fausse piste servie en tête
                // du journal, exactement là où on cherche le coupable.
                if (aQui == soi)
                {
                    handle = GetWindow(handle, 2);

                    continue;
                }
            }

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

    // ---- The pointer ----

    /// <summary>
    /// Separates what SteamXBox injects, what the real hardware sends, and what the cursor does.
    /// </summary>
    /// <remarks>
    /// Added on 12 August, after a night spent unable to answer "did the cursor stop, or did nothing
    /// ever ask it to move". The product injects pointer motion with <c>SendInput</c>, the user also
    /// has a real mouse, and until now the log showed neither — only windows and key state. Three
    /// separate readings are needed and no two of them can be inferred from each other:
    ///
    /// <list type="bullet">
    ///   <item><b>injected</b> — <c>SendInput</c> reached the input queue, so SteamXBox did its part.</item>
    ///   <item><b>physical</b> — a real mouse moved, which tells whether the user was even trying.</item>
    ///   <item><b>cursor</b> — the pointer actually changed position, which is the only thing the user sees.</item>
    /// </list>
    ///
    /// <para>
    /// Injected motion with a cursor that never moves means something swallows the events downstream.
    /// No injected motion at all means the product never tried — a different fault entirely, and the
    /// one that took a day to name because nothing distinguished it from the first.
    /// </para>
    ///
    /// <para>
    /// <b>The callback does nothing but count.</b> A low-level hook is called on the thread that
    /// installed it and every event in the machine waits for it to return; the keyboard hook in
    /// SteamXBox.Desktop froze the whole machine for 33 seconds by doing real work in one of these.
    /// So: its own thread, no logging, no allocation, and the flags read at a fixed offset rather
    /// than marshalled into a structure. The beat thread does the reporting.
    /// </para>
    /// </remarks>
    private static int _injectedMoves;
    private static int _physicalMoves;
    private static int _injectedClicks;
    private static int _physicalClicks;
    private static int _cursorSteps;

    private static HookProc? _mouseProc;
    private static (int X, int Y) _cursorWas;
    private static DateTime _pointerReportedAt = DateTime.Now;

    /// <summary>Enough motion in one second that a motionless cursor cannot be a coincidence.</summary>
    private const int EnoughToExpectMovement = 5;

    private static void StartPointerWatch()
    {
        // Primed, or the first sample compares against (0,0) and reports a movement that never
        // happened. A single false step in the first second is small and completely misleading.
        if (GetCursorPos(out var start))
        {
            _cursorWas = (start.X, start.Y);
        }

        var thread = new Thread(PointerPump) { IsBackground = true, Name = "moniteur-pointeur" };
        thread.Start();
    }

    private static void PointerPump()
    {
        // Held in a field: a delegate passed to Win32 and then collected leaves the hook calling
        // into freed memory, which takes the whole desktop's input with it.
        _mouseProc = OnMouseEvent;

        var hook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, GetModuleHandleW(null), 0);

        if (hook == IntPtr.Zero)
        {
            Loud($"pointeur : crochet indisponible ({Marshal.GetLastWin32Error()}). Injecté et physique ne seront pas distingués.");
            return;
        }

        Loud("pointeur : crochet posé — injecté, physique et curseur sont distingués.");

        // A low-level hook is only delivered to a thread that pumps messages.
        while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        UnhookWindowsHookEx(hook);
    }

    private static IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            // Offset 12 in MSLLHOOKSTRUCT: two ints of position, then mouseData, then the flags.
            // Read directly rather than marshalled — this runs ahead of every mouse event on the
            // machine, and PtrToStructure would allocate on each one.
            var injected = (Marshal.ReadInt32(data, 12) & LLMHF_INJECTED) != 0;

            if ((int)message == WM_MOUSEMOVE)
            {
                if (injected)
                {
                    Interlocked.Increment(ref _injectedMoves);
                }
                else
                {
                    Interlocked.Increment(ref _physicalMoves);
                }
            }
            else if (injected)
            {
                Interlocked.Increment(ref _injectedClicks);
            }
            else
            {
                Interlocked.Increment(ref _physicalClicks);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    /// <summary>
    /// Samples where the cursor is, and once a second says what the three readings disagreed about.
    /// </summary>
    /// <remarks>
    /// <b>The cursor count is sampled, the other two are counted.</b> Movement is read once per beat,
    /// so several events inside the same 20 ms become one step: 40 injected moves measured 29 steps
    /// when this was verified. That gap is the sampler, not lost input, and the two numbers are not
    /// meant to match. Only the difference between "some" and "none" is load-bearing here — which is
    /// why the fault below is <c>steps == 0</c> and not a ratio.
    /// </remarks>
    private static void WatchPointer()
    {
        if (GetCursorPos(out var now) && (now.X != _cursorWas.X || now.Y != _cursorWas.Y))
        {
            _cursorWas = (now.X, now.Y);
            _cursorSteps++;
        }

        if (DateTime.Now - _pointerReportedAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _pointerReportedAt = DateTime.Now;

        var injectedMoves = Interlocked.Exchange(ref _injectedMoves, 0);
        var physicalMoves = Interlocked.Exchange(ref _physicalMoves, 0);
        var injectedClicks = Interlocked.Exchange(ref _injectedClicks, 0);
        var physicalClicks = Interlocked.Exchange(ref _physicalClicks, 0);
        var steps = _cursorSteps;
        _cursorSteps = 0;

        // Silence when nothing happened, or the log is one line of zeroes per second all night.
        if ((injectedMoves | physicalMoves | injectedClicks | physicalClicks | steps) == 0)
        {
            return;
        }

        Quiet($"pointeur  mouvements inj/phys={injectedMoves}/{physicalMoves} "
              + $"curseur={steps} clics inj/phys={injectedClicks}/{physicalClicks}");

        if (steps == 0 && injectedMoves + physicalMoves >= EnoughToExpectMovement)
        {
            Loud($"CURSEUR SOURD : {injectedMoves} mouvement(s) injecté(s) et {physicalMoves} physique(s) "
                 + "en une seconde, et le curseur n'a pas bougé d'un pixel.");
            Incident();
        }
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int LLMHF_INJECTED = 0x00000001;

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Where;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hook, HookProc callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Message message, IntPtr window, uint first, uint last);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

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

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle, IntPtr apres, int x, int y, int largeur, int hauteur, uint drapeaux);

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
