using HidSharp;
using SenSÉ.Core.Input;
using SenSÉ.Core.Runtime;
using SenSÉ.Windows;

namespace SenSÉ.Hid;

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
public sealed class DualSenseControllerSource : IPhysicalControllerSource, IPowerControl
{
    /// <summary>Sony Interactive Entertainment.</summary>
    public const int SonyVendorId = 0x054C;

    /// <summary>DualSense (PS5). The Edge reports a different product id and is not handled here.</summary>
    public const int DualSenseProductId = 0x0CE6;

    /// <summary>
    /// How long an unchanging controller may stay silent before a frame is sent anyway.
    /// </summary>
    /// <remarks>
    /// Dropping repeated states cuts the load massively, and it broke the power-off chord outright:
    /// Menu + View has to be <i>held</i> for two seconds, and the detector counts that time frame by
    /// frame. A thumb holding two buttons perfectly still produces identical states, every one of
    /// them was dropped, and the two seconds never accumulated. The same is true of every other
    /// time-based rule here — the mode chord, the trackpad's inertia — which all need to keep being
    /// told that time is passing.
    ///
    /// <para>
    /// So a still controller still speaks, at 20 Hz instead of 800. That is thirty frames inside a
    /// two second hold, plenty for any of them, and one fortieth of the work.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan IdleHeartbeat = TimeSpan.FromMilliseconds(50);

    private readonly string? _devicePath;
    private readonly int _readTimeoutMs;
    private readonly Action<string>? _log;

    private readonly object _stateGate = new();
    private HidStream? _activeStream;
    private object? _activeStreamGate;
    private bool _isBluetooth;

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

        // The last state handed out, timestamp stripped, so a repeat can be recognised.
        ControllerState previous = default;

        // Null until the first frame, never a sentinel. TimeSpan.MinValue stood here and the very
        // first comparison — state.Timestamp minus it — overflowed TimeSpan.MaxValue and threw,
        // killing the reader on its first report. The pad then looked slow and unreliable when in
        // fact its source was dying and restarting.
        TimeSpan? lastYielded = null;

        using (stream)
        {
            stream.ReadTimeout = _readTimeoutMs;
            var buffer = new byte[Math.Max(11, device.GetMaxInputReportLength())];
            var streamGate = new object();

            // Held so <see cref="SendPowerOff"/> can write through the same stream the reads use.
            // A feature report is a different pipe from the input report, but serialising them is
            // what the Steam source does, and two writers on one handle is a race nobody needs.
            lock (_stateGate)
            {
                _activeStream = stream;
                _activeStreamGate = streamGate;
                _isBluetooth = IsBluetoothPath(device.DevicePath);
            }

            if (RequestFullBluetoothReports && IsBluetoothPath(device.DevicePath))
            {
                AskForFullReports(device, stream);
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read;

                    try
                    {
                        read = await Task.Run(
                                () =>
                                {
                                    lock (streamGate)
                                    {
                                        return stream.Read(buffer);
                                    }
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
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
                        // Buttons and report shape only. The sticks were in this condition too, at a
                        // sixteenth of travel, which sounded coarse enough — a moving thumb crosses
                        // a sixteenth constantly, so it produced a thirty-two byte dump per frame at
                        // two hundred frames a second, three thousand lines a minute, each one
                        // flushed to disk. It was the single largest thing in the log and a real
                        // load on the machine.
                        //
                        // Nothing is lost: the per-second counter line already reports each
                        // controller's stick peak, which is what "did the stick move" needs.
                        if (state.Buttons != lastButtons || read != lastLength)
                        {
                            lastButtons = state.Buttons;
                            lastLength = read;

                            _log?.Invoke(
                                $"DualSense report id=0x{buffer[0]:X2} len={read} "
                                + $"bytes=[{string.Join(" ", buffer.Take(Math.Min(read, 12)).Select(b => b.ToString("X2")))}] "
                                + $"=> {state.Buttons} "
                                + $"L=({state.LeftStick.X:F2},{state.LeftStick.Y:F2}) "
                                + $"R=({state.RightStick.X:F2},{state.RightStick.Y:F2})");
                        }

                        // A repeat of the previous state is not news. See the same guard in
                        // TritonSteamControllerSource: the pad reports at its own rate whether
                        // anything moved or not, and each repeat used to run the whole mapper for a
                        // state already handled. Compared on the parsed state with the timestamp
                        // removed — the raw report carries a counter that moves on its own.
                        //
                        // This one earns less than the Steam Controller's: a DualSense stick sits on
                        // analogue noise, so consecutive states genuinely differ by a least
                        // significant bit and are correctly let through. The guard is here for when
                        // the pad is truly still.
                        var current = state with { Timestamp = default };
                        if (previous == current
                            && lastYielded is { } last
                            && state.Timestamp - last < IdleHeartbeat)
                        {
                            continue;
                        }

                        previous = current;
                        lastYielded = state.Timestamp;
                        yield return state;
                    }
                }
            }
            finally
            {
                lock (_stateGate)
                {
                    _activeStream = null;
                    _activeStreamGate = null;
                }
            }
        }
    }

    /// <summary>
    /// The calibration feature report. Reading it is what switches a Bluetooth DualSense out of
    /// compatibility mode; its contents are not used here.
    /// </summary>
    private const byte CalibrationFeatureReportId = 0x05;

    /// <summary>
    /// Off. Asking for full reports works, and costs more than it gives as things stand.
    /// </summary>
    /// <remarks>
    /// Turned on on 12 August and turned off the same night. The request itself succeeds — the pad
    /// switched to <c>0x31</c> and delivered its analogue triggers, its touchpad and its gyroscope,
    /// which is what a DualSense is supposed to give. What came with it was the frame rate: from
    /// about 60 a second to an average of 175 with peaks at 554, taking the whole pipeline to 617,
    /// and severe input lag reported within the hour.
    ///
    /// <para>
    /// The lag is not a full queue — the channel is bounded and drops the oldest, and the counters
    /// show every frame being consumed. So the cost is somewhere else and is not yet understood,
    /// and a controller that lags is worse than one missing its gyroscope. This goes back on when
    /// the cost has been measured, not before.
    /// </para>
    /// </remarks>
    private const bool RequestFullBluetoothReports = false;

    /// <summary>
    /// Asks a Bluetooth DualSense for its full reports instead of the compatibility ones.
    /// </summary>
    /// <remarks>
    /// Connected over Bluetooth, the DualSense does not send its full <c>0x31</c> report until some
    /// host asks for the calibration feature report. Until then it sends <c>0x01</c>: the sticks and
    /// the face buttons, and nothing else — no analogue triggers, no touchpad, no gyroscope. Worse
    /// for diagnosis, that report is sent on change rather than on a clock, so the frame rate follows
    /// how hard the thumbs are working. Measured on 12 August before this existed: an average of 196
    /// frames a second swinging between 8 and 541, against a steady 64 for every other pad, and every
    /// report logged as <c>id=0x01 len=78</c> with its payload ending at byte seven.
    ///
    /// <para>
    /// <b>That rate description did not hold up.</b> Measured again on the bench on 15 August, same
    /// pad, compatibility mode: a steady <b>600 reports a second</b> whatever is happening — 599 with
    /// the pad lying untouched on a table, 591 with a stick pushed against its stop and held still for
    /// twelve seconds, worst gap 20 ms across thirteen manoeuvres. The per-frame counter advances on
    /// every one of them, so they are the pad's own reports and not Windows replaying a buffer. What
    /// differed between the two measurements is the Bluetooth link itself, which is the one thing
    /// neither reading controlled for. Whatever makes the DualSense pointer stutter, it is not this
    /// source going quiet.
    /// </para>
    ///
    /// <para>
    /// The read is the whole point — the calibration values are discarded. Wired pads never come here:
    /// over USB the full report is what arrives from the first frame.
    /// </para>
    ///
    /// <para>
    /// Best effort by design. Windows does not reliably carry feature reports over Bluetooth HID — the
    /// same limitation <see cref="SendPowerOff"/> documents — so a refusal is reported and the pad
    /// keeps working exactly as it did before, reduced but alive. Failing to ask is not a reason to
    /// drop a controller.
    /// </para>
    /// </remarks>
    private void AskForFullReports(HidDevice device, HidStream stream)
    {
        try
        {
            var length = device.GetMaxFeatureReportLength();

            if (length <= 0)
            {
                _log?.Invoke("DualSense: no feature report length advertised; staying in compatibility mode.");
                return;
            }

            var feature = new byte[length];
            feature[0] = CalibrationFeatureReportId;

            stream.GetFeature(feature);

            _log?.Invoke("DualSense: full Bluetooth reports requested (calibration feature read). "
                         + "Expect report id 0x31 from here.");
        }
        catch (Exception failure)
        {
            _log?.Invoke($"DualSense: could not ask for full reports ({failure.GetType().Name}: {failure.Message}). "
                         + "The pad stays in compatibility mode: sticks and face buttons only.");
        }
    }

    /// <summary>
    /// Asks the controller to power off. Bluetooth only: a wired DualSense is powered by the cable.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing when there is no open stream (the pad is asleep or gone),
    /// when the pad is on USB, where the command has no meaning, or when Windows refuses every
    /// means of switching it off.
    ///
    /// <para>
    /// The Bluetooth control report is the one proven to work on Linux — the same feature report
    /// <c>dualsensectl</c> sends, built at the 48-byte length Windows presents the report at and
    /// checksummed with the <c>0xA3</c> seed the controller verifies. Windows does not reliably
    /// deliver feature reports over Bluetooth HID, though: HidD_SetFeature can refuse them outright
    /// (error 0). When the report is refused, the Bluetooth link itself is cut through
    /// <see cref="BluetoothLink"/> and the controller powers itself off, which is the behaviour
    /// this machine is expected to land on.
    /// </para>
    /// </remarks>
    public bool SendPowerOff()
    {
        lock (_stateGate)
        {
            if (_activeStream is null || _activeStreamGate is null)
            {
                return false;
            }

            if (!_isBluetooth)
            {
                _log?.Invoke("DualSense power-off skipped: it only works over Bluetooth, and this pad is wired.");
                return false;
            }

            // Through the guard, not straight to the link. BluetoothLink.Cut takes any interface
            // path, so calling it directly leaves the one general-purpose way to disconnect any
            // Bluetooth device on the machine reachable from ordinary code. Everything goes through
            // DualSenseShutdown so that stays the only door — including this class, which has no
            // more right to the shortcut than anyone else.
            //
            // The identity is built here because this source is a DualSense by construction; the
            // guard is there to stop somebody else pointing the same mechanism at a headset.
            var path = _devicePath ?? "";
            var identity = new ControllerIdentity(
                ControllerKind.DualSense,
                ControllerIdentityFactory.FromHidPath(path),
                "Manette PS5",
                Slot: -1);

            if (DualSenseShutdown.PowerOff(identity, path, _log))
            {
                _log?.Invoke("DualSense Bluetooth link cut; the controller is powering itself off.");
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Whether the device path names a Bluetooth transport.
    /// </summary>
    /// <remarks>
    /// Over Bluetooth the path reads <c>VID&amp;0002054C</c> — an ampersand instead of an
    /// underscore, with a <c>0002</c> prefix from the Bluetooth transport — while over USB it reads
    /// <c>VID_054C</c>. Matching the string is what this file already documents the difference as,
    /// and getting it wrong is safe: at worst the pad is left on and told so.
    /// </remarks>
    private static bool IsBluetoothPath(string devicePath)
        => devicePath.Contains("VID&0002", StringComparison.OrdinalIgnoreCase);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
