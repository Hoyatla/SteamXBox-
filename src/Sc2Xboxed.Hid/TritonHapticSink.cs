using HidSharp;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Hid;

/// <summary>
/// Single writer for controller haptics. All haptic sources (Xbox rumble feedback,
/// overlay keyboard ticks) must go through one instance: concurrent writes to the
/// same HID stream interleave and corrupt reports.
/// </summary>
public sealed class TritonHapticSink : IHapticSink
{
    private const int ReopenCooldownMs = 2000;

    private readonly SteamHidDiscovery _discovery;
    private readonly TritonHapticReportBuilder _reportBuilder;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private HidStream? _stream;
    private int _outputReportLength = 65;
    private int _lastOpenAttemptTick;
    private bool _hasAttemptedOpen;
    private bool _disposed;

    /// <summary>
    /// When set, every submission is dropped without touching the device. Used while
    /// Steam owns the controller so SteamXBox never writes to a device it has released.
    /// </summary>
    public bool Muted { get; set; }

    private readonly Action<string>? _log;

    public TritonHapticSink()
        : this(new SteamHidDiscovery(), new TritonHapticReportBuilder())
    {
    }

    public TritonHapticSink(SteamHidDiscovery discovery, TritonHapticReportBuilder reportBuilder, Action<string>? log = null)
    {
        _discovery = discovery;
        _reportBuilder = reportBuilder;
        _log = log;
    }

    /// <summary>
    /// Whether the HID stream is currently open. Read without synchronising: this is diagnostic only,
    /// and taking the write gate here could block a log line behind a device write.
    /// </summary>
    public bool IsDeviceOpen => _stream is not null;

    public async ValueTask SubmitAsync(HapticOutputFrame frame, CancellationToken cancellationToken)
    {
        if (_disposed || Muted)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!EnsureOpen())
            {
                return;
            }

            foreach (var command in frame.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryWrite(_reportBuilder.Build(command, _outputReportLength)))
                {
                    return;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask SubmitPowerOffAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!EnsureOpen())
            {
                return;
            }

            var report = new byte[Math.Max(2, _outputReportLength)];
            report[0] = 0x9F;
            report[1] = 0x01;
            TryWrite(report);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _writeGate.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Nothing left to guard.
        }

        CloseStream();
        _writeGate.Dispose();
    }

    /// <summary>
    /// Drops the HID stream so the next submission reopens it. Called when the device
    /// is handed to another owner, or after a write failure.
    /// </summary>
    public void Reset()
    {
        _writeGate.Wait();
        try
        {
            CloseStream();
            _lastOpenAttemptTick = int.MinValue;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Caller must hold <see cref="_writeGate"/>.</summary>
    private bool TryWrite(byte[] report)
    {
        try
        {
            _stream!.Write(report);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            _log?.Invoke($"haptics: write failed ({exception.GetType().Name}: {exception.Message}); dropping the stream.");
            CloseStream();
            return false;
        }
    }

    /// <summary>
    /// Caller must hold <see cref="_writeGate"/>. Returns false instead of throwing when no
    /// controller is present: a missing device is an expected state, not an error.
    /// </summary>
    private bool EnsureOpen()
    {
        if (_stream is not null)
        {
            return true;
        }

        // Discovery enumerates every Valve HID interface; without a cooldown a disconnected
        // controller would make each haptic frame pay for a full enumeration.
        //
        // The "never attempted" state must be a flag, not a sentinel tick value: seeding the tick
        // with int.MinValue made the first subtraction overflow to a negative number, which read as
        // "still cooling down" forever and silently disabled every haptic in the app.
        int now = Environment.TickCount;
        if (_hasAttemptedOpen && now - _lastOpenAttemptTick < ReopenCooldownMs)
        {
            return false;
        }
        _lastOpenAttemptTick = now;
        _hasAttemptedOpen = true;

        try
        {
            var device = _discovery.FindPreferredControllerDevice();
            if (device is null)
            {
                _log?.Invoke("haptics: no preferred controller device found; haptics are inert.");
                return false;
            }

            if (!device.TryOpen(out var stream))
            {
                // The input source already holds this device; if Windows refuses a second handle,
                // haptics can never work and that must be visible rather than silent.
                _log?.Invoke($"haptics: TryOpen FAILED on PID=0x{device.ProductID:X4}; haptics are inert.");
                return false;
            }

            _stream = stream;
            _outputReportLength = Math.Max(7, device.GetMaxOutputReportLength());
            _stream.WriteTimeout = 250;
            _log?.Invoke($"haptics: device opened, PID=0x{device.ProductID:X4} outputReportLength={_outputReportLength}");
            return true;
        }
        catch (Exception exception)
        {
            _log?.Invoke($"haptics: open threw {exception.GetType().Name}: {exception.Message}");
            CloseStream();
            return false;
        }
    }

    private void CloseStream()
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null;
    }
}
