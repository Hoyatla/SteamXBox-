using System.Globalization;
using System.IO;
using System.Text;

namespace Sc2Xboxed.Core.Diagnostics;

/// <summary>
/// File logger with levels, subsystem categories and size-capped rotation.
/// </summary>
/// <remarks>
/// Replaces an unconditional per-frame write that produced multi-megabyte logs in minutes and buried
/// every useful line. Timestamps are local time, matching what file explorers and the user's clock
/// show: the previous logger stamped UTC while file modification times were local, which made
/// correlating a log with a build genuinely misleading.
/// </remarks>
public sealed class DiagnosticLog : IDisposable
{
    private const long DefaultMaxBytes = 8L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private StreamWriter? _writer;
    private long _bytesWritten;
    private bool _disposed;

    public DiagnosticLog(
        string path,
        LogLevel level = LogLevel.Info,
        LogCategory categories = LogCategory.Default,
        long maxBytes = DefaultMaxBytes,
        bool alsoConsole = false)
    {
        _path = path;
        _maxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
        Level = level;
        Categories = categories;
        AlsoConsole = alsoConsole;

        Open(truncate: true);
    }

    public LogLevel Level { get; set; }

    public LogCategory Categories { get; set; }

    /// <summary>Mirror output to stdout, for interactive CLI runs.</summary>
    public bool AlsoConsole { get; set; }

    public string Path => _path;

    /// <summary>
    /// Cheap guard for hot paths: lets callers skip building an interpolated string that would be
    /// discarded anyway.
    /// </summary>
    public bool IsEnabled(LogLevel level, LogCategory category)
    {
        return !_disposed && level <= Level && (Categories & category) != 0;
    }

    public void Error(LogCategory category, string message) => Write(LogLevel.Error, category, message);

    public void Warn(LogCategory category, string message) => Write(LogLevel.Warn, category, message);

    public void Info(LogCategory category, string message) => Write(LogLevel.Info, category, message);

    public void Debug(LogCategory category, string message) => Write(LogLevel.Debug, category, message);

    public void Trace(LogCategory category, string message) => Write(LogLevel.Trace, category, message);

    /// <summary>Writes a block of lines under one category without re-stamping each line.</summary>
    public void WriteBlock(LogLevel level, LogCategory category, string title, IEnumerable<string> lines)
    {
        if (!IsEnabled(level, category))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(Prefix(level, category)).Append(title).Append('\n');
        foreach (var line in lines)
        {
            builder.Append("             ").Append(line).Append('\n');
        }

        Emit(builder.ToString(), trailingNewline: false);
    }

    public void Write(LogLevel level, LogCategory category, string message)
    {
        if (!IsEnabled(level, category))
        {
            return;
        }

        Emit(Prefix(level, category) + message, trailingNewline: true);
    }

    private static string Prefix(LogLevel level, LogCategory category)
    {
        // Local time on purpose; the session header records the UTC offset once.
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        return $"[{stamp}] {Abbreviate(level)} {CategoryName(category),-8} ";
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Error => "ERR ",
        LogLevel.Warn => "WARN",
        LogLevel.Info => "INFO",
        LogLevel.Debug => "DBG ",
        _ => "TRC ",
    };

    private static string CategoryName(LogCategory category)
    {
        // Callers pass a single flag; if several are combined, name the lowest set bit.
        return category switch
        {
            LogCategory.Hid => "Hid",
            LogCategory.Mapping => "Mapping",
            LogCategory.Haptics => "Haptics",
            LogCategory.Osk => "Osk",
            LogCategory.Owner => "Owner",
            LogCategory.Mode => "Mode",
            LogCategory.Pipe => "Pipe",
            LogCategory.Frame => "Frame",
            LogCategory.Counters => "Counter",
            LogCategory.Session => "Session",
            _ => "-",
        };
    }

    private void Emit(string text, bool trailingNewline)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (trailingNewline)
                {
                    _writer?.WriteLine(text);
                    _bytesWritten += text.Length + 2;
                }
                else
                {
                    _writer?.Write(text);
                    _bytesWritten += text.Length;
                }

                if (_bytesWritten >= _maxBytes)
                {
                    Rotate();
                }
            }
            catch (IOException)
            {
                // A log that cannot be written must never take the app down.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (AlsoConsole)
        {
            try { Console.Write(trailingNewline ? text + Environment.NewLine : text); }
            catch { }
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void Rotate()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            var backup = _path + ".1";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
            if (File.Exists(_path))
            {
                File.Move(_path, backup);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Open(truncate: true);
        _bytesWritten = 0;

        try
        {
            _writer?.WriteLine($"{Prefix(LogLevel.Info, LogCategory.Session)}Log rotated; previous content is in {System.IO.Path.GetFileName(_path)}.1");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>, except from the constructor.</summary>
    private void Open(bool truncate)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = new StreamWriter(_path, append: !truncate) { AutoFlush = true };
        }
        catch (Exception)
        {
            // Read-only install directory, file locked by another instance: run without a log rather
            // than refusing to start.
            _writer = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { _writer?.Flush(); _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
