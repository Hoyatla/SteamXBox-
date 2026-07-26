using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Osk;

public static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool ShowCursor(bool bShow);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const uint MouseEventfLeftdown = 0x0002;
    private const uint MouseEventfLeftup = 0x0004;
    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;

    [STAThread]
    public static async Task Main(string[] args)
    {
        var reader = new PadInputReader();
        var haptics = new HapticFeedback();
        var mapper = new ScreenMapper();

        var window = new OverlayWindow();

        int vx = GetSystemMetrics(SmXvirtualscreen);
        int vy = GetSystemMetrics(SmYvirtualscreen);
        int vw = GetSystemMetrics(78);
        int vh = GetSystemMetrics(79);

        window.Show();
        window.SetBounds(vx, vy, vw, vh);
        ShowCursor(false);

        var cts = new CancellationTokenSource();

        window.Closing += (_, _) =>
        {
            cts.Cancel();
            ShowCursor(true);
        };

        bool wasTouched = false;
        bool prevRightClick = false;
        bool prevLeftClick = false;

        try
        {
            await foreach (var state in reader.ReadFramesAsync(cts.Token))
            {
                mapper.UpdateOskBounds();

                if (!mapper.IsOskFound)
                {
                    if (wasTouched)
                    {
                        ShowCursor(true);
                        wasTouched = false;
                    }
                    continue;
                }

                if (!wasTouched)
                {
                    mapper.Reset();
                    ShowCursor(false);
                    wasTouched = true;
                }

                mapper.UpdateRightPad(state.RightPad.X, state.RightPad.Y);
                mapper.UpdateLeftPad(state.LeftPad.X, state.LeftPad.Y);

                var right = mapper.RightCursor;
                var left = mapper.LeftCursor;

                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    window.SetRightCursor(right.X, right.Y);
                    window.SetLeftCursor(left.X, left.Y);
                });

                bool rightClick = state.RightPad.IsPressed;
                bool leftClick = state.LeftPad.IsPressed;

                if (rightClick && !prevRightClick)
                {
                    int sx = (int)(right.X + vx);
                    int sy = (int)(right.Y + vy);
                    _ = window.Dispatcher.BeginInvoke(() => window.FlashRight());
                    SetCursorPos(sx, sy);
                    mouse_event(MouseEventfLeftdown, 0, 0, 0, IntPtr.Zero);
                    mouse_event(MouseEventfLeftup, 0, 0, 0, IntPtr.Zero);
                    _ = haptics.PulseRightAsync();
                }

                if (leftClick && !prevLeftClick)
                {
                    int sx = (int)(left.X + vx);
                    int sy = (int)(left.Y + vy);
                    _ = window.Dispatcher.BeginInvoke(() => window.FlashLeft());
                    SetCursorPos(sx, sy);
                    mouse_event(MouseEventfLeftdown, 0, 0, 0, IntPtr.Zero);
                    mouse_event(MouseEventfLeftup, 0, 0, 0, IntPtr.Zero);
                    _ = haptics.PulseLeftAsync();
                }

                prevRightClick = rightClick;
                prevLeftClick = leftClick;

                if (Process.GetProcessesByName("osk").Length == 0)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"OSK overlay error: {ex.Message}");
        }
        finally
        {
            ShowCursor(true);
            window.Dispatcher.Invoke(() => window.Close());
            await reader.DisposeAsync().ConfigureAwait(false);
            await haptics.DisposeAsync().ConfigureAwait(false);
        }
    }
}
