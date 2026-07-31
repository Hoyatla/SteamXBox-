using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Osk;

/// <summary>
/// Overlay keyboard haptics. Requests are forwarded to the core process, which owns the only
/// HID stream that writes haptics: a second stream here would interleave with game rumble and
/// corrupt reports. Every method returns immediately so typing latency never depends on HID.
/// </summary>
public sealed class HapticFeedback : IAsyncDisposable
{
    private readonly HapticRequestSender _sender;

    public HapticFeedback(Action<string>? log = null)
    {
        _sender = new HapticRequestSender(log);
        _sender.Start();
    }

    /// <summary>Light tick emitted when the highlighted key changes.</summary>
    public void Hover(HapticActuator actuator)
    {
        var settings = Program.Settings;
        if (!settings.HoverHaptics || !settings.HapticsEnabled)
        {
            return;
        }

        _sender.Submit(new HapticCommand(
            actuator,
            HapticType.Tick,
            GainDb: 0,
            PulseWidthUs: settings.HoverPulseUs));
    }

    /// <summary>
    /// Firmer click emitted when a key is actually sent. Strength is per pad, so each thumb can be
    /// tuned separately or silenced without affecting the other.
    /// </summary>
    public void Press(HapticActuator actuator)
    {
        var isLeftPad = actuator == HapticActuator.LeftTrackpad || actuator == HapticActuator.LeftRumble;
        var pulse = Program.Settings.ClickPulseUsFor(isLeftPad);

        if (pulse == 0)
        {
            return;
        }

        _sender.Submit(new HapticCommand(
            actuator,
            HapticType.Click,
            GainDb: 0,
            PulseWidthUs: pulse));
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
