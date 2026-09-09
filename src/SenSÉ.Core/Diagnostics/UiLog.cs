using System.Globalization;
using System.IO;
using System.Reflection;

namespace SenSÉ.Core.Diagnostics;

/// <summary>
/// The session record of a SenSÉ window process.
/// </summary>
/// <remarks>
/// The controller bridge has written a detailed log since the day it was built. The two processes
/// that actually own the screen — the environment and the configuration window — wrote nothing. So
/// when something happened on screen, the only account of it was whatever the user could put into
/// words afterwards, from memory, about half a second of movement. That is not a reasonable thing
/// to ask, and it is the reason several defects here went undiagnosed for entire sessions.
///
/// <para>
/// What is recorded is chosen for that purpose: <b>anything that changes what is on screen, and
/// anything that failed</b>. Windows appearing and closing, tiles launched, child processes started
/// and killed, exceptions — including the ones deliberately swallowed, which are precisely the
/// failures that leave no visible trace.
/// </para>
///
/// <para>
/// Static, and never throwing. A logger that needs wiring through constructors would not be reached
/// from the places that matter most — a <c>catch</c> in a static helper, an exception handler
/// running while the application is coming apart. And a diagnostic that can itself fail is a
/// liability: every call here is safe to make before <see cref="Start"/>, after <see cref="Stop"/>,
/// and from any thread.
/// </para>
/// </remarks>
public static class UiLog
{
    private static readonly object Gate = new();
    private static DiagnosticLog? _log;
    private static string _process = "ui";

    /// <summary>Path of the file currently being written, or null when logging is not running.</summary>
    public static string? Path { get; private set; }

    /// <summary>
    /// Opens the log for this process and records what it is.
    /// </summary>
    /// <param name="processName">Short name used in the file name, such as "desktop" or "gui".</param>
    /// <remarks>
    /// One file per process rather than one shared file. The environment and the configuration
    /// window run at the same time, and interleaving two processes' lines through a shared handle
    /// means either locking across processes or losing lines — while the question being asked of
    /// this log is almost always about one of them.
    ///
    /// The identity block is written before anything else and repeats what the bridge records, so a
    /// log can be matched to a build without having to be trusted about which one it came from.
    /// </remarks>
    public static void Start(string processName)
    {
        lock (Gate)
        {
            if (_log is not null)
            {
                return;
            }

            _process = string.IsNullOrWhiteSpace(processName) ? "ui" : processName;

            try
            {
                var path = CheminDebug.DebugLog(_process);

                _log = new DiagnosticLog(path, LogLevel.Debug, LogCategory.Ui);
                Path = path;
            }
            catch
            {
                // A read-only or missing directory must not stop the interface from starting. The
                // rest of this class no-ops from here.
                _log = null;
                Path = null;
                return;
            }
        }

        WriteIdentity();
    }

    private static void WriteIdentity()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                              ?.InformationalVersion
                          ?? assembly?.GetName().Version?.ToString()
                          ?? "unknown";

            var now = DateTimeOffset.Now;

            Block($"=== SenSÉ {_process} session start ===",
            [
                $"version        : {version}",
                $"executable     : {Environment.ProcessPath ?? "unknown"}",
                $"base directory : {AppContext.BaseDirectory}",
                $"process id     : {Environment.ProcessId}",
                // The sign is written explicitly: a TimeSpan format string drops it, which turned
                // UTC+02:00 into UTC02:00 and would silently read as UTC on a machine behind it.
                $"local time     : {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} "
                + $"(UTC{(now.Offset < TimeSpan.Zero ? '-' : '+')}{now.Offset:hh\\:mm})",
                $"os             : {Environment.OSVersion.VersionString} / {(Environment.Is64BitProcess ? "X64" : "X86")}",
                $"runtime        : {Environment.Version}",
            ]);
        }
        catch
        {
            // Identity is context, not the record itself; losing it must not lose the session.
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    /// <summary>
    /// Records a window changing what is on screen.
    /// </summary>
    /// <param name="window">The window, by type name.</param>
    /// <param name="action">What happened to it: shown, hidden, closed, activated.</param>
    /// <remarks>
    /// The lines this log exists for. A window that opens and closes within a second leaves nothing
    /// a user can describe, and a sequence of these is exactly what such a moment looks like written
    /// down.
    /// </remarks>
    public static void Window(string window, string action, string? detail = null)
        => Write(LogLevel.Info, detail is null
            ? $"window {window}: {action}"
            : $"window {window}: {action} ({detail})");

    /// <summary>Records a tile or command the user activated.</summary>
    public static void Action(string name, string? detail = null)
        => Write(LogLevel.Info, detail is null ? $"action {name}" : $"action {name}: {detail}");

    /// <summary>Records a child process starting, being found already running, or being stopped.</summary>
    public static void Process(string name, string outcome)
        => Write(LogLevel.Info, $"process {name}: {outcome}");

    /// <summary>
    /// Records a failure that the caller is deliberately continuing past.
    /// </summary>
    /// <remarks>
    /// This is the method most of this class was written for. Swallowing an exception is often the
    /// right call in an interface — a broken theme must not stop the environment from starting — but
    /// swallowing it <i>silently</i> converts a precise, already-diagnosed failure into a mystery
    /// about something the user thought they saw. Catching stays; the silence goes.
    /// </remarks>
    public static void Failure(string what, Exception exception)
        => Write(LogLevel.Warn, $"{what} failed: {exception.GetType().Name}: {exception.Message}");

    /// <summary>Records an unhandled exception, with the stack trace.</summary>
    public static void Crash(string what, Exception exception)
    {
        Write(LogLevel.Error, $"{what}: {exception.GetType().Name}: {exception.Message}");
        Block("stack trace", (exception.StackTrace ?? "(none)").Split('\n').Select(l => l.TrimEnd()));
    }

    /// <summary>Closes the log, recording why the process is ending.</summary>
    /// <remarks>
    /// The reason matters more than the fact. "Closed by the user" and "exited while restarting for
    /// a theme" produce the same silence otherwise, and only one of them is a bug.
    /// </remarks>
    public static void Stop(string reason)
    {
        lock (Gate)
        {
            if (_log is null)
            {
                return;
            }

            try
            {
                _log.Write(LogLevel.Info, LogCategory.Ui, $"=== session end: {reason} ===");
                _log.Dispose();
            }
            catch
            {
                // Ending is best-effort by definition.
            }

            _log = null;
            Path = null;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        lock (Gate)
        {
            try
            {
                _log?.Write(level, LogCategory.Ui, message);
            }
            catch
            {
                // Never let the record of a failure become one.
            }
        }
    }

    private static void Block(string title, IEnumerable<string> lines)
    {
        lock (Gate)
        {
            try
            {
                _log?.WriteBlock(LogLevel.Info, LogCategory.Ui, title, lines);
            }
            catch
            {
            }
        }
    }
}
