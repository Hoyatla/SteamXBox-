using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.VirtualGamepad;

/// <summary>
/// A virtual DualShock 4 pad for a DualSense, so a game sees a PlayStation pad with the layout and
/// tuning the user chose.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ViGEmXbox360Sink"/>, for the family that is a DualShock 4. The two share
/// the connection lifecycle; they differ in the report each submits.
///
/// <para>
/// Rumble is forwarded to whoever subscribes to <see cref="RumbleReceived"/>. ViGEm's
/// <see cref="IDualShock4Controller.FeedbackReceived"/> is deprecated, so the raw output report is
/// read instead through <c>AwaitRawOutputReport</c>: a game's feedback reaches the virtual pad as a
/// HID output report, and the rumble bytes are at fixed positions in it. The subscriber — the
/// process that owns the physical DualSense — turns them into vibration on the real controller.
/// </para>
/// </remarks>
public sealed class ViGEmDS4Sink : IVirtualDS4Sink
{
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private IDualShock4Controller? _controller;
    private bool _connected;

    private CancellationTokenSource? _rumbleCancel;
    private Task? _rumbleLoop;

    /// <summary>Raised on the rumble loop's thread for every output report a game sends to this pad.</summary>
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
            _controller = _client.CreateDualShock4Controller();
            _controller.AutoSubmitReport = false;
            _controller.Connect();
            _connected = true;
            _rumbleCancel = new CancellationTokenSource();
            _rumbleLoop = Task.Run(() => RumbleLoop(_rumbleCancel.Token));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SubmitAsync(DS4Report report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            EnsureConnected();

            // Buttons first: the report's bit positions are the ones ViGEm writes, so the bitfield
            // goes straight into the wire report. The d-pad, axes and triggers follow, then the
            // special byte — PlayStation is 0x02 on the wire, not a ViGEm button bit.
            _controller!.SetButtonsFull((ushort)report.Buttons);
            _controller.SetDPadDirection(ToViGEmDpad(report.Dpad));
            _controller.SetAxisValue(DualShock4Axis.LeftThumbX, report.LeftThumbX);
            _controller.SetAxisValue(DualShock4Axis.LeftThumbY, report.LeftThumbY);
            _controller.SetAxisValue(DualShock4Axis.RightThumbX, report.RightThumbX);
            _controller.SetAxisValue(DualShock4Axis.RightThumbY, report.RightThumbY);
            _controller.SetSliderValue(DualShock4Slider.LeftTrigger, report.LeftTrigger);
            _controller.SetSliderValue(DualShock4Slider.RightTrigger, report.RightTrigger);
            _controller.SetSpecialButtonsFull((byte)(report.PlayStation ? 0x02 : 0x00));
            _controller.SubmitReport();
        }

        return ValueTask.CompletedTask;
    }

    public bool IsConnected
    {
        get { lock (_gate) return _connected; }
    }

    /// <summary>Unplugs the virtual pad, for the handovers that must put every pad away.</summary>
    public async ValueTask DisconnectAsync()
    {
        await SubmitNeutralIfConnectedAsync().ConfigureAwait(false);

        lock (_gate)
        {
            _rumbleCancel?.Cancel();
            _rumbleCancel = null;

            if (_controller is not null)
            {
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

        return SubmitAsync(DS4Report.Neutral, CancellationToken.None);
    }

    private void EnsureConnected()
    {
        if (!_connected || _controller is null)
        {
            throw new InvalidOperationException("The virtual DualShock 4 controller is not connected.");
        }
    }

    /// <summary>
    /// Drains the virtual pad's output reports and forwards the rumble in them.
    /// </summary>
    /// <remarks>
    /// The raw buffer is the DualShock 4 USB output report the game sent: report id 0x05 at byte 0,
    /// flags at byte 1, then the small (weak) motor at byte 4 and the big (strong) motor at byte 5 —
    /// the positions the ViGEm driver itself copies into its notification struct. Forwarded on every
    /// report, not only when the motor bytes change: the physical DualSense holds its last commanded
    /// strength, and a game that re-sends an identical rumble is telling us the strength is still
    /// current. The loop reads with a 250 ms timeout so a pad that falls silent does not burn a core.
    /// </remarks>
    private void RumbleLoop(CancellationToken cancellationToken)
    {
        var controller = _controller;
        if (controller is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IEnumerable<byte> report;
                try
                {
                    report = controller.AwaitRawOutputReport(250, out var timedOut);
                    if (timedOut)
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    // The device was unplugged or disposed; the loop has no further business.
                    break;
                }

                var bytes = report as byte[] ?? report.ToArray();
                if (bytes.Length < 6)
                {
                    continue;
                }

                RumbleReceived?.Invoke(
                    this,
                    new XboxRumbleFrame(
                        LeftMotor: bytes[5] / 255.0,
                        RightMotor: bytes[4] / 255.0));
            }
        }
        catch (Exception)
        {
            // Best effort by design; a rumble loop must never take the process down with it.
        }
    }

    private static DualShock4DPadDirection ToViGEmDpad(DS4Dpad dpad) => dpad switch
    {
        DS4Dpad.Up => DualShock4DPadDirection.North,
        DS4Dpad.UpRight => DualShock4DPadDirection.Northeast,
        DS4Dpad.Right => DualShock4DPadDirection.East,
        DS4Dpad.DownRight => DualShock4DPadDirection.Southeast,
        DS4Dpad.Down => DualShock4DPadDirection.South,
        DS4Dpad.DownLeft => DualShock4DPadDirection.Southwest,
        DS4Dpad.Left => DualShock4DPadDirection.West,
        DS4Dpad.UpLeft => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };
}
