using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.VirtualGamepad;

public sealed class ViGEmXbox360Sink : IVirtualXbox360Sink
{
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _connected;

    public event EventHandler<XboxRumbleFrame>? RumbleReceived;

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_connected)
            {
                return ValueTask.CompletedTask;
            }

            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.AutoSubmitReport = false;
            _controller.FeedbackReceived += OnFeedbackReceived;
            _controller.Connect();
            _connected = true;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SubmitAsync(Xbox360Report report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            EnsureConnected();

            _controller!.SetButtonsFull((ushort)report.Buttons);
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, report.LeftTrigger);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, report.RightTrigger);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, report.LeftThumbX);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, report.LeftThumbY);
            _controller.SetAxisValue(Xbox360Axis.RightThumbX, report.RightThumbX);
            _controller.SetAxisValue(Xbox360Axis.RightThumbY, report.RightThumbY);
            _controller.SubmitReport();
        }

        return ValueTask.CompletedTask;
    }

    public bool IsConnected
    {
        get { lock (_gate) return _connected; }
    }

    /// <summary>
    /// Unplugs the virtual pad. Needed when handing the physical controller to Steam: leaving it
    /// plugged in makes Steam enumerate a phantom Xbox 360 controller alongside the real one, and
    /// games launched from Steam may bind to the phantom. <see cref="ConnectAsync"/> plugs it
    /// back in.
    /// </summary>
    public async ValueTask DisconnectAsync()
    {
        await SubmitNeutralIfConnectedAsync().ConfigureAwait(false);

        lock (_gate)
        {
            if (_controller is not null)
            {
                _controller.FeedbackReceived -= OnFeedbackReceived;
                if (_connected)
                {
                    _controller.Disconnect();
                }
            }

            _controller = null;
            _client?.Dispose();
            _client = null;
            _connected = false;
        }
    }

    public ValueTask DisposeAsync() => DisconnectAsync();

    private ValueTask SubmitNeutralIfConnectedAsync()
    {
        lock (_gate)
        {
            if (!_connected || _controller is null)
            {
                return ValueTask.CompletedTask;
            }
        }

        return SubmitAsync(Xbox360Report.Neutral, CancellationToken.None);
    }

    private void EnsureConnected()
    {
        if (!_connected || _controller is null)
        {
            throw new InvalidOperationException("The virtual Xbox 360 controller is not connected.");
        }
    }

    private void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        RumbleReceived?.Invoke(
            this,
            new XboxRumbleFrame(
                e.LargeMotor / 255.0,
                e.SmallMotor / 255.0));
    }
}
