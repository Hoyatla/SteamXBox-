using System.IO;
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
    /// Where the fact that the screen is cleared is written down, for a session that does not
    /// survive to put it back.
    /// </summary>
    /// <remarks>
    /// The count above lives in memory and dies with the process. Everything else SteamXBox leaves
    /// on the machine has a way back; a desktop full of minimised windows had none — the user was
    /// left to restore them one by one without knowing what had done it.
    ///
    /// <para>
    /// In the shared state, beside the note that records hidden controllers, and for the reason that
    /// note had to move there: a marker next to the executable belongs to one copy of SteamXBox, and
    /// the copy that made the mess is not always the one that comes back.
    /// </para>
    /// </remarks>
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox",
        "windows-minimised.state");

    /// <summary>
    /// Puts back a screen that an earlier session cleared and never restored.
    /// </summary>
    /// <remarks>
    /// <b>Only if those windows can still exist.</b> Restoring blindly would be worse than doing
    /// nothing: <c>UndoMinimizeALL</c> raises everything currently minimised, so a marker left by a
    /// crash three days ago would, at the next launch, throw open every window the user had put away
    /// on purpose that morning.
    ///
    /// <para>
    /// A minimised window does not survive a restart, so the marker is only acted on when it was
    /// written since the machine last booted. Older than that and there is nothing left to give
    /// back — the marker is simply cleared.
    /// </para>
    /// </remarks>
    public static void RepairOnStart(Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(MarkerPath))
            {
                return;
            }

            var written = File.GetLastWriteTimeUtc(MarkerPath);
            var booted = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

            if (written < booted)
            {
                File.Delete(MarkerPath);
                log?.Invoke(
                    "clear-the-screen: an earlier session left the screen cleared, but the machine has "
                    + "restarted since, so those windows are long gone. Marker cleared.");
                return;
            }

            log?.Invoke("clear-the-screen: an earlier session cleared the screen and did not put it back.");
            UndoMinimizeAll(log);
            File.Delete(MarkerPath);
        }
        catch (Exception exception)
        {
            // A marker that cannot be read is not worth failing the start over; the user can press
            // the gesture twice.
            log?.Invoke($"clear-the-screen: could not check the earlier session: {exception.Message}");
        }
    }

    /// <summary>Writes or clears the marker, and never lets that stop the gesture.</summary>
    private static void Remember(bool cleared, Action<string>? log)
    {
        try
        {
            if (!cleared)
            {
                File.Delete(MarkerPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, _countBeforeClear.ToString());
        }
        catch (Exception exception)
        {
            log?.Invoke($"clear-the-screen: could not record the state: {exception.Message}");
        }
    }

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
            Remember(cleared: false, log);

            return restored;
        }

        if (!MinimizeAll(log))
        {
            return false;
        }

        _countBeforeClear = now;

        // Written after the shell has been asked, not before: a marker naming a screen that was
        // never cleared would make the next start raise windows nobody minimised.
        Remember(cleared: true, log);

        return true;
    }

    /// <summary>
    /// Puts the windows back if SteamXBox is the reason they are down.
    /// </summary>
    /// <remarks>
    /// Called when the environment closes. Clearing the screen is a gesture of the environment, so
    /// it has no business outliving it: quitting while cleared leaves an empty desktop and nothing
    /// left to undo it, and the user puts seven windows back one taskbar button at a time. That is
    /// the whole of "restoring the windows is slow" — measured, the shell does it in two
    /// milliseconds when it is asked, and the animations were already off on the machine where it
    /// felt slow.
    ///
    /// <para>
    /// Nothing happens when the screen was not cleared by us. Somebody who minimised their own
    /// windows before quitting meant them minimised.
    /// </para>
    /// </remarks>
    public static void RestoreOnExit(Action<string>? log = null)
    {
        if (_countBeforeClear < 0)
        {
            return;
        }

        log?.Invoke("clear-the-screen: putting the windows back before quitting.");

        UndoMinimizeAll(log);
        _countBeforeClear = -1;
        Remember(cleared: false, log);
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

    /// <summary>
    /// The shell automation object, created once and kept.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed. Creating one per call leaked about twenty handles <b>inside
    /// explorer.exe</b> every time the screen was cleared — eighty-nine over one short session, and
    /// they were not given back when SteamXBox exited. Releasing our side with
    /// <c>FinalReleaseComObject</c> did not prevent it: the activation itself makes objects in the
    /// shell's own process, and after ten hours of use explorer had grown from seven thousand
    /// handles to nine and a half thousand, with window ordering degrading as it went.
    ///
    /// <para>
    /// One activation for the life of the session instead. It is dropped and remade if the shell
    /// ever refuses it — explorer can restart, and a reference to the old one would then be dead
    /// for the rest of the session.
    /// </para>
    /// </remarks>
    private static object? _shell;

    private static bool Ask(string method, Action<string>? log)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");

                if (type is null)
                {
                    log?.Invoke($"{method}: Shell.Application is not registered.");
                    return false;
                }

                _shell ??= Activator.CreateInstance(type);

                type.InvokeMember(method, BindingFlags.InvokeMethod, null, _shell, null);

                log?.Invoke($"{method}: the shell accepted.");

                return true;
            }
            catch (Exception exception)
            {
                // Most likely a shell that has restarted under us, taking the object with it. Drop
                // it and try once more; a second failure is a real one.
                Release();

                if (attempt == 1)
                {
                    log?.Invoke($"{method} failed: {exception.GetType().Name}: {exception.Message}");
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>Lets go of the shell object, on the way out or after a failure.</summary>
    public static void Release()
    {
        if (_shell is null)
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(_shell);
        }
        catch (ArgumentException)
        {
            // Already released, or never a COM object. Either way there is nothing to free.
        }

        _shell = null;
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
