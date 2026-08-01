using System.IO;
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
    public static void Main(string[] args)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "steamxbox-osk-debug.log");
        _logFile = new StreamWriter(logPath, append: false) { AutoFlush = true };

        // Before the form exists, so the first paint is already skinned.
        OverlayPalette.Load(AppContext.BaseDirectory);

        Log($"OSK overlay starting. BaseDir={AppContext.BaseDirectory}");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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

    private static void Run()
    {
        var form = new OverlayForm();
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

        var closeSignalPath = Path.Combine(AppContext.BaseDirectory, "osk-close.signal");
        var showSignalPath = Path.Combine(AppContext.BaseDirectory, "osk-show.signal");
        var exitSignalPath = Path.Combine(AppContext.BaseDirectory, "osk-exit.signal");
        foreach (var stale in new[] { closeSignalPath, showSignalPath, exitSignalPath })
        {
            try { if (File.Exists(stale)) File.Delete(stale); } catch { }
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
                        var mode = OskSettings.Load().TypingMode;
                        try
                        {
                            form.Invoke(() =>
                            {
                                form.SetTypingMode(mode == OskTypingMode.Daisywheel);
                                form.Show();
                                form.BringToFront();
                                // The overlay is layered and never activates, so no external probe
                                // can tell whether it is on screen. Report it ourselves.
                                Log($"Overlay shown. Visible={form.Visible} mode={mode}");
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

        var reader = new PadInputReader();
        var haptics = new HapticFeedback(Log);
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
            await foreach (var frame in s.Reader.ReadFramesAsync(s.Cts.Token))
            {
                if (!s.WasConnected)
                {
                    s.WasConnected = true;
                    Log("OSK: pipe connected, keyboard active.");
                }

                if (Settings.TypingMode == OskTypingMode.Daisywheel)
                {
                    try { HandleDaisywheelFrame(s, frame); }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                    continue;
                }

                double kw = s.Form.KeyW, kh = s.Form.KeyH;

                bool rightTouched = frame.RightPad.IsTouched || frame.RightPad.IsPressed;
                bool leftTouched = frame.LeftPad.IsTouched || frame.LeftPad.IsPressed;

                KeyDef? rightKey = null;
                KeyDef? leftKey = null;

                if (rightTouched)
                {
                    double py = (frame.RightPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                    int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    rightKey = KeyboardLayout.FindKeyAt(row, KeyboardLayout.ColumnFor(frame.RightPad.X, isLeftPad: false, row));
                }

                if (leftTouched)
                {
                    double py = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                    int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    leftKey = KeyboardLayout.FindKeyAt(row, KeyboardLayout.ColumnFor(frame.LeftPad.X, isLeftPad: true, row));
                }

                // While a pad is pressed the key under it is latched, so the small shift that pressing
                // always causes cannot land the keystroke on a neighbour.
                rightKey = s.LatchRight(rightKey, frame.RightPad.IsPressed, frame.Timestamp);
                leftKey = s.LatchLeft(leftKey, frame.LeftPad.IsPressed, frame.Timestamp);

                // Tick on every key boundary crossed, not just on keypress: this is what makes
                // typing possible without watching the overlay.
                if (!ReferenceEquals(rightKey, s.PrevRightKey))
                {
                    if (rightKey is not null) s.Haptics.Hover(HapticActuator.RightTrackpad);
                    s.PrevRightKey = rightKey;
                }
                if (!ReferenceEquals(leftKey, s.PrevLeftKey))
                {
                    if (leftKey is not null) s.Haptics.Hover(HapticActuator.LeftTrackpad);
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
                        s.Form.SetRightCursor(s.SmoothRightX, boardY + s.SmoothRightY);
                        s.Form.HighlightKey(rightKey);
                    }
                    else
                    {
                        s.HasSmoothRight = false;
                        s.Form.HideRightCursor();
                    }

                    if (leftTouched)
                    {
                        double rawLy = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        int leftRow = Math.Clamp((int)(rawLy / kh), 0, KeyboardLayout.Rows - 1);
                        double rawLx = KeyboardLayout.CursorXFor(frame.LeftPad.X, isLeftPad: true, leftRow, kw);
                        s.EaseLeft(rawLx, rawLy);
                        s.Form.SetLeftCursor(s.SmoothLeftX, boardY + s.SmoothLeftY);
                        s.Form.HighlightLeftKey(leftKey);
                    }
                    else
                    {
                        s.HasSmoothLeft = false;
                        s.Form.HideLeftCursor();
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                bool rightPressed = frame.RightPad.IsPressed;
                bool leftPressed = frame.LeftPad.IsPressed;

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

        Log("Pipe loop ended.");
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
    /// left pad click toggles shift. The overlay is closed from the core, on the Menu button.
    /// </summary>
    private static void HandleDaisywheelFrame(LoopState s, SteamControllerState frame)
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
            s.PrevLeftPressed = frame.LeftPad.IsPressed;
            return;
        }

        bool leftTouched = frame.LeftPad.IsTouched || frame.LeftPad.IsPressed;
        int? petal = leftTouched
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

        // Pad click is free in this mode, so it carries shift.
        bool leftPressed = frame.LeftPad.IsPressed;
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
        try { s.Form.FlashKey(key); } catch { }
        s.Haptics.Press(actuator);
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
}
