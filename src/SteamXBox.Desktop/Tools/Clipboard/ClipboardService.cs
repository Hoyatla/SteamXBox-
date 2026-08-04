using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

using SteamXBox.Tools.Clipboard;

namespace SteamXBox.Desktop.Tools.Clipboard;

/// <summary>
/// Watches the clipboard for as long as the environment runs, and pastes back into the application
/// the user was actually in.
/// </summary>
/// <remarks>
/// This is the first tool that is not just a window. A history only exists if something was
/// listening at the moment of the copy, so the watcher starts with the environment and never stops
/// — which is why <see cref="ToolDescriptor"/> gained a start hook rather than being opened on
/// demand like the calculator.
///
/// It listens through <c>AddClipboardFormatListener</c> rather than by polling: the operating
/// system says when the clipboard changed, so nothing is missed between two polls and nothing is
/// burned checking a clipboard that has not moved.
/// </remarks>
public static class ClipboardService
{
    public static ClipboardHistory History { get; } = new();

    private static HwndSource? _sink;

    /// <summary>The window that had the foreground before the environment took it.</summary>
    /// <remarks>
    /// The whole reason Win+V was unsatisfying here: a paste goes to the focused window, and once
    /// the overlay is focused that is the overlay, which has nowhere to paste into. Remembering the
    /// previous foreground is what lets the paste land where the user meant.
    /// </remarks>
    private static IntPtr _target;

    /// <summary>True while this class is putting something on the clipboard itself.</summary>
    private static bool _writing;

    public static void Start()
    {
        if (_sink is not null)
        {
            return;
        }

        // A message-only window: it exists purely to receive WM_CLIPBOARDUPDATE, is never shown and
        // never appears in the taskbar or in Alt+Tab.
        var parameters = new HwndSourceParameters("SteamXBoxClipboard")
        {
            ParentWindow = HwndMessage,
        };

        _sink = new HwndSource(parameters);
        _sink.AddHook(OnMessage);
        AddClipboardFormatListener(_sink.Handle);

        // The paste target has to be captured while the user is still in the other application:
        // by the time the clipboard window is open, SteamXBox is the foreground and the answer is
        // already lost. Polling is the only way to know what was in front a moment ago — there is
        // no notification for "the foreground is about to change to us".
        //
        // Half a second is plenty: nobody copies, switches window and pastes inside that.
        _foregroundWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _foregroundWatch.Tick += (_, _) => RememberTarget();
        _foregroundWatch.Start();

        // What is already on the clipboard when the environment starts counts as the first entry;
        // otherwise the history is empty until the user copies something new, which reads as broken.
        Record();
    }

    private static System.Windows.Threading.DispatcherTimer? _foregroundWatch;

    public static void Stop()
    {
        if (_sink is null)
        {
            return;
        }

        _foregroundWatch?.Stop();
        _foregroundWatch = null;
        RemoveClipboardFormatListener(_sink.Handle);
        _sink.RemoveHook(OnMessage);
        _sink.Dispose();
        _sink = null;
    }

    /// <summary>Remembers which window to paste into, before the overlay takes the foreground.</summary>
    public static void RememberTarget()
    {
        var foreground = GetForegroundWindow();

        // Never our own windows: pasting into the environment is exactly the failure being avoided.
        if (foreground != IntPtr.Zero && !IsOurs(foreground))
        {
            _target = foreground;
        }
    }

    /// <summary>
    /// Puts an entry back on the clipboard and pastes it into the remembered window.
    /// </summary>
    /// <returns>True when a paste was actually sent.</returns>
    public static bool PasteInto(ClipboardEntry entry)
    {
        _writing = true;
        try
        {
            // The shared library keeps the image as object so its rules can be tested without WPF;
            // this is the one place that needs the real type back.
            if (entry.Kind == ClipboardKind.Image && entry.Image is BitmapSource image)
            {
                System.Windows.Clipboard.SetImage(image);
            }
            else
            {
                System.Windows.Clipboard.SetText(entry.Text);
            }
        }
        catch
        {
            // Another process can hold the clipboard open; nothing to paste in that case.
            return false;
        }
        finally
        {
            _writing = false;
        }

        if (_target == IntPtr.Zero || !IsWindow(_target))
        {
            // The clipboard still holds the entry, so selecting it was not wasted — the user can
            // paste manually. Saying so is better than pretending it worked.
            return false;
        }

        SetForegroundWindow(_target);
        SendCtrlV();
        return true;
    }

    private static IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate && !_writing)
        {
            Record();
        }

        return IntPtr.Zero;
    }

    private static void Record()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    History.Add(new ClipboardEntry(ClipboardKind.Text, text, null, DateTimeOffset.Now));
                }

                return;
            }

            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is not null)
                {
                    // Frozen so it can be shown from any thread and cannot be mutated later.
                    image.Freeze();
                    History.Add(new ClipboardEntry(
                        ClipboardKind.Image, $"Image {image.PixelWidth}×{image.PixelHeight}", image,
                        DateTimeOffset.Now));
                }
            }
        }
        catch
        {
            // The clipboard is a shared resource and is regularly locked by whoever just wrote to
            // it. A missed entry is not worth an error: the next copy will be caught.
        }
    }

    private static bool IsOurs(IntPtr hwnd)
        => Application.Current.Windows.OfType<Window>()
            .Any(w => new WindowInteropHelper(w).Handle == hwnd);

    private static void SendCtrlV()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr HwndMessage = new(-3);
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
}
