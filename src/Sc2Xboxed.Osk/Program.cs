using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.Osk;

public static class Program
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_LSHIFT = 0xA0;

    private static StreamWriter? _logFile;
    internal static OskSettings Settings = OskSettings.Load();

    internal static void Log(string msg)
    {
        var line = $"[{DateTimeOffset.UtcNow:HH:mm:ss.fff}] {msg}";
        try { _logFile?.WriteLine(line); _logFile?.Flush(); } catch { }
    }

    [STAThread]
    /// <summary>
    /// Closes the overlay once the bridge that owns it is gone.
    /// </summary>
    /// <remarks>
    /// The overlay is started by the bridge and outlives it. Until now nothing told it the bridge
    /// had died, so a core that was killed or crashed left a topmost window on screen with nothing
    /// feeding it, and the user had to end the task by hand.
    ///
    /// <para>
    /// It watches for itself rather than being told. Asking the bridge to announce its own death
    /// only covers the deaths it survives long enough to announce — which excludes exactly the ones
    /// that strand the overlay. And it deliberately knows nothing of the configuration window: that
    /// window comes and goes constantly, and tying the overlay's life to it would close the keyboard
    /// every time somebody shut the settings.
    /// </para>
    ///
    /// <para>
    /// The grace period matters. The overlay is pre-warmed, so it is often running before the bridge
    /// has finished starting; without it the overlay would shut itself down a second after launch,
    /// every time.
    /// </para>
    /// </remarks>
    private static void StartOrphanWatcher(CancellationTokenSource cts)
    {
        const string CoreProcess = "SteamXBox.Core";

        var watcher = new Thread(() =>
        {
            var seenCore = false;

            // Two minutes: long enough for the slowest start, short enough that an overlay left
            // behind by a crash does not sit there for the rest of the session.
            var graceUntil = DateTime.UtcNow.AddMinutes(2);

            while (!cts.IsCancellationRequested)
            {
                Thread.Sleep(2000);

                bool running;
                try
                {
                    running = System.Diagnostics.Process.GetProcessesByName(CoreProcess).Length > 0;
                }
                catch
                {
                    // Enumeration can be refused; assume it is still there rather than close on a
                    // permission error.
                    continue;
                }

                if (running)
                {
                    seenCore = true;
                    continue;
                }

                // Only after the bridge has been seen at least once, or once the grace period is
                // over. Closing before it ever appeared would kill the pre-warmed instance the
                // bridge is about to use.
                if (!seenCore && DateTime.UtcNow < graceUntil)
                {
                    continue;
                }

                Log("Core is gone; the overlay closes rather than staying on screen alone.");
                cts.Cancel();

                try
                {
                    Application.Exit();
                }
                catch
                {
                    // The message loop may already be gone; the process ends either way.
                }

                return;
            }
        })
        {
            IsBackground = true,
            Name = "OskOrphanWatcher",
        };

        watcher.Start();
        Log("Orphan watcher started.");
    }

    /// <summary>Last stick-and-key line written, so an unchanging state is not repeated.</summary>
    private static string _lastResolved = "";

    /// <summary>How many distinct stick states have been seen since the pipe opened.</summary>
    /// <remarks>
    /// Reported when the loop ends. Zero with the pipe connected means the frames never arrive —
    /// a different fault from frames arriving flat, and the two are indistinguishable from the
    /// screen.
    /// </remarks>
    private static int _framesSeen;

    /// <summary>The instance suffix passed as <c>--instance &lt;suffix&gt;</c>, or empty.</summary>
    /// <remarks>
    /// The suffix rather than the controller key: this process has no business knowing which pad it
    /// serves, only which channels to open — and a device identifier on a command line is visible to
    /// every process on the machine.
    /// </remarks>
    /// <summary>
    /// The input style this build serves, when its file name says so.
    /// </summary>
    /// <remarks>
    /// Two executables are published from this one project: <c>Sc2XboxedPads.Osk.exe</c> for a Steam
    /// Controller, which types on its trackpads, and <c>Sc2XboxedSticks.Osk.exe</c> for a pad that
    /// only has joysticks. One source, because two copies of a keyboard would drift apart within a
    /// week; two names, because a window that decides its input style at runtime has to keep both
    /// paths live, and a resting thumb on one then fights the other hand.
    ///
    /// Null when the name says nothing — the plain <c>Sc2Xboxed.Osk.exe</c> — and the style then
    /// comes from the frame, as before. That keeps the single-controller case working untouched.
    /// </remarks>
    private static OskControllerKind? KindFromExecutableName()
    {
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "";

        if (name.Contains("Pads", StringComparison.OrdinalIgnoreCase))
        {
            return OskControllerKind.Steam;
        }

        return name.Contains("Sticks", StringComparison.OrdinalIgnoreCase)
            ? OskControllerKind.Sticks
            : null;
    }

    /// <summary>Set once at startup; overrides the kind carried on each frame.</summary>
    private static OskControllerKind? _forcedKind;

    /// <summary>The kind this build serves, falling back to what the frame reports.</summary>
    private static OskControllerKind EffectiveKind(OskControllerKind fromFrame) => _forcedKind ?? fromFrame;

    private static string ReadInstanceSuffix()
    {
        var argv = Environment.GetCommandLineArgs();

        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i].Equals("--instance", StringComparison.OrdinalIgnoreCase))
            {
                return argv[i + 1];
            }
        }

        return "";
    }

    public static void Main(string[] args)
    {
        // One log per executable: two specialised keyboards writing to one file would truncate
        // each other on startup, and the survivor's log would look like the only one that ran.
        var stem = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "osk";
        var logPath = Path.Combine(AppContext.BaseDirectory, $"steamxbox-{stem}-debug.log");
        _logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };

        // Before the form exists, so the first paint is already skinned.
        OverlayPalette.Load(AppContext.BaseDirectory, Settings.Theme);

        _forcedKind = KindFromExecutableName();
        Log($"OSK overlay starting. BaseDir={AppContext.BaseDirectory} kind={(_forcedKind?.ToString() ?? "(from frame)")} "
            + $"elevated={Environment.IsPrivilegedProcess}");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Diagnostic: report what the caret detection actually sees, for the window in front.
        // Added because the placement was corrected twice on the assumption that a caret was being
        // found, without that assumption ever being checked.
        if (args.Contains("--probe-caret", StringComparer.OrdinalIgnoreCase))
        {
            ProbeCaret();
            return;
        }

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex.GetType().Name}: {ex.Message}");
            Log($"Stack: {ex.StackTrace}");
            if (ex.InnerException is { } ie)
                Log($"Inner: {ie.GetType().Name}: {ie.Message}");
        }
        finally
        {
            _logFile?.Dispose();
        }
    }

    /// <summary>
    /// Prints what the caret detection finds for the foreground window, once a second.
    /// </summary>
    /// <remarks>
    /// Writes to the log rather than the console: this is a WinExe and has no console attached.
    /// Focus the application under test, wait a few seconds, then read steamxbox-osk-debug.log.
    /// </remarks>
    private static void ProbeCaret()
    {
        Log("=== caret probe ===");

        for (var i = 0; i < 12; i++)
        {
            Thread.Sleep(1000);

            try
            {
                var win32 = CaretLocator.FindActiveField();
                var uia = UiAutomationCaret.Find();
                var detailed = CaretLocator.FindActiveFieldDetailed();
                var work = detailed.Rect.IsEmpty
                    ? CaretLocator.ForegroundWorkArea()
                    : CaretLocator.WorkAreaFor(detailed.Rect);

                var placement = Sc2Xboxed.Core.Osk.OverlayPlacement.Place(
                    1012, 345, detailed.Rect, detailed.IsCaret, work);

                Log($"[{i}] win32={Describe(win32)} uia={Describe(uia)} " +
                    $"chosen={Describe(detailed.Rect)} isCaret={detailed.IsCaret} " +
                    $"work={Describe(work)} -> {placement.Kind} {Describe(placement.Bounds)}");
            }
            catch (Exception exception)
            {
                Log($"[{i}] probe failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Log("=== probe done ===");
    }

    private static string Describe(Sc2Xboxed.Core.Osk.ScreenRect rect)
        => rect.IsEmpty ? "none" : $"({rect.X},{rect.Y} {rect.Width}x{rect.Height})";

    /// <summary>Scale read from the settings, applied to the form the next time it is shown.</summary>
    private static int _pendingScale = 100;

    /// <summary>Floating or pinned, read from the settings on every show.</summary>
    private static bool _pendingFloating = true;

    /// <summary>
    /// Applies the stored layout name and size. An unrecognised layout falls back to detection
    /// rather than leaving the overlay with no keys at all.
    /// </summary>
    private static void ApplyKeyboardLayout(OskSettings settings)
    {
        _pendingScale = settings.ClampedKeyboardScale;
        _pendingFloating = settings.FloatingKeyboard;

        // La palette suit le theme choisi dans le GUI, relue a chaque affichage.
        OverlayPalette.Load(AppContext.BaseDirectory, settings.Theme);

        KeyboardLayout.Selected =
            Enum.TryParse<OskKeyboardLayout>(settings.KeyboardLayout, ignoreCase: true, out var layout)
                ? layout
                : OskKeyboardLayout.Auto;

        Log($"Keyboard layout: {KeyboardLayout.Selected} (setting was '{settings.KeyboardLayout}'), scale {_pendingScale}%, floating={_pendingFloating}");
    }

    private static void Run()
    {
        ApplyKeyboardLayout(Settings);

        var form = new OverlayForm { ScalePercent = _pendingScale, Floating = _pendingFloating, Log = Log };
        form.SetTypingMode(Settings.TypingMode == OskTypingMode.Daisywheel);
        Log($"Overlay form created. TypingMode={Settings.TypingMode}");

        var cts = new CancellationTokenSource();
        form.FormClosing += (_, _) => cts.Cancel();

        // Prewarm: the process starts and stays resident with the window hidden, so toggling the
        // overlay costs a signal file instead of a cold start. Measured on this build, the .NET host
        // needs about four seconds before the first line of this program runs — the overlay itself
        // takes 94 ms. Paying that once, when the runtime starts, is the whole point.
        var prewarm = Environment.GetCommandLineArgs()
            .Contains("--prewarm", StringComparer.OrdinalIgnoreCase);

        // Every channel this keyboard uses is derived from one suffix, handed over on the command
        // line. Without it the historical names are used, so a keyboard started on its own — the
        // pre-warmed one, or a single-controller session — behaves exactly as before.
        var naming = Sc2Xboxed.Core.Osk.OskInstanceNaming.FromSuffix(ReadInstanceSuffix());
        Log($"Instance suffix '{naming.Suffix}' -> pipe {naming.PadPipeName}");

        var closeSignalPath = Path.Combine(AppContext.BaseDirectory, naming.CloseSignalFile);
        var showSignalPath = Path.Combine(AppContext.BaseDirectory, naming.ShowSignalFile);
        var exitSignalPath = Path.Combine(AppContext.BaseDirectory, naming.ExitSignalFile);
        // Close and exit signals left by a previous run are stale and must go. A show signal is not:
        // the resident overlay takes several seconds to start, and a toggle pressed during that
        // window writes its signal before the watcher exists. Deleting it here swallowed the very
        // first press, which is exactly why the overlay seemed hard to open at the beginning.
        foreach (var stale in new[] { closeSignalPath, exitSignalPath })
        {
            try { if (File.Exists(stale)) File.Delete(stale); } catch { }
        }

        if (File.Exists(showSignalPath))
        {
            Log("A show signal was already waiting; honouring it.");
        }
        Log($"Signal paths under {AppContext.BaseDirectory} (prewarm={prewarm})");

        // Invoke needs a window handle, and a form that is never shown has none. Force it here, on
        // the UI thread, before anything can post to it.
        _ = form.Handle;

        var closeWatcher = new Thread(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(exitSignalPath))
                    {
                        File.Delete(exitSignalPath);
                        Log("Exit signal detected, shutting down overlay.");
                        try
                        {
                            form.Invoke(() =>
                            {
                                form.Close();
                                // A bare message loop has no main form to end it: it runs until the
                                // thread is told to stop.
                                Application.ExitThread();
                            });
                        }
                        catch { try { form.Close(); } catch { } }
                        return;
                    }

                    if (File.Exists(showSignalPath))
                    {
                        File.Delete(showSignalPath);
                        Log("Show signal detected.");
                        // Re-read the mode: the user may have changed it in the GUI while resident.
                        var reloaded = OskSettings.Load();
                        ApplyKeyboardLayout(reloaded);
                        var mode = reloaded.TypingMode;
                        try
                        {
                            form.Invoke(() =>
                            {
                                form.SetTypingMode(mode == OskTypingMode.Daisywheel);
                                // Before showing: the field moves between one use and the next, so
                                // the board is placed against the caret each time rather than once.
                                form.ScalePercent = _pendingScale;
                                form.Floating = _pendingFloating;
                                form.UpdatePlacement();
                                form.Show();
                                form.BringToFront();
                                // The overlay is layered and never activates, so no external probe
                                // can tell whether it is on screen. Report it ourselves.
                                Log($"Overlay shown. Visible={form.Visible} mode={mode} " +
                                    $"placement={form.LastPlacement} at ({form.BoardX:F0},{form.BoardY:F0})");
                            });
                        }
                        catch { }
                    }

                    if (File.Exists(closeSignalPath))
                    {
                        File.Delete(closeSignalPath);

                        if (prewarm)
                        {
                            // Hide, do not exit: staying resident is what makes the next toggle instant.
                            try
                            {
                                form.Invoke(() =>
                                {
                                    form.HideAll();
                                    form.Hide();
                                    Log($"Overlay hidden (resident). Visible={form.Visible}");
                                });
                            }
                            catch { }
                        }
                        else
                        {
                            Log("Close signal file detected, shutting down overlay.");
                            try { form.Invoke(form.Close); } catch { try { form.Close(); } catch { } }
                            return;
                        }
                    }
                }
                catch { }
                Thread.Sleep(prewarm ? 60 : 300);
            }
        })
        {
            IsBackground = true,
            Name = "OskCloseWatcher"
        };
        closeWatcher.Start();
        Log("Signal watcher started.");

        StartOrphanWatcher(cts);

        var reader = new PadInputReader(naming.PadPipeName);
        var haptics = new HapticFeedback(Log, naming.HapticPipeName);
        Log("Reader + haptics created, starting pipe loop...");

        var state = new LoopState(reader, haptics, form, cts);

        var bgThread = new Thread(() => RunPipeLoop(state))
        {
            IsBackground = true,
            Name = "PadPipeLoop"
        };
        bgThread.Start();

        if (prewarm)
        {
            // A bare message loop. Application.Run(Form) shows the form, and so does
            // Application.Run(ApplicationContext) — it sets MainForm.Visible before pumping. Either
            // one puts the keyboard on screen at startup with the runtime believing it is closed,
            // which is exactly the state where the toggle button appears to do nothing.
            Log("Overlay resident, hidden, waiting for a show signal.");
            Application.Run();
        }
        else
        {
            form.Show();
            Log("Overlay form shown.");
            Application.Run(form);
        }

        Log("Application.Run exited.");

        // Belt and braces: the overlay no longer holds the physical Shift key at all, but releasing it
        // on the way out costs nothing and guarantees no build can ever leave the keyboard stuck.
        InputHelper.KeyUp(VK_LSHIFT);
        state.Shift = ShiftMode.Off;

        cts.Cancel();
        reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        haptics.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bgThread.Join(3000);
        Log("Overlay stopped.");
    }

    private sealed class LoopState(
        PadInputReader reader,
        HapticFeedback haptics,
        OverlayForm form,
        CancellationTokenSource cts)
    {
        public PadInputReader Reader = reader;
        public HapticFeedback Haptics = haptics;
        public OverlayForm Form = form;
        public CancellationTokenSource Cts = cts;
        public bool WasConnected;
        public ShiftMode Shift;
        public bool SymActive;
        public bool PrevRightPressed;
        public bool PrevLeftPressed;
        public double SmoothRightX, SmoothRightY, SmoothLeftX, SmoothLeftY;
        public bool HasSmoothRight, HasSmoothLeft;
        public KeyDef? PendingRightKey, PendingLeftKey;
        public bool RightKeyPending, LeftKeyPending;

        /// <summary>Last key highlighted per pad, used to tick only on a real change.</summary>
        public KeyDef? PrevRightKey, PrevLeftKey;

        /// <summary>Key held under each pad for the duration of a press.</summary>
        public KeyDef? LatchedRight, LatchedLeft;
        public TimeSpan? RightPressAt, LeftPressAt;

        /// <summary>
        /// Last resolved column and row per stick, feeding the selection hysteresis: a stick holds
        /// its key until it has clearly crossed into a neighbour, instead of flickering at the edge.
        /// </summary>
        public int? PrevRightCol, PrevRightRow, PrevLeftCol, PrevLeftRow;

        /// <summary>
        /// Frames since each pad last reported a touch, capped at the hold budget. A pad stays in
        /// charge while it is touched and for a few frames after, so a phantom blip cannot flicker
        /// its key against the stick's key at frame rate.
        /// </summary>
        public int RightTouchHold, LeftTouchHold;

        /// <summary>
        /// Key and cursor spot each pad selected, kept for the duration of the debounce hold so a
        /// just-lifted finger does not snap the highlight to the centre of a released pad.
        /// </summary>
        public KeyDef? RightHoldKey, LeftHoldKey;

        /// <summary>When each pad last pulsed a hover tick, feeding the hover rate limit.</summary>
        public DateTimeOffset RightHoverAt, LeftHoverAt;

        /// <summary>
        /// Freezes the selected key while a pad is pressed. Pressing a touchpad always shifts the
        /// finger a little, and that shift used to move the selection onto a neighbouring key between
        /// the press and the moment the character was emitted.
        /// </summary>
        public KeyDef? LatchRight(KeyDef? current, bool pressed, TimeSpan now)
        {
            if (!pressed)
            {
                RightPressAt = null;
                LatchedRight = null;
                return current;
            }

            if (RightPressAt is null)
            {
                RightPressAt = now;
                LatchedRight = current;
            }

            return LatchedRight ?? current;
        }

        public KeyDef? LatchLeft(KeyDef? current, bool pressed, TimeSpan now)
        {
            if (!pressed)
            {
                LeftPressAt = null;
                LatchedLeft = null;
                return current;
            }

            if (LeftPressAt is null)
            {
                LeftPressAt = now;
                LatchedLeft = current;
            }

            return LatchedLeft ?? current;
        }

        // ---- Daisywheel state ----
        public int? ActivePetal;
        public readonly bool[] PrevSlotDown = new bool[DaisywheelLayout.SlotsPerPetal];
        public bool DaisywheelPrimed;

        private readonly CursorFilter _rightFilter = new(Settings.CursorSmoothing);
        private readonly CursorFilter _leftFilter = new(Settings.CursorSmoothing);

        public void EaseRight(double rawX, double rawY)
        {
            if (!HasSmoothRight) { _rightFilter.Reset(); HasSmoothRight = true; }
            _rightFilter.Update(rawX, rawY);
            SmoothRightX = _rightFilter.X;
            SmoothRightY = _rightFilter.Y;
        }

        public void EaseLeft(double rawX, double rawY)
        {
            if (!HasSmoothLeft) { _leftFilter.Reset(); HasSmoothLeft = true; }
            _leftFilter.Update(rawX, rawY);
            SmoothLeftX = _leftFilter.X;
            SmoothLeftY = _leftFilter.Y;
        }
    }

    private static async void RunPipeLoop(LoopState s)
    {
        try
        {
            await foreach (var padFrame in s.Reader.ReadFramesAsync(s.Cts.Token))
            {
                var frame = padFrame.State;
                if (!s.WasConnected)
                {
                    s.WasConnected = true;
                    Log("OSK: pipe connected, keyboard active.");
                }

                if (Settings.TypingMode == OskTypingMode.Daisywheel)
                {
                    try { HandleDaisywheelFrame(s, frame, EffectiveKind(padFrame.Kind) == OskControllerKind.Sticks); }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                    continue;
                }

                double kw = s.Form.KeyW, kh = s.Form.KeyH;

                // One keyboard per kind of controller. A Steam Controller types on its pads, a
                // PS5 or Xbox pad on its sticks. Keeping both paths live on the same window was
                // the conflict this split exists to remove: a hand resting on a stick re-anchored
                // the selection under a pad typist, and a phantom pad touch fought a stick typist's
                // keys. Only the kind's own path is read, so neither can disturb the other.
                bool padDriven = EffectiveKind(padFrame.Kind) == OskControllerKind.Steam;

                // A pad overrides the stick from the moment it is touched: the thumb that reaches
                // for the pad is the one typing. The touch is held for a few frames after it drops,
                // so a phantom blip cannot flicker the pad's key — and while it is held, the key
                // stays where the finger left it.
                bool rightTouched = padDriven && (frame.RightPad.IsTouched || frame.RightPad.IsPressed);
                bool leftTouched = padDriven && (frame.LeftPad.IsTouched || frame.LeftPad.IsPressed);
                bool rightActive = padDriven && PadActive(frame.RightPad, ref s.RightTouchHold);
                bool leftActive = padDriven && PadActive(frame.LeftPad, ref s.LeftTouchHold);

                KeyDef? rightKey = null;
                KeyDef? leftKey = null;

                if (padDriven)
                {
                    if (rightTouched)
                    {
                        double py = (frame.RightPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                        rightKey = KeyboardLayout.FindKeyAt(row, KeyboardLayout.ColumnFor(frame.RightPad.X, isLeftPad: false, row));
                        s.RightHoldKey = rightKey;
                    }
                    else if (s.RightTouchHold > 0)
                    {
                        rightKey = s.RightHoldKey;
                    }

                    if (leftTouched)
                    {
                        double py = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                        leftKey = KeyboardLayout.FindKeyAt(row, KeyboardLayout.ColumnFor(frame.LeftPad.X, isLeftPad: true, row));
                        s.LeftHoldKey = leftKey;
                    }
                    else if (s.LeftTouchHold > 0)
                    {
                        leftKey = s.LeftHoldKey;
                    }
                }
                else
                {
                    // Sticks aim at the same keyboard, each from its own half. The pad paths above
                    // stay dead for this kind, so a Steam pad can never type into a PS5/Xbox board.
                    rightKey = StickKey(frame.RightStick, leftStick: false, ref s.PrevRightCol, ref s.PrevRightRow);
                    leftKey = StickKey(frame.LeftStick, leftStick: true, ref s.PrevLeftCol, ref s.PrevLeftRow);
                }

                // Sticks in, keys out, once per change. "The overlay does not respond to the
                // controller" covers four different faults that need opposite fixes: no frame
                // arrives at all, frames arrive with the sticks flat, the sticks move but resolve to
                // no key, or a key is resolved and never committed. The counters in the bridge
                // narrowed this to the overlay; only a line here can say which of the four it is.
                var resolved = $"{frame.LeftStick.X:F2},{frame.LeftStick.Y:F2}"
                             + $"|{frame.RightStick.X:F2},{frame.RightStick.Y:F2}"
                             + $"|{leftKey?.Label ?? "-"}|{rightKey?.Label ?? "-"}";

                if (resolved != _lastResolved)
                {
                    _lastResolved = resolved;
                    _framesSeen++;

                    Log($"stick L=({frame.LeftStick.X:F2},{frame.LeftStick.Y:F2}) "
                        + $"R=({frame.RightStick.X:F2},{frame.RightStick.Y:F2}) "
                        + $"=> left={leftKey?.Label ?? "(aucune)"} right={rightKey?.Label ?? "(aucune)"}");
                }

                // While a pad is pressed the key under it is latched, so the small shift that pressing
                // always causes cannot land the keystroke on a neighbour.
                rightKey = s.LatchRight(rightKey, frame.RightPad.IsPressed, frame.Timestamp);
                leftKey = s.LatchLeft(leftKey, frame.LeftPad.IsPressed, frame.Timestamp);

                // Tick on every key boundary crossed, not just on keypress: this is what makes
                // typing possible without watching the overlay. Rate-limited so an input that wavers
                // between two keys buzzes once instead of at frame rate.
                if (!ReferenceEquals(rightKey, s.PrevRightKey))
                {
                    if (rightKey is not null && CanHover(ref s.RightHoverAt)) s.Haptics.Hover(HapticActuator.RightTrackpad);
                    s.PrevRightKey = rightKey;
                }
                if (!ReferenceEquals(leftKey, s.PrevLeftKey))
                {
                    if (leftKey is not null && CanHover(ref s.LeftHoverAt)) s.Haptics.Hover(HapticActuator.LeftTrackpad);
                    s.PrevLeftKey = leftKey;
                }

                try
                {
                    s.Form.SetModifierState(s.Shift, s.SymActive);
                    double boardY = s.Form.BoardY;

                    if (rightTouched)
                    {
                        double rawRy = (frame.RightPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        int rightRow = Math.Clamp((int)(rawRy / kh), 0, KeyboardLayout.Rows - 1);
                        double rawRx = KeyboardLayout.CursorXFor(frame.RightPad.X, isLeftPad: false, rightRow, kw);
                        s.EaseRight(rawRx, rawRy);
                        s.Form.SetRightCursor(s.Form.BoardX + s.SmoothRightX, boardY + s.SmoothRightY);
                        s.Form.HighlightKey(rightKey);
                    }
                    else if (rightActive)
                    {
                        // Debouncing a just-lifted finger: hold the cursor and the key where they are
                        // rather than snapping to the centre of a released pad.
                        s.Form.SetRightCursor(s.Form.BoardX + s.SmoothRightX, boardY + s.SmoothRightY);
                        s.Form.HighlightKey(rightKey);
                    }
                    else if (rightKey is not null)
                    {
                        s.HasSmoothRight = false;
                        s.Form.SetRightCursor(
                            s.Form.BoardX + (rightKey.Col + rightKey.Width * 0.5) * kw,
                            boardY + (rightKey.Row + 0.5) * kh);
                        s.Form.HighlightKey(rightKey);
                    }
                    else
                    {
                        s.HasSmoothRight = false;
                        s.Form.HideRightCursor();
                        s.Form.HighlightKey(null);
                    }

                    if (leftTouched)
                    {
                        double rawLy = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        int leftRow = Math.Clamp((int)(rawLy / kh), 0, KeyboardLayout.Rows - 1);
                        double rawLx = KeyboardLayout.CursorXFor(frame.LeftPad.X, isLeftPad: true, leftRow, kw);
                        s.EaseLeft(rawLx, rawLy);
                        s.Form.SetLeftCursor(s.Form.BoardX + s.SmoothLeftX, boardY + s.SmoothLeftY);
                        s.Form.HighlightLeftKey(leftKey);
                    }
                    else if (leftActive)
                    {
                        // Debouncing a just-lifted finger: hold the cursor and the key where they are
                        // rather than snapping to the centre of a released pad.
                        s.Form.SetLeftCursor(s.Form.BoardX + s.SmoothLeftX, boardY + s.SmoothLeftY);
                        s.Form.HighlightLeftKey(leftKey);
                    }
                    else if (leftKey is not null)
                    {
                        s.HasSmoothLeft = false;
                        s.Form.SetLeftCursor(
                            s.Form.BoardX + (leftKey.Col + leftKey.Width * 0.5) * kw,
                            boardY + (leftKey.Row + 0.5) * kh);
                        s.Form.HighlightLeftKey(leftKey);
                    }
                    else
                    {
                        s.HasSmoothLeft = false;
                        s.Form.HideLeftCursor();
                        s.Form.HighlightLeftKey(null);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                // A trigger commits the key its own half has selected, exactly as a pad click does.
                // Or-ed with the pad rather than replacing it: a Steam Controller has both, and
                // whichever the hand reaches for should work without a mode to choose first.
                //
                // Half travel, not full: a trigger has a long throw, and waiting for the bottom
                // would make committing feel heavier than clicking a pad.
                const double TriggerCommitPoint = 0.5;

                bool rightPressed = frame.RightPad.IsPressed || frame.RightTrigger >= TriggerCommitPoint;
                bool leftPressed = frame.LeftPad.IsPressed || frame.LeftTrigger >= TriggerCommitPoint;

                if (Settings.ValidateOnRelease)
                {
                    // Arm on click, commit on release, so the finger can be repositioned while
                    // held down without typing the wrong key.
                    if (rightPressed && !s.PrevRightPressed && rightKey is not null)
                    {
                        s.PendingRightKey = rightKey;
                        s.RightKeyPending = true;
                    }
                    if (!rightPressed && s.RightKeyPending)
                    {
                        if (s.PendingRightKey is not null)
                        {
                            Emit(s, s.PendingRightKey, HapticActuator.RightTrackpad);
                        }
                        s.RightKeyPending = false;
                    }

                    if (leftPressed && !s.PrevLeftPressed && leftKey is not null)
                    {
                        s.PendingLeftKey = leftKey;
                        s.LeftKeyPending = true;
                    }
                    if (!leftPressed && s.LeftKeyPending)
                    {
                        if (s.PendingLeftKey is not null)
                        {
                            Emit(s, s.PendingLeftKey, HapticActuator.LeftTrackpad);
                        }
                        s.LeftKeyPending = false;
                    }
                }
                else
                {
                    if (rightPressed && !s.PrevRightPressed && rightKey is not null)
                    {
                        Emit(s, rightKey, HapticActuator.RightTrackpad);
                    }

                    if (leftPressed && !s.PrevLeftPressed && leftKey is not null)
                    {
                        Emit(s, leftKey, HapticActuator.LeftTrackpad);
                    }
                }

                s.PrevRightPressed = rightPressed;
                s.PrevLeftPressed = leftPressed;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"OSK pipe error: {ex.GetType().Name}: {ex.Message}");
        }

        Log($"Pipe loop ended. {_framesSeen} distinct stick state(s) seen.");
    }

    /// <summary>Buttons that pick a slot, in the same order as the petal's four slots.</summary>
    private static readonly SteamControllerButtons[] SlotButtons =
    [
        SteamControllerButtons.A,
        SteamControllerButtons.B,
        SteamControllerButtons.X,
        SteamControllerButtons.Y,
    ];

    /// <summary>
    /// Daisywheel frame: the left pad direction selects the petal, ABXY selects the slot, and the
    /// left pad click toggles shift. On a controller with no pads the left stick points at the
    /// petal and its click toggles shift. The overlay is closed from the core, on the Menu button.
    /// </summary>
    private static void HandleDaisywheelFrame(LoopState s, ControllerState frame, bool stickDriven)
    {
        // The button that opened the overlay is usually still held on the first frame. Latch the
        // starting state so its release is not read as a keypress.
        if (!s.DaisywheelPrimed)
        {
            s.DaisywheelPrimed = true;
            for (int slot = 0; slot < SlotButtons.Length; slot++)
            {
                s.PrevSlotDown[slot] = frame.Buttons.HasFlag(SlotButtons[slot]);
            }
            s.PrevLeftPressed = stickDriven
                ? frame.Buttons.HasFlag(SteamControllerButtons.LeftStick)
                : frame.LeftPad.IsPressed;
            return;
        }

        // The left stick points at a petal the way the left pad does on a Steam Controller.
        int? petal = stickDriven
            ? DaisywheelLayout.PetalFromPad(frame.LeftStick.X, frame.LeftStick.Y)
            : (frame.LeftPad.IsTouched || frame.LeftPad.IsPressed)
                ? DaisywheelLayout.PetalFromPad(frame.LeftPad.X, frame.LeftPad.Y)
                : null;

        if (petal != s.ActivePetal)
        {
            s.ActivePetal = petal;
            s.Form.SetActivePetal(petal);
            if (petal is not null)
            {
                s.Haptics.Hover(HapticActuator.LeftTrackpad);
            }
        }

        // Pad click is free in this mode, so it carries shift; on a stick keyboard the stick
        // click does.
        bool leftPressed = stickDriven
            ? frame.Buttons.HasFlag(SteamControllerButtons.LeftStick)
            : frame.LeftPad.IsPressed;
        if (leftPressed && !s.PrevLeftPressed)
        {
            s.Shift = s.Shift switch
            {
                ShiftMode.Off => ShiftMode.OneShot,
                ShiftMode.OneShot => ShiftMode.Locked,
                _ => ShiftMode.Off,
            };

            s.Form.SetModifierState(s.Shift, s.SymActive);
            s.Haptics.Press(HapticActuator.LeftTrackpad);
        }
        s.PrevLeftPressed = leftPressed;

        for (int slot = 0; slot < SlotButtons.Length; slot++)
        {
            bool down = frame.Buttons.HasFlag(SlotButtons[slot]);
            bool rising = down && !s.PrevSlotDown[slot];
            s.PrevSlotDown[slot] = down;

            if (!rising || s.ActivePetal is not { } activePetal)
            {
                continue;
            }

            var key = DaisywheelLayout.Slot(activePetal, slot, s.SymActive);
            if (key is null)
            {
                continue;
            }

            SendKey(key, ref s.Shift, ref s.SymActive);
            s.Form.FlashSlot(key);
            s.Form.SetModifierState(s.Shift, s.SymActive);
            s.Haptics.Press(HapticActuator.RightTrackpad);
        }
    }

    /// <summary>Sends a key, flashes it in the overlay and fires the keypress haptic.</summary>
    private static void Emit(LoopState s, KeyDef key, HapticActuator actuator)
    {
        SendKey(key, ref s.Shift, ref s.SymActive);

        // Diagnostic: which window the committed key was aimed at. If a user reports that the
        // keyboard "does not type", this says whether the keystroke reached the foreground at all
        // and which window was in front when it was sent — an elevated game drops SendInput from a
        // non-elevated overlay silently, and only the foreground title makes that visible.
        try { Log($"OSK emit '{key.Label}' -> fg window '{ForegroundWindowTitle()}'"); } catch { }

        try { s.Form.FlashKey(key); } catch { }
        s.Haptics.Press(actuator);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private static string ForegroundWindowTitle()
    {
        var sb = new StringBuilder(512);
        try { GetWindowText(GetForegroundWindow(), sb, sb.Capacity); } catch { }
        return sb.ToString();
    }

    private static void SendKey(KeyDef key, ref ShiftMode shift, ref bool symActive)
    {
        switch (key.Action)
        {
            case SpecialAction.Shift:
                // Phone convention: one press capitalises the next character, a second locks capitals.
                shift = shift switch
                {
                    ShiftMode.Off => ShiftMode.OneShot,
                    ShiftMode.OneShot => ShiftMode.Locked,
                    _ => ShiftMode.Off,
                };
                break;
            case SpecialAction.Sym:
                symActive = !symActive;
                break;
            case SpecialAction.Backspace:
                InputHelper.KeyTap(VK_BACK);
                break;
            case SpecialAction.Enter:
                InputHelper.KeyTap(VK_RETURN);
                break;
            case SpecialAction.Tab:
                InputHelper.KeyTap(VK_TAB);
                break;
            case SpecialAction.Space:
                InputHelper.UnicodeChar(' ');
                break;
            default:
                char ch;
                if (symActive && key.SymChar != '\0')
                    ch = key.SymChar;
                else
                    ch = shift != ShiftMode.Off ? key.ShiftedChar : key.NormalChar;

                if (ch != '\0')
                    InputHelper.UnicodeChar(ch);

                // A one-shot capital is spent as soon as a character is produced. The shifted glyph is
                // sent directly, so the physical Shift key is never held and can never be left stuck.
                if (shift == ShiftMode.OneShot)
                    shift = ShiftMode.Off;
                break;
        }
    }

    /// <summary>
    /// The number of consecutive frames a pad stays in charge after its last reported touch. A
    /// Steam Controller's touch bit blips off for a frame now and then while the finger is still
    /// down; without the hold, each blip would drop the pad back to the stick's anchor and back,
    /// flickering the highlight at frame rate.
    /// </summary>
    private const int TouchHoldFrames = 3;

    /// <summary>
    /// Whether a pad is being used on this frame. Decided with the author: the pad takes over from
    /// the stick as soon as it is touched — no movement required — and stays in charge for a few
    /// frames after the touch drops, so a phantom blip cannot fight the stick's anchor.
    /// </summary>
    private static bool PadActive(TouchpadSample pad, ref int hold)
    {
        if (pad.IsTouched || pad.IsPressed)
        {
            hold = TouchHoldFrames;
            return true;
        }

        if (hold > 0)
        {
            hold--;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Minimum interval between hover ticks on one side. The hover fires on every key boundary the
    /// highlight crosses, so an input that wavers between two keys would otherwise buzz the pad at
    /// frame rate. The interval is far shorter than a deliberate move between keys, so real typing
    /// is never held back by it.
    /// </summary>
    private const double MinHoverIntervalMs = 50;

    private static bool CanHover(ref DateTimeOffset last)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - last).TotalMilliseconds < MinHoverIntervalMs)
        {
            return false;
        }

        last = now;
        return true;
    }

    /// <summary>
    /// The key a stick is pointing at. A resting stick points at its anchor.
    /// </summary>
    /// <remarks>
    /// A resting stick lights the key under its anchor, and holds it. The keyboard is driven without
    /// looking at it: a thumb that lets go and finds nothing lit has lost its place, and has to push
    /// again just to see where it is. The anchor is a home key in the sense a typist means it —
    /// somewhere the hand returns to and can commit from.
    ///
    /// The hysteresis still applies while pushing: a selection holds its key until the stick has
    /// clearly crossed into a neighbour, so every key catches as the stick leaves it. The margin is
    /// in key widths — 0.20 of a key past each boundary — so the catch feels the same in every
    /// direction however the band scales the push.
    /// </remarks>
    private static KeyDef? StickKey(
        NormalizedStick stick, bool leftStick, ref int? prevCol, ref int? prevRow)
    {
        const double RestRadius = 0.25;
        const double BoundaryMargin = 0.20;

        var anchors = StickAnchorLayout.Build(KeyboardLayout.WidestRow, KeyboardLayout.Rows);
        var index = Math.Min(StickAnchorLayout.AnchorFor(leftStick), anchors.Count - 1);
        var anchor = anchors[index];

        if (stick.X * stick.X + stick.Y * stick.Y < RestRadius * RestRadius)
        {
            // Back to the anchor, and lit. The hysteresis is dropped with it so the next push starts
            // from the anchor rather than from wherever the thumb happened to release.
            prevCol = anchor.Home.Column;
            prevRow = anchor.Home.Row;

            return KeyboardLayout.FindKeyAt(anchor.Home.Row, anchor.Home.Column);
        }

        var (rawColumn, rawRow) =
            StickAnchorLayout.ResolveRaw(anchor, stick.X, stick.Y, KeyboardLayout.Rows);

        var column = HystereticIndex(rawColumn, prevCol, anchor.FirstColumn, anchor.LastColumn, BoundaryMargin);
        var row = HystereticIndex(rawRow, prevRow, 0, KeyboardLayout.Rows - 1, BoundaryMargin);

        prevCol = column;
        prevRow = row;

        return KeyboardLayout.FindKeyAt(row, column);
    }

    /// <summary>
    /// Rounds an unrounded index with a hold band around each boundary: the selection keeps its
    /// index until the stick has crossed the boundary by the margin, then lands on the new index.
    /// </summary>
    private static int HystereticIndex(double raw, int? prev, int first, int last, double margin)
    {
        if (prev is { } previous)
        {
            if (raw >= previous + 0.5 + margin || raw <= previous - 0.5 - margin)
            {
                return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), first, last);
            }

            return Math.Clamp(previous, first, last);
        }

        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), first, last);
    }
}
