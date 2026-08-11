using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Input;

/// <summary>One key, tapped twice, and what that should do.</summary>
/// <param name="Name">For the log, so a gesture that fires can be told from one that does not.</param>
/// <param name="Keys">
/// Every virtual key code that counts as this gesture's key. Shift is three of them — the neutral
/// code and one per side — and a gesture that listed only the neutral one would miss half the
/// keyboards.
/// </param>
public sealed record DoubleTapGesture(string Name, IReadOnlySet<int> Keys, Action Triggered);

/// <summary>
/// Watches the keyboard for keys tapped twice, and runs what each one is bound to.
/// </summary>
/// <remarks>
/// A low-level keyboard hook, and it is worth saying plainly why one is acceptable here when the
/// plugin rules forbid so much less. A hook <i>observes</i> keystrokes in this process's own callback;
/// it does not put code inside another program, which is what the no-injection rule is about. It is
/// also a capability of the host, never of a tool: no manifest can ask for it.
///
/// <para>
/// One hook for every gesture, and that is the reason this class is shaped this way rather than
/// being instantiated once per shortcut. A low-level hook sits in the path of every keystroke on the
/// machine, so each one is a tax the whole system pays; installing a second to watch a second key
/// would double it for nothing. Adding a gesture here costs a set lookup.
/// </para>
///
/// <para>
/// It never swallows a keystroke. The callback always passes the event on, so the keys keep behaving
/// as themselves everywhere — a shortcut that ate Shift would break capital letters system-wide, and
/// one that ate Escape would trap the user in every dialog on the machine.
/// </para>
///
/// <para>
/// The gesture itself is decided by <see cref="DoubleTapDetector"/>, which is testable. What is here
/// is only the plumbing: the hook, the key codes, and posting the result to the UI thread.
/// </para>
/// </remarks>
public sealed class DoubleTapHotkey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_QUIT = 0x0012;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    public const int VK_SHIFT = 0x10;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;

    /// <summary>Shift, whichever of the three codes the keyboard reports.</summary>
    public static IReadOnlySet<int> ShiftKeys { get; } =
        new HashSet<int> { VK_SHIFT, VK_LSHIFT, VK_RSHIFT };

    /// <summary>One key, by its code on this machine's layout.</summary>
    public static IReadOnlySet<int> Key(int virtualKey) => new HashSet<int> { virtualKey };

    private readonly DoubleTapRouter<DoubleTapGesture> _router;
    private readonly string _names;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Action<string>? _log;

    // Held for as long as the hook lives. A delegate handed to unmanaged code is not rooted by the
    // call; collected, the next keystroke on the machine calls into freed memory.
    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hook;

    public DoubleTapHotkey(IEnumerable<DoubleTapGesture> gestures, Action<string>? log = null)
    {
        var all = gestures.ToArray();

        _router = new DoubleTapRouter<DoubleTapGesture>(all.Select(g => (g, g.Keys)));
        _names = string.Join(", ", all.Select(g => g.Name));
        _log = log;
        _callback = OnKey;
    }

    /// <summary>
    /// Installs the hook on a thread of its own. Safe to call twice.
    /// </summary>
    /// <remarks>
    /// <b>A low-level keyboard hook is delivered on the thread that installed it, and that thread
    /// must be pumping messages for a keystroke to get through.</b> Not on "whatever thread Windows
    /// chose", which is what the comment in this file used to claim — the mistake mattered, because
    /// it made the interface thread look like a harmless place to install from.
    ///
    /// <para>
    /// It is not. Every keystroke on the machine, in every application, waits for this hook to
    /// answer. Installed from the interface thread, anything that blocks that thread blocks the
    /// keyboard of the whole computer — measured: copying a folder in the file manager made the
    /// clipboard listener open the clipboard on the interface thread, and the keyboard was dead
    /// everywhere for thirty-three seconds, until SteamXBox was closed and the hook went with it.
    /// </para>
    ///
    /// <para>
    /// So the hook gets a thread that does nothing else and can therefore never be busy. The work a
    /// gesture triggers is still posted to the interface thread, as it always was; what changed is
    /// that the interface thread's troubles are no longer the keyboard's.
    /// </para>
    /// </remarks>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        var ready = new ManualResetEventSlim(false);

        _thread = new Thread(() => Pump(ready))
        {
            IsBackground = true,
            Name = "SteamXBox keyboard hook",
        };

        _thread.Start();

        // Waited for so that Stop can rely on the thread identifier being known. Bounded, because a
        // hook that failed to install must not hold up the start of the environment.
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>The hook's whole life: install, pump, unhook.</summary>
    private void Pump(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();

        // The module handle of this process. A managed hook needs one that is loaded, and passing
        // zero makes SetWindowsHookEx refuse for a low-level hook on some versions.
        using (var self = Process.GetCurrentProcess())
        using (var module = self.MainModule)
        {
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module?.ModuleName), 0);
        }

        _log?.Invoke(_hook == IntPtr.Zero
            ? $"double-tap hotkeys unavailable: SetWindowsHookEx failed ({Marshal.GetLastWin32Error()})."
            : $"double-tap hotkeys installed on their own thread: {_names}.");

        ready.Set();

        if (_hook == IntPtr.Zero)
        {
            return;
        }

        // A plain message loop, and deliberately nothing else. This thread exists to be idle: any
        // work put here would be paid for by every keystroke on the machine.
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _log?.Invoke("double-tap hotkeys removed.");
    }

    /// <summary>Removes the hook. The machine stops paying for it immediately.</summary>
    /// <remarks>
    /// The hook has to be removed by the thread that installed it, so this asks rather than does:
    /// quitting that thread's message loop is what unhooks.
    /// </remarks>
    public void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        // Bounded, because closing the environment must not hang on this. The thread is a background
        // one, so the process can end regardless.
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
    }

    public void Dispose() => Stop();

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        try
        {
            var message = (int)wParam;
            var key = Marshal.ReadInt32(lParam);

            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                foreach (var gesture in _router.Press(key, _clock.ElapsedMilliseconds))
                {
                    // Posted, never run here. This callback holds up every keystroke on the machine
                    // until it returns, so it does the least it can: decide, hand over, get out.
                    Application.Current?.Dispatcher.BeginInvoke(gesture.Triggered);
                }
            }
            else if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                _router.Release(key);
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape into the hook chain: an exception here is a crash inside every
            // keystroke of every application.
            _log?.Invoke($"double-tap hotkeys: {exception.GetType().Name}: {exception.Message}");
        }

        // Always passed on. Swallowing these keys would break them everywhere.
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private Thread? _thread;
    private uint _threadId;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? name);
}
