using HidSharp;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Hid;

public sealed class TritonSteamControllerSource : IPhysicalControllerSource, INativeLayerControl, IPowerControl, ITrackpadInput
{
    private readonly SteamHidDiscovery _discovery;
    private readonly TritonInputReportParser _parser;
    private readonly int _readTimeoutMs;
    private readonly bool _manageNativeLayer;
    private readonly object _stateGate = new();
    private bool _desiredNativeLayerEnabled;
    private bool? _appliedNativeLayerEnabled;
    private HidStream? _activeStream;
    private object? _activeStreamGate;
    private int _outputReportLength = 65;
    private SteamControllerLizardModeHeartbeat? _heartbeat;

    public TritonSteamControllerSource()
        : this(
            new SteamHidDiscovery(),
            new TritonInputReportParser(),
            readTimeoutMs: 20,
            manageNativeLayer: true,
            initialNativeLayerEnabled: false,
            log: null)
    {
    }

    public TritonSteamControllerSource(
        SteamHidDiscovery discovery,
        TritonInputReportParser parser,
        int readTimeoutMs,
        bool manageNativeLayer,
        bool initialNativeLayerEnabled,
        Action<string>? log = null)
    {
        _discovery = discovery;
        _parser = parser;
        _readTimeoutMs = readTimeoutMs;
        _manageNativeLayer = manageNativeLayer;
        _desiredNativeLayerEnabled = initialNativeLayerEnabled;
    }

    public ValueTask SetNativeLayerEnabledAsync(bool enabled)
    {
        lock (_stateGate)
        {
            _desiredNativeLayerEnabled = enabled;
            ApplyNativeLayerStateIfNeeded();
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var device = _discovery.FindPreferredControllerDevice()
            ?? throw new InvalidOperationException("No known Valve Steam Controller HID interface was found.");

        if (!device.TryOpen(out HidStream stream))
        {
            throw new IOException($"Unable to open HID device {device.DevicePath}.");
        }

        using (stream)
        {
            stream.ReadTimeout = _readTimeoutMs;
            stream.WriteTimeout = 250;
            var buffer = new byte[Math.Max(1, device.GetMaxInputReportLength())];
            var streamGate = new object();

            // The last state handed out, timestamp stripped, so a repeat can be recognised.
            ControllerState previous = default;
            var lastYielded = TimeSpan.MinValue;

            // How long an unchanging controller may stay silent before a frame is sent anyway.
            var IdleHeartbeat = TimeSpan.FromMilliseconds(50);

            try
            {
                lock (_stateGate)
                {
                    _activeStream = stream;
                    _activeStreamGate = streamGate;
                    _outputReportLength = Math.Max(7, device.GetMaxOutputReportLength());
                    ApplyNativeLayerStateIfNeeded();
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = await Task.Run(
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
                        continue;
                    }

                    if (bytesRead <= 0)
                    {
                        continue;
                    }

                    var report = buffer.AsSpan(0, bytesRead);
                    if (_parser.TryParse(report, TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond), out var state))
                    {
                        // A repeat of the previous state is not news. The controller keeps reporting
                        // at its own rate whether anything moved or not — measured at around 800
                        // reports a second — and every one of them used to run the whole profile
                        // mapper, the trackball, the arbiter and the haptics for a state already
                        // handled. That is the machine mobilised to say nothing changed.
                        //
                        // Compared on the parsed state with the timestamp removed, never on the raw
                        // bytes: the report carries a packet counter that moves on its own, so byte
                        // equality would match nothing. And never on a judgement about what the
                        // frame "means" — an earlier attempt at filtering by intent silenced the
                        // pipe completely. Equal states are equal; there is nothing to interpret.
                        // Still speaking when still: a held chord, the mode switch and the trackpad's
                        // inertia are all counted frame by frame, so a controller that goes silent
                        // stops time for them. Dropping every repeat broke the power-off chord — two
                        // buttons held perfectly still produce identical states, and the two second
                        // hold never accumulated. 20 Hz instead of 800 keeps them all fed.
                        var current = state with { Timestamp = default };
                        if (previous == current && state.Timestamp - lastYielded < IdleHeartbeat)
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
                    _appliedNativeLayerEnabled = null;
                }

                if (_heartbeat is not null)
                {
                    await _heartbeat.DisposeAsync().ConfigureAwait(false);
                    _heartbeat = null;
                }

                if (_manageNativeLayer)
                {
                    SteamControllerLizardMode.Enable(stream, streamGate);
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Asks the controller to power off. Returns false when there is no open stream.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="SteamControllerPowerOff"/>, which uses the feature-report envelope
    /// that the native-layer commands use. The previous implementation wrote a raw output report and
    /// silently did nothing.
    /// </remarks>
    public bool SendPowerOff()
    {
        lock (_stateGate)
        {
            if (_activeStream is null || _activeStreamGate is null)
            {
                return false;
            }

            SteamControllerPowerOff.Send(_activeStream, _activeStreamGate);
            return true;
        }
    }

    public ValueTask SendPowerOffAsync()
    {
        SendPowerOff();
        return ValueTask.CompletedTask;
    }

    private void ApplyNativeLayerStateIfNeeded()
    {
        if (!_manageNativeLayer ||
            _activeStream is null ||
            _activeStreamGate is null ||
            _appliedNativeLayerEnabled == _desiredNativeLayerEnabled)
        {
            return;
        }

        if (_desiredNativeLayerEnabled)
        {
            _heartbeat?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _heartbeat = null;
            SteamControllerLizardMode.Enable(_activeStream, _activeStreamGate);
        }
        else
        {
            SteamControllerLizardMode.Disable(_activeStream, _activeStreamGate);
            _heartbeat ??= new SteamControllerLizardModeHeartbeat(_activeStream, _activeStreamGate);
        }

        _appliedNativeLayerEnabled = _desiredNativeLayerEnabled;
    }
}
