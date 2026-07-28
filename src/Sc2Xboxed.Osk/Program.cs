using System.IO;
using System.Threading;
using System.Windows.Forms;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Osk;

public static class Program
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_LSHIFT = 0xA0;

    private static StreamWriter? _logFile;

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
        Log("Overlay form created.");

        var cts = new CancellationTokenSource();
        form.FormClosing += (_, _) => cts.Cancel();

        EventWaitHandle? closeEvent = null;
        try
        {
            closeEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "SteamXBox_OskClose");
            Log("Close event handle created.");

            var closeWatcher = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (closeEvent.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        Log("Close event signaled, shutting down overlay.");
                        try { form.Invoke(form.Close); }
                        catch { try { form.Close(); } catch { } }
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "OskCloseWatcher"
            };
            closeWatcher.Start();
        }
        catch (Exception ex)
        {
            Log($"Failed to create close event handle: {ex.Message}");
        }

        var reader = new PadInputReader();
        var haptics = new HapticFeedback();
        Log("Reader + haptics created, starting pipe loop...");

        var state = new LoopState(reader, haptics, form, cts);

        var bgThread = new Thread(() => RunPipeLoop(state))
        {
            IsBackground = true,
            Name = "PadPipeLoop"
        };
        bgThread.Start();

        form.Show();
        Log("Overlay form shown.");

        Application.Run(form);
        Log("Application.Run exited.");

        cts.Cancel();
        reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        haptics.DisposeAsync().AsTask().GetAwaiter().GetResult();
        bgThread.Join(3000);
        closeEvent?.Dispose();
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
        public bool ShiftHeld;
        public bool SymActive;
        public bool PrevRightPressed;
        public bool PrevLeftPressed;
        public double SmoothRightX, SmoothRightY, SmoothLeftX, SmoothLeftY;
        public bool HasSmoothRight, HasSmoothLeft;

        private const double Smoothing = 0.20;

        public double EaseRight(double rawX, double rawY)
        {
            if (!HasSmoothRight) { SmoothRightX = rawX; SmoothRightY = rawY; HasSmoothRight = true; }
            else { SmoothRightX += Smoothing * (rawX - SmoothRightX); SmoothRightY += Smoothing * (rawY - SmoothRightY); }
            return 0;
        }

        public double EaseLeft(double rawX, double rawY)
        {
            if (!HasSmoothLeft) { SmoothLeftX = rawX; SmoothLeftY = rawY; HasSmoothLeft = true; }
            else { SmoothLeftX += Smoothing * (rawX - SmoothLeftX); SmoothLeftY += Smoothing * (rawY - SmoothLeftY); }
            return 0;
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

                double kw = s.Form.KeyW, kh = s.Form.KeyH;

                bool rightTouched = frame.RightPad.IsTouched || frame.RightPad.IsPressed;
                bool leftTouched = frame.LeftPad.IsTouched || frame.LeftPad.IsPressed;

                KeyDef? rightKey = null;
                KeyDef? leftKey = null;

                if (rightTouched)
                {
                    double px = (frame.RightPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                    double py = (frame.RightPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                    int col = Math.Clamp((int)(px / kw), 0, KeyboardLayout.MaxCols - 1);
                    int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    rightKey = KeyboardLayout.FindKeyAt(row, col);
                }

                if (leftTouched)
                {
                    double px = (frame.LeftPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                    double py = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                    int col = Math.Clamp((int)(px / kw), 0, KeyboardLayout.MaxCols - 1);
                    int row = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    leftKey = KeyboardLayout.FindKeyAt(row, col);
                }

                try
                {
                    s.Form.SetModifierState(s.ShiftHeld, s.SymActive);
                    double boardY = s.Form.BoardY;

                    if (rightTouched)
                    {
                        double rawRx = (frame.RightPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                        double rawRy = (frame.RightPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        if (!s.HasSmoothRight) { s.SmoothRightX = rawRx; s.SmoothRightY = rawRy; s.HasSmoothRight = true; }
                        else { s.SmoothRightX += 0.35 * (rawRx - s.SmoothRightX); s.SmoothRightY += 0.35 * (rawRy - s.SmoothRightY); }
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
                        double rawLx = (frame.LeftPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                        double rawLy = (frame.LeftPad.Y + 1.0) / 2.0 * (kh * KeyboardLayout.Rows);
                        if (!s.HasSmoothLeft) { s.SmoothLeftX = rawLx; s.SmoothLeftY = rawLy; s.HasSmoothLeft = true; }
                        else { s.SmoothLeftX += 0.35 * (rawLx - s.SmoothLeftX); s.SmoothLeftY += 0.35 * (rawLy - s.SmoothLeftY); }
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

                if (rightPressed && !s.PrevRightPressed && rightKey is not null)
                {
                    SendKey(rightKey, ref s.ShiftHeld, ref s.SymActive);
                    try { s.Form.FlashKey(rightKey); } catch { }
                    try { await s.Haptics.PulseRightAsync(); } catch { }
                }

                if (leftPressed && !s.PrevLeftPressed && leftKey is not null)
                {
                    SendKey(leftKey, ref s.ShiftHeld, ref s.SymActive);
                    try { s.Form.FlashKey(leftKey); } catch { }
                    try { await s.Haptics.PulseLeftAsync(); } catch { }
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

    private static void SendKey(KeyDef key, ref bool shiftHeld, ref bool symActive)
    {
        switch (key.Action)
        {
            case SpecialAction.Shift:
                shiftHeld = !shiftHeld;
                if (shiftHeld)
                    InputHelper.KeyDown(VK_LSHIFT);
                else
                    InputHelper.KeyUp(VK_LSHIFT);
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
                    ch = shiftHeld ? key.ShiftedChar : key.NormalChar;
                if (ch != '\0')
                    InputHelper.UnicodeChar(ch);
                break;
        }
    }
}
