using System.Diagnostics;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Osk;

public static class Program
{
    private const ushort VK_BACK = 0x08;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_LSHIFT = 0xA0;

    [STAThread]
    public static async Task Main(string[] args)
    {
        var reader = new PadInputReader();
        var haptics = new HapticFeedback();
        var window = new OverlayWindow();

        window.Show();

        var cts = new CancellationTokenSource();

        window.Closing += (_, _) => cts.Cancel();

        bool wasConnected = false;
        bool shiftHeld = false;
        bool prevRightPressed = false;
        bool prevLeftPressed = false;
        KeyDef? prevRightKey = null;
        KeyDef? prevLeftKey = null;

        try
        {
            await foreach (var state in reader.ReadFramesAsync(cts.Token))
            {
                if (!wasConnected)
                {
                    wasConnected = true;
                    Log("OSK: pipe connected, keyboard active.");
                }

                var metrics = window.GetMetrics();
                double bx = metrics.BoardX, by = metrics.BoardY;
                double kw = metrics.KeyW, kh = metrics.KeyH;

                bool rightTouched = state.RightPad.IsTouched || state.RightPad.IsPressed;
                bool leftTouched = state.LeftPad.IsTouched || state.LeftPad.IsPressed;

                int rightCol = -1, rightRow = -1;
                int leftCol = -1, leftRow = -1;
                KeyDef? rightKey = null;
                KeyDef? leftKey = null;

                if (rightTouched)
                {
                    double px = (state.RightPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                    double py = (1.0 - state.RightPad.Y) / 2.0 * (kh * KeyboardLayout.Rows);
                    rightCol = Math.Clamp((int)(px / kw), 0, KeyboardLayout.MaxCols - 1);
                    rightRow = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    rightKey = KeyboardLayout.FindKeyAt(rightRow, rightCol);
                }

                if (leftTouched)
                {
                    double px = (state.LeftPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                    double py = (1.0 - state.LeftPad.Y) / 2.0 * (kh * KeyboardLayout.Rows);
                    leftCol = Math.Clamp((int)(px / kw), 0, KeyboardLayout.MaxCols - 1);
                    leftRow = Math.Clamp((int)(py / kh), 0, KeyboardLayout.Rows - 1);
                    leftKey = KeyboardLayout.FindKeyAt(leftRow, leftCol);
                }

                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    if (rightTouched)
                    {
                        double rx = (state.RightPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                        double ry = (1.0 - state.RightPad.Y) / 2.0 * (kh * KeyboardLayout.Rows);
                        window.SetRightCursor(rx, ry);
                        window.HighlightKey(rightKey);
                    }
                    else
                    {
                        window.HideRightCursor();
                    }

                    if (leftTouched)
                    {
                        double lx = (state.LeftPad.X + 1.0) / 2.0 * (kw * KeyboardLayout.MaxCols);
                        double ly = (1.0 - state.LeftPad.Y) / 2.0 * (kh * KeyboardLayout.Rows);
                        window.SetLeftCursor(lx, ly);
                    }
                    else
                    {
                        window.HideLeftCursor();
                    }
                });

                bool rightPressed = state.RightPad.IsPressed;
                bool leftPressed = state.LeftPad.IsPressed;

                if (rightPressed && !prevRightPressed && rightKey is not null)
                {
                    SendKey(rightKey, shiftHeld);
                    _ = window.Dispatcher.BeginInvoke(() => window.FlashKey(rightKey));
                    try { await haptics.PulseRightAsync(); } catch { }
                }

                if (leftPressed && !prevLeftPressed && leftKey is not null)
                {
                    SendKey(leftKey, shiftHeld);
                    _ = window.Dispatcher.BeginInvoke(() => window.FlashKey(leftKey));
                    try { await haptics.PulseLeftAsync(); } catch { }
                }

                prevRightPressed = rightPressed;
                prevLeftPressed = leftPressed;
                prevRightKey = rightKey;
                prevLeftKey = leftKey;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"OSK error: {ex.Message}");
        }
        finally
        {
            window.Dispatcher.Invoke(() => window.HideAll());
            window.Dispatcher.Invoke(() => window.Close());
            await reader.DisposeAsync().ConfigureAwait(false);
            await haptics.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void SendKey(KeyDef key, bool shiftHeld)
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
                char ch = shiftHeld ? key.ShiftedChar : key.NormalChar;
                if (ch != '\0')
                    InputHelper.UnicodeChar(ch);
                break;
        }
    }

    private static void Log(string msg)
    {
        try { Console.Error.WriteLine(msg); } catch { }
    }
}
