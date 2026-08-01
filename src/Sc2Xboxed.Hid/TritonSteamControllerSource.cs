using HidSharp;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Hid;

public sealed class TritonSteamControllerSource : IPhysicalControllerSource
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

    public async IAsyncEnumerable<SteamControllerState> ReadFramesAsync(
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
