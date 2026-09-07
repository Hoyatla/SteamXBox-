using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Input;

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
/// <b>Raw input, and it replaced a low-level keyboard hook for a reason worth writing down.</b> A
/// hook does not watch keystrokes, it <i>stands in their way</i>: Windows hands the key to every
/// hooked process in turn and waits for each to answer before the key reaches the application the
/// user is typing into. The wait is bounded by <c>LowLevelHooksTimeout</c>, which is not set on most
/// machines and therefore defaults to three hundred milliseconds — per keystroke, for the whole
/// computer.
///
/// <para>
/// Measured here, and reported by the user: typing in another application was slow whenever SenSÉ
/// was open, <i>even idle</i>. The callback was blameless — it allocated nothing and returned at
/// once — but it lived inside a process that renders an interface, watches folders, holds an index
/// and talks to a language model. A garbage collection suspends every managed thread, the hook's
/// included, and each suspension is a late keystroke somewhere else on the machine.
/// </para>
///
/// <para>
/// Raw input has no such standing. The keystroke goes to its application immediately and a copy is
/// posted to the message-only window below, off to one side. Late delivery here delays this class's
/// own gesture and nothing else. That the swap is possible at all is decided by a single fact: this
/// class never swallowed a key — it always passed the event on, because a shortcut that ate Shift
/// would break capital letters system-wide. Suppression is the only power a hook has that raw input
/// does not, and it was the one power never used.
/// </para>
///
/// <para>
/// What is given up, measured rather than assumed: nothing that was in use. The gestures still fire
/// on keys the overlay keyboard and the MCP tools inject — a <c>SendInput</c> of F13 was watched
/// arriving here as key-down then key-up, from a window that never held the focus — and they still
/// fire while another application is in front, which is the whole reason for the input-sink flag.
/// The one power that is genuinely gone is suppression, and this class never used it.
/// </para>
///
/// <para>
/// One registration for every gesture, and that is why this class is shaped this way rather than
/// being instantiated once per shortcut: a process registers a usage page once, and the second
/// registration replaces the first instead of adding to it. Adding a gesture here costs a set lookup.
/// </para>
///
/// <para>
/// The gesture itself is decided by <see cref="DoubleTapDetector"/>, which is testable. What is here
/// is only the plumbing: the window, the device registration, the key codes, and posting the result
/// to the UI thread.
/// </para>
/// </remarks>
public sealed class DoubleTapHotkey : IDisposable
{
    private const uint WM_QUIT = 0x0012;
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;

    /// <summary>The generic desktop usage page, and the keyboard on it. HID, not Windows.</summary>
    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageKeyboard = 0x06;

    /// <summary>Deliver the keys even when no window of this process has the focus.</summary>
    /// <remarks>
    /// The whole point: the gestures are global. Without this flag the keys would only arrive while
    /// SenSÉ was already in front, which is when the user least needs a shortcut to reach it.
    /// </remarks>
    private const uint RIDEV_INPUTSINK = 0x00000100;

    /// <summary>Stop delivering. The target window must be null for this one.</summary>
    private const uint RIDEV_REMOVE = 0x00000001;

    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;

    /// <summary>Set when the key came in through the E0 escape — the right-hand Ctrl and Alt.</summary>
    private const ushort RI_KEY_E0 = 0x02;

    /// <summary>The parent that makes a window message-only: no pixels, no taskbar, no z-order.</summary>
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    /// <summary>The scan code of the right-hand Shift, which is how the two are told apart.</summary>
    private const ushort ScanRightShift = 0x36;

    /// <summary>Not a key. Windows sends it as the first half of an escaped pair.</summary>
    private const ushort VK_FAKE = 0xFF;

    /// <summary>Shift, whichever of the three codes the keyboard reports.</summary>
    public static IReadOnlySet<int> ShiftKeys { get; } =
        new HashSet<int> { VK_SHIFT, VK_LSHIFT, VK_RSHIFT };

    /// <summary>One key, by its code on this machine's layout.</summary>
    public static IReadOnlySet<int> Key(int virtualKey) => new HashSet<int> { virtualKey };

    private readonly DoubleTapRouter<DoubleTapGesture> _router;
    private readonly string _names;

    // Monotonic and fine-grained, where TickCount64 advances in fifteen-millisecond steps: the
    // double-tap window is 350 ms, and a clock that coarse would decide the edges of it by luck.
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly Action<string>? _log;

    // Held for as long as the window lives. A delegate handed to unmanaged code is not rooted by the
    // call; collected, the next message to this window calls into freed memory.
    private readonly WindowProcedure _procedure;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _window;
    private IntPtr _module;
    private string? _className;
    private ushort _class;
    private bool _listening;

    public DoubleTapHotkey(IEnumerable<DoubleTapGesture> gestures, Action<string>? log = null)
    {
        var all = gestures.ToArray();

        _router = new DoubleTapRouter<DoubleTapGesture>(all.Select(g => (g, g.Keys)));
        _names = string.Join(", ", all.Select(g => g.Name));
        _log = log;
        _procedure = OnMessage;
    }

    /// <summary>
    /// Starts listening, on a thread of its own. Safe to call twice.
    /// </summary>
    /// <remarks>
    /// <b>Raw input is delivered to a window, and a window belongs to the thread that created it —
    /// which must be pumping messages for anything to arrive.</b>
    ///
    /// <para>
    /// The interface thread would have done, now that a slow answer no longer holds up the machine.
    /// It gets its own thread anyway, for a reason the history of this file makes plain: while a
    /// hook was installed from the interface thread, copying a folder in the file manager made the
    /// clipboard listener block it, and the keyboard was dead everywhere for thirty-three seconds.
    /// The hook is gone and that particular disaster with it, but a thread that does nothing else
    /// cannot be busy, and gestures that answer while the interface is stuck are worth the thread.
    /// </para>
    ///
    /// <para>
    /// The work a gesture triggers is still posted to the interface thread, as it always was.
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
            Name = "SenSÉ raw keyboard",

            // Normal, and that is the change. The hook this replaced was raised above normal
            // because Windows held every keystroke on the machine until it answered; nothing waits
            // on this thread any more, so preferring it would buy the user nothing.
        };

        _thread.Start();

        // Waited for so that Stop can rely on the thread identifier being known. Bounded, because a
        // registration that failed must not hold up the start of the environment.
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>The listener's whole life: window, registration, pump, teardown.</summary>
    private void Pump(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();

        try
        {
            _window = Ouvrir();
            _listening = _window != IntPtr.Zero && Inscrire(_window, RIDEV_INPUTSINK);
        }
        catch (Exception exception)
        {
            _log?.Invoke($"double-tap hotkeys: {exception.GetType().Name}: {exception.Message}");
        }

        _log?.Invoke(_listening
            ? $"double-tap hotkeys listening on raw keyboard input: {_names}."
            : "double-tap hotkeys unavailable: raw keyboard input could not be registered.");

        ready.Set();

        if (_listening)
        {
            // A plain message loop, and deliberately nothing else. This thread exists to be idle.
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }

        Fermer();
    }

    /// <summary>Creates the message-only window the raw input is posted to.</summary>
    /// <remarks>
    /// A class name unique to this instance, because a window class is registered per process and
    /// re-registering a name that already exists fails — which would make a second
    /// <see cref="Start"/> after a <see cref="Stop"/> silently deaf.
    /// </remarks>
    private IntPtr Ouvrir()
    {
        _module = GetModuleHandle(null);
        _className = $"SenSÉ.RawKeyboard.{Guid.NewGuid():N}";

        var classe = new WNDCLASS
        {
            Procedure = _procedure,
            Instance = _module,
            ClassName = _className,
        };

        _class = RegisterClass(ref classe);

        if (_class == 0)
        {
            _log?.Invoke($"double-tap hotkeys: RegisterClass failed ({Marshal.GetLastWin32Error()}).");
            return IntPtr.Zero;
        }

        var window = CreateWindowEx(
            0, _className, "SenSÉ raw keyboard", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, _module, IntPtr.Zero);

        if (window == IntPtr.Zero)
        {
            _log?.Invoke($"double-tap hotkeys: CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
        }

        return window;
    }

    /// <summary>Asks for, or gives up, the keyboard's raw stream.</summary>
    private bool Inscrire(IntPtr target, uint flags)
    {
        var device = new RAWINPUTDEVICE
        {
            UsagePage = UsagePageGeneric,
            Usage = UsageKeyboard,
            Flags = flags,
            Target = target,
        };

        if (RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            return true;
        }

        _log?.Invoke(
            $"double-tap hotkeys: RegisterRawInputDevices failed ({Marshal.GetLastWin32Error()}).");

        return false;
    }

    /// <summary>Gives everything back, in the order Windows expects.</summary>
    private void Fermer()
    {
        if (_listening)
        {
            Inscrire(IntPtr.Zero, RIDEV_REMOVE);
            _listening = false;
        }

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_class != 0 && _className is not null)
        {
            UnregisterClass(_className, _module);
            _class = 0;
            _className = null;
        }

        _log?.Invoke("double-tap hotkeys removed.");
    }

    /// <summary>Removes the listener. Safe to call twice.</summary>
    /// <remarks>
    /// A window can only be destroyed by the thread that created it, so this asks rather than does:
    /// quitting that thread's message loop is what tears the listener down.
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

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_INPUT)
        {
            OnKey(lParam);
        }

        // Passed on even when handled: WM_INPUT leaves state behind that only DefWindowProc cleans up.
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void OnKey(IntPtr input)
    {
        try
        {
            var size = (uint)Marshal.SizeOf<RAWINPUTKEYBOARD>();
            var header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

            // uint.MaxValue is the API's -1: the buffer was too small, or the handle is stale
            // because another message already consumed it.
            if (GetRawInputData(input, RID_INPUT, out var raw, ref size, header) == uint.MaxValue)
            {
                return;
            }

            if (raw.Header.Type != RIM_TYPEKEYBOARD || raw.Keyboard.VKey == VK_FAKE)
            {
                return;
            }

            var key = Coder(raw.Keyboard);

            if (raw.Keyboard.Message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                foreach (var gesture in _router.Press(key, _clock.ElapsedMilliseconds))
                {
                    // Posted, never run here. Nothing on the machine waits for this window any more,
                    // but the gesture's work belongs to the interface thread all the same.
                    Application.Current?.Dispatcher.BeginInvoke(gesture.Triggered);
                }
            }
            else if (raw.Keyboard.Message is WM_KEYUP or WM_SYSKEYUP)
            {
                _router.Release(key);
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape into a window procedure: an exception here is a crash in the
            // message loop, and the gestures would go silent for the rest of the session.
            _log?.Invoke($"double-tap hotkeys: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Turns a raw key into the code a hook would have reported for it.</summary>
    /// <remarks>
    /// Raw input names the modifiers neutrally — one <c>VK_SHIFT</c> for both — while a low-level
    /// hook names the side. Restoring the side is not cosmetic: the gestures were written against
    /// the hook's codes, and this keeps them seeing exactly the stream they saw before. The side is
    /// carried in the scan code for Shift, and in the E0 escape flag for Ctrl and Alt.
    /// </remarks>
    private static int Coder(RAWKEYBOARD key)
    {
        var etendue = (key.Flags & RI_KEY_E0) != 0;

        return key.VKey switch
        {
            VK_SHIFT => key.MakeCode == ScanRightShift ? VK_RSHIFT : VK_LSHIFT,
            VK_CONTROL => etendue ? VK_RCONTROL : VK_LCONTROL,
            VK_MENU => etendue ? VK_RMENU : VK_LMENU,
            _ => key.VKey,
        };
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint Style;
        public WindowProcedure Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    /// <summary>A RAWINPUT known to carry a keyboard, which is all this window ever asked for.</summary>
    /// <remarks>
    /// The real structure ends in a union of mouse, keyboard and device data; only the keyboard arm
    /// is declared because only the keyboard usage is registered, and the type is checked before the
    /// arm is read. The header is eight-byte aligned on x64, which is where the union starts, so the
    /// two members fall exactly where Windows put them.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTKEYBOARD
    {
        public RAWINPUTHEADER Header;
        public RAWKEYBOARD Keyboard;
    }

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

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS classe);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(ref RAWINPUTDEVICE devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput, uint command, out RAWINPUTKEYBOARD data, ref uint size, uint headerSize);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? name);
}
