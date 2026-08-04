using HidSharp;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Hid;

/// <summary>
/// Finds and reads a PlayStation 5 DualSense over HID.
/// </summary>
/// <remarks>
/// A DualSense is not an XInput device and cannot be made into one: XInput enumerates
/// Xbox-compatible pads only. Windows presents it as an ordinary HID gamepad, so it is read here
/// and decoded by <see cref="DualSenseReportParser"/>.
///
/// <para>
/// The vendor id is matched numerically rather than by searching the device path for text. Over
/// Bluetooth the path reads <c>VID&amp;0002054C</c> — an ampersand instead of an underscore, and a
/// <c>0002</c> prefix from the Bluetooth transport — while over USB it reads <c>VID_054C</c>.
/// Matching the string cost this project a wrong diagnosis: a filter looking for <c>VID_054C</c>
/// found nothing on a machine where the pad was connected and working, and the absence was reported
/// as the pad not existing. HidSharp already parses the id; there is no reason to re-derive it from
/// the path.
/// </para>
/// </remarks>
public sealed class DualSenseControllerSource : IPhysicalControllerSource
{
    /// <summary>Sony Interactive Entertainment.</summary>
    public const int SonyVendorId = 0x054C;

    /// <summary>DualSense (PS5). The Edge reports a different product id and is not handled here.</summary>
    public const int DualSenseProductId = 0x0CE6;

    private readonly string? _devicePath;
    private readonly int _readTimeoutMs;
    private readonly Action<string>? _log;

    /// <param name="devicePath">A specific interface, or null to take the first DualSense found.</param>
    /// <param name="readTimeoutMs">
    /// How long a read may block. A timeout is not an error: a pad nobody is touching sends nothing,
    /// and the loop simply asks again.
    /// </param>
    public DualSenseControllerSource(string? devicePath = null, int readTimeoutMs = 20, Action<string>? log = null)
    {
        _devicePath = devicePath;
        _readTimeoutMs = readTimeoutMs;
        _log = log;
    }

    /// <summary>Every DualSense interface Windows currently exposes.</summary>
    /// <remarks>
    /// A DualSense exposes more than one HID interface. Only the ones carrying an input report long
    /// enough to hold the axes are returned; the others are control channels that would open
    /// successfully and then never produce a frame — which looks exactly like a pad that does not
    /// work.
    /// </remarks>
    public static IReadOnlyList<HidDevice> Discover(Action<string>? log = null)
    {
        try
        {
            var found = DeviceList.Local
                .GetHidDevices(SonyVendorId, DualSenseProductId)
                .Where(d => d.GetMaxInputReportLength() >= 11)
                .ToList();

            log?.Invoke($"DualSense interfaces found: {found.Count}.");

            return found;
        }
        catch (Exception ex)
        {
            log?.Invoke($"enumerating DualSense devices: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var device = _devicePath is null
            ? Discover(_log).FirstOrDefault()
            : Discover(_log).FirstOrDefault(d => d.DevicePath == _devicePath);

        if (device is null)
        {
            _log?.Invoke("No DualSense interface to open.");
            yield break;
        }

        if (!device.TryOpen(out HidStream stream))
        {
            // Another process holding it exclusively is the usual cause, and it is worth naming:
            // otherwise this is indistinguishable from the pad being absent.
            _log?.Invoke($"Unable to open DualSense {device.DevicePath}; it may be held by another process.");
            yield break;
        }

        var start = DateTimeOffset.UtcNow;
        var lastButtons = SteamControllerButtons.None;
        var lastLength = -1;
        var lastStick = (0, 0, 0, 0);

        using (stream)
        {
            stream.ReadTimeout = _readTimeoutMs;
            var buffer = new byte[Math.Max(11, device.GetMaxInputReportLength())];

            while (!cancellationToken.IsCancellationRequested)
            {
                int read;

                try
                {
                    read = await Task.Run(() => stream.Read(buffer), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Nobody is touching the pad. Not a fault, and not a reason to end the stream.
                    continue;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    _log?.Invoke($"DualSense read ended: {ex.GetType().Name}: {ex.Message}");
                    yield break;
                }

                if (read <= 0)
                {
                    continue;
                }

                // Null for the feature and audio reports a DualSense also emits on this pipe.
                if (DualSenseReportParser.Parse(
                        buffer.AsSpan(0, read), DateTimeOffset.UtcNow - start) is { } state)
                {
                    // Raw bytes beside the decoded result, once per change rather than per frame.
                    // "The mapping is wrong" cannot be acted on: it does not say which button, nor
                    // whether the byte was misread or mapped to the wrong Xbox equivalent. One press
                    // logged like this says both, and ends the guessing this project has already
                    // paid for several times over.
                    // Sticks as well as buttons. Logging only on button change never captured a
                    // stick push at all, so "the stick does nothing" and "the stick moves and the
                    // mapper ignores it" stayed indistinguishable. Quantised to a sixteenth so a
                    // resting thumb does not produce a line per frame.
                    var stickStep = (
                        (int)(state.LeftStick.X * 16), (int)(state.LeftStick.Y * 16),
                        (int)(state.RightStick.X * 16), (int)(state.RightStick.Y * 16));

                    if (state.Buttons != lastButtons || read != lastLength || stickStep != lastStick)
                    {
                        lastButtons = state.Buttons;
                        lastLength = read;
                        lastStick = stickStep;

                        _log?.Invoke(
                            $"DualSense report id=0x{buffer[0]:X2} len={read} "
                            + $"bytes=[{string.Join(" ", buffer.Take(Math.Min(read, 12)).Select(b => b.ToString("X2")))}] "
                            + $"=> {state.Buttons} "
                            + $"L=({state.LeftStick.X:F2},{state.LeftStick.Y:F2}) "
                            + $"R=({state.RightStick.X:F2},{state.RightStick.Y:F2})");
                    }

                    yield return state;
                }
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
