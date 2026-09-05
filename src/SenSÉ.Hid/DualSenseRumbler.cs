using HidSharp;
using SenSÉ.Core.Input;

namespace SenSÉ.Hid;

/// <summary>
/// Single writer for DualSense rumble, matched to a controller by its durable identity.
/// </summary>
/// <remarks>
/// Writes over its own HID stream, separate from the input stream the source reads: a game's rumble
/// arrives on the virtual pad and must reach the physical one whatever the input thread is doing, and
/// serialising the two through one stream would couple vibration to the read loop. The input source
/// has already shown a second handle to the same device opens fine; this writer follows the same
/// reopen-with-cooldown pattern as <see cref="TritonHapticSink"/>, so a pad that goes away is simply
/// rediscovered on the next rumble.
///
/// <para>
/// The identity key is the same one the source built its <see cref="ControllerIdentity"/> from
/// (<see cref="ControllerIdentityFactory.FromHidPath"/>), so enumeration finds the very interface
/// this controller is read from — not just "a DualSense" — and two pads rumble each their own. The
/// long-lived durability resolver is also the one the source used, because it is set once process-wide
/// by <c>AttachedControllers</c> before any controller is opened.
/// </para>
///
/// <para>
/// Failures are silent by design. A report the pad rejects produces no feedback and no exception
/// tells us why, so the Bluetooth sequence tag still rotates to keep consecutive reports distinct, and
/// writes are dropped when the device cannot be opened or has vanished. Getting this wrong cannot
/// break the input path: this class never touches the stream the source reads.
/// </para>
/// </remarks>
public sealed class DualSenseRumbler : IAsyncDisposable
{
    private const int ReopenCooldownMs = 2000;

    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HidStream? _stream;
    private bool _bluetooth;
    private byte _sequence;
    private bool _hasAttemptedOpen;
    private int _lastOpenAttemptTick;
    private bool _disposed;

    public DualSenseRumbler(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>Whether the write stream is currently open. Diagnostic only.</summary>
    public bool IsDeviceOpen => _stream is not null;

    /// <summary>
    /// Makes the DualSense whose identity is <paramref name="identityId"/> vibrate.
    /// </summary>
    /// <param name="identityId">The controller's durable identity, from its source's identity.</param>
    /// <param name="leftMotor">Low-frequency (strong) motor, 0 to 1.</param>
    /// <param name="rightMotor">High-frequency (weak) motor, 0 to 1.</param>
    /// <remarks>
    /// Returns without doing anything when the pad is absent or cooling down: rumble is a best-effort
    /// effect and none of those states is an error the caller can act on.
    /// </remarks>
    public async ValueTask RumbleAsync(
        string identityId,
        double leftMotor,
        double rightMotor,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!EnsureOpen(identityId))
            {
                return;
            }

            var strong = ToStrength(leftMotor);
            var weak = ToStrength(rightMotor);

            // The sequence tag exists only over Bluetooth; over USB the short report is used as-is.
            var report = _bluetooth
                ? DualSenseOutputReport.Rumble(bluetooth: true, weak, strong, NextBluetoothSequence())
                : DualSenseOutputReport.Rumble(bluetooth: false, weak, strong);

            try
            {
                _stream!.Write(report);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                _log?.Invoke($"DualSense rumble: write failed ({exception.GetType().Name}: {exception.Message}); dropping the stream.");
                CloseStream();
            }
        }
        finally
        {
            _gate.Release();
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
            await _gate.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Nothing left to guard.
        }

        CloseStream();
        _gate.Dispose();
    }

    private static byte ToStrength(double amplitude)
        => (byte)Math.Round(Math.Clamp(amplitude, 0.0, 1.0) * byte.MaxValue);

    private byte NextBluetoothSequence()
    {
        _sequence = (byte)((_sequence + 1) & 0x0F);
        return _sequence;
    }

    /// <summary>
    /// Caller must hold <see cref="_gate"/>. Returns false instead of throwing when no controller
    /// matches: an absent pad is an expected state, not an error.
    /// </summary>
    private bool EnsureOpen(string identityId)
    {
        if (_stream is not null)
        {
            return true;
        }

        // Discovery enumerates every DualSense interface; without a cooldown a disconnected pad would
        // make each rumble frame pay for a full enumeration. The "never attempted" state is a flag,
        // never a sentinel tick value — a sentinel overflowed in TritonHapticSink and read as
        // "still cooling down" forever.
        var now = Environment.TickCount;
        if (_hasAttemptedOpen && now - _lastOpenAttemptTick < ReopenCooldownMs)
        {
            return false;
        }
        _lastOpenAttemptTick = now;
        _hasAttemptedOpen = true;

        try
        {
            var device = FindGamepadInterface(identityId);
            if (device is null)
            {
                _log?.Invoke($"DualSense rumble: no interface for {identityId}; rumble is inert.");
                return false;
            }

            if (!device.TryOpen(out var stream))
            {
                // Another process holding the device exclusively is the usual cause, and naming it
                // matters: otherwise this is indistinguishable from the pad being absent.
                _log?.Invoke($"DualSense rumble: TryOpen FAILED on {device.DevicePath}; it may be held by another process.");
                return false;
            }

            _stream = stream;
            _stream.WriteTimeout = 250;
            _bluetooth = IsBluetoothPath(device.DevicePath);
            _log?.Invoke($"DualSense rumble: opened for {identityId} over {(IsBluetoothPath(device.DevicePath) ? "Bluetooth" : "USB")}.");
            return true;
        }
        catch (Exception exception)
        {
            _log?.Invoke($"DualSense rumble: open threw {exception.GetType().Name}: {exception.Message}");
            CloseStream();
            return false;
        }
    }

    /// <summary>
    /// The gamepad interface of the controller with the given identity.
    /// </summary>
    /// <remarks>
    /// A DualSense exposes several HID collections. The same grouping rule as the reader applies:
    /// interfaces sharing the identity are one controller, and the gamepad is the one carrying the
    /// axes, recognised here as the longest input report.
    /// </remarks>
    private static HidDevice? FindGamepadInterface(string identityId)
    {
        foreach (var group in DualSenseControllerSource.Discover()
                     .GroupBy(d => ControllerIdentityFactory.FromHidPath(d.DevicePath)))
        {
            if (group.Key != identityId)
            {
                continue;
            }

            return group.OrderByDescending(d => d.GetMaxInputReportLength()).First();
        }

        return null;
    }

    /// <inheritdoc cref="DualSenseControllerSource"/>
    /// <remarks>
    /// Over Bluetooth the path reads <c>VID&amp;0002054C</c> — an ampersand instead of an underscore,
    /// with a <c>0002</c> prefix from the Bluetooth transport — while over USB it reads
    /// <c>VID_054C</c>. Getting it wrong is safe: at worst the wrong report form is sent and the pad
    /// ignores it.
    /// </remarks>
    private static bool IsBluetoothPath(string devicePath)
        => devicePath.Contains("VID&0002", StringComparison.OrdinalIgnoreCase);

    private void CloseStream()
    {
        try { _stream?.Dispose(); } catch { }
        _stream = null;
    }
}
