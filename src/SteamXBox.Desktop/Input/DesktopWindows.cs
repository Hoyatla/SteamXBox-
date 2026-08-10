using System.Reflection;
using System.Runtime.InteropServices;

namespace SteamXBox.Desktop.Input;

/// <summary>
/// Clears the screen of every window, leaving the environment — and puts them back.
/// </summary>
/// <remarks>
/// What Windows itself does for <c>Win+M</c>, asked of the shell rather than reproduced. Walking the
/// windows and minimising them one by one is the obvious implementation and the wrong one: it has to
/// decide what counts as a window, and it gets that decision wrong on tool windows, on owned
/// dialogs, on anything that draws its own frame. The shell already knows, because it is the shell.
///
/// <para>
/// The environment itself is unaffected, and not because it is excluded here — it refuses to
/// minimise at all, in <c>MainWindow</c>. That is the right place for the rule: it holds against this
/// call, against <c>Win+M</c>, and against anything else that ever asks.
/// </para>
/// </remarks>
public static class DesktopWindows
{
    /// <summary>
    /// How many ordinary windows were up just before the last clear, or <c>-1</c> when the screen is
    /// not currently cleared.
    /// </summary>
    /// <remarks>
    /// Counted before, and that detail was found by measuring rather than by thinking. The first
    /// version counted straight after asking the shell to minimise, meaning to record what had
    /// survived — and recorded the full ten windows, because the shell minimises asynchronously and
    /// nothing had moved yet. The comparison was then always true and the self-correction did
    /// nothing at all, while behaving correctly in the simple case that would have been tested.
    /// </remarks>
    private static int _countBeforeClear = -1;

    /// <summary>
    /// Clears the screen, or puts it back if the last press cleared it.
    /// </summary>
    /// <remarks>
    /// Decided by counting rather than by a flag alone, because a flag alone goes out of step the
    /// moment the user restores a window by hand: the next press would put everything back — the
    /// direction they did not ask for — and the shortcut would feel broken.
    ///
    /// <para>
    /// The comparison is against how many there were before, not against zero. Some windows never
    /// minimise, and measuring that on a real desktop is what showed it: an always-on-top
    /// picture-in-picture, an invisible explorer helper window, a desktop widget and the Windows
    /// input panel all stayed up — ten windows became three, not none. Windows' own <c>Win+M</c>
    /// leaves exactly the same ones. Waiting for zero would mean the screen never counts as cleared
    /// and the shortcut never restores.
    /// </para>
    /// </remarks>
    public static bool Toggle(Action<string>? log = null)
    {
        var now = OrdinaryWindows();

        // Said on the way in, and on success as well as failure. Only failures were logged, which
        // left the one question that actually gets asked unanswerable: when nothing happens on
        // screen, was the shortcut not recognised, or recognised and ignored by the shell?
        log?.Invoke(
            $"clear-the-screen gesture: {now} ordinary window(s), {_countBeforeClear} before the "
            + "last clear.");

        if (_countBeforeClear >= 0 && now < _countBeforeClear)
        {
            var restored = UndoMinimizeAll(log);
            _countBeforeClear = -1;

            return restored;
        }

        if (!MinimizeAll(log))
        {
            return false;
        }

        _countBeforeClear = now;

        return true;
    }

    /// <summary>Minimises every window, and says whether the shell accepted.</summary>
    /// <remarks>
    /// Late-bound through the ProgID rather than a typed interop reference, which keeps this to the
    /// one call it needs without carrying a COM reference through the build. <c>MinimizeAll</c> is
    /// documented on <c>IShellDispatch</c>, so this is the supported route and not one of the
    /// undocumented window messages that do the same thing.
    /// </remarks>
    public static bool MinimizeAll(Action<string>? log = null) => Ask("MinimizeAll", log);

    /// <summary>Puts back what the last clear minimised.</summary>
    /// <remarks>
    /// Spelled <c>UndoMinimizeALL</c>, with the last three letters capitalised, which is not a typo
    /// here but one in the shell's own interface. Late binding matches the name exactly, so the
    /// natural spelling fails at run time and only at run time.
    /// </remarks>
    public static bool UndoMinimizeAll(Action<string>? log = null) => Ask("UndoMinimizeALL", log);

    /// <summary>Puts one request to the shell.</summary>
    private static bool Ask(string method, Action<string>? log)
    {
        object? shell = null;

        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");

            if (type is null)
            {
                log?.Invoke($"{method}: Shell.Application is not registered.");
                return false;
            }

            shell = Activator.CreateInstance(type);

            type.InvokeMember(method, BindingFlags.InvokeMethod, null, shell, null);

            log?.Invoke($"{method}: the shell accepted.");

            return true;
        }
        catch (Exception exception)
        {
            // The shell is out of reach — busy, restarting, or refusing the automation call. One
            // shortcut does nothing; nothing else about the session is affected.
            log?.Invoke($"{method} failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    /// <summary>
    /// How many ordinary windows are on screen: visible, not minimised, and something the user
    /// thinks of as a window.
    /// </summary>
    /// <remarks>
    /// The same filter the taskbar applies. A tool window is excluded because it is a palette or a
    /// helper, an untitled window because it is machinery, and an owned window because it is a
    /// dialog belonging to something already counted.
    /// </remarks>
    private static int OrdinaryWindows()
    {
        var count = 0;

        EnumWindows((hwnd, _) =>
        {
            if (IsOrdinary(hwnd))
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);

        return count;
    }

    private static bool IsOrdinary(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var extended = GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64();

        if ((extended & WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        return GetWindowTextLengthW(hwnd) > 0;
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
}
