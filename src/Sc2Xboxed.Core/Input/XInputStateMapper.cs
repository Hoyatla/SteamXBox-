namespace Sc2Xboxed.Core.Input;

/// <summary>An XInput frame, as the driver reports it.</summary>
/// <param name="Buttons">Packed button bits, XInput's own layout.</param>
/// <param name="LeftTrigger">0 to 255.</param>
/// <param name="RightTrigger">0 to 255.</param>
/// <param name="LeftThumbX">-32768 to 32767.</param>
/// <param name="LeftThumbY">-32768 to 32767.</param>
/// <param name="RightThumbX">-32768 to 32767.</param>
/// <param name="RightThumbY">-32768 to 32767.</param>
public readonly record struct XInputFrame(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY);

/// <summary>
/// Turns an XInput frame into the state the rest of SteamXBox already understands.
/// </summary>
/// <remarks>
/// Kept apart from the reader so it can be tested without a controller: the reader is a P/Invoke
/// that needs real hardware, this is arithmetic and a lookup table. Every mistake worth making here
/// — an inverted axis, a swapped button, an off-by-one on the raw range — is invisible at runtime
/// and obvious in a test.
///
/// Only the buttons every Xbox controller has are mapped. The Steam Controller's grip paddles
/// (L4, R4, L5, R5) and its Steam and Quick Access buttons have no counterpart and stay unset,
/// rather than being approximated onto something that would then fire unexpectedly.
/// </remarks>
public static class XInputStateMapper
{
    /// <summary>Full scale of an XInput thumb axis.</summary>
    private const double AxisScale = 32767.0;

    /// <summary>Full scale of an XInput trigger.</summary>
    private const double TriggerScale = 255.0;

    // XInput's own bit layout, from the Windows headers.
    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort DPadLeft = 0x0004;
    private const ushort DPadRight = 0x0008;
    private const ushort Start = 0x0010;
    private const ushort Back = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;

    /// <summary>The Xbox button. Only ever set when the state came from XInputGetStateEx.</summary>
    /// <remarks>
    /// The documented XInputGetState masks this bit out, so it reads as zero there and the mapping
    /// below simply never fires — no special case needed for a machine where the undocumented export
    /// could not be resolved.
    /// </remarks>
    private const ushort Guide = 0x0400;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    public static ControllerState Map(XInputFrame frame, TimeSpan timestamp)
    {
        return new ControllerState(
            timestamp,
            MapButtons(frame.Buttons),
            Stick(frame.LeftThumbX, frame.LeftThumbY),
            Stick(frame.RightThumbX, frame.RightThumbY),
            frame.LeftTrigger / TriggerScale,
            frame.RightTrigger / TriggerScale,

            // No trackpads on an Xbox controller. Reported as released rather than as a pad resting
            // at the centre, which would read as a finger held still in the middle.
            TouchpadSample.Released,
            TouchpadSample.Released);
    }

    /// <summary>
    /// Maps the buttons an Xbox controller and a Steam Controller share.
    /// </summary>
    /// <remarks>
    /// Start becomes Menu and Back becomes View, matching what the two controllers call the same
    /// physical role. That pairing has to agree with the Xbox output mapping, which sends Menu back
    /// out as Back — otherwise a round trip through SteamXBox would swap the two.
    /// </remarks>
    public static SteamControllerButtons MapButtons(ushort buttons)
    {
        var result = SteamControllerButtons.None;

        if ((buttons & A) != 0) result |= SteamControllerButtons.A;
        if ((buttons & B) != 0) result |= SteamControllerButtons.B;
        if ((buttons & X) != 0) result |= SteamControllerButtons.X;
        if ((buttons & Y) != 0) result |= SteamControllerButtons.Y;

        if ((buttons & LeftShoulder) != 0) result |= SteamControllerButtons.LeftBumper;
        if ((buttons & RightShoulder) != 0) result |= SteamControllerButtons.RightBumper;

        if ((buttons & LeftThumb) != 0) result |= SteamControllerButtons.LeftStick;
        if ((buttons & RightThumb) != 0) result |= SteamControllerButtons.RightStick;

        if ((buttons & Start) != 0) result |= SteamControllerButtons.Menu;
        if ((buttons & Back) != 0) result |= SteamControllerButtons.View;

        // The Xbox button becomes Steam, the flag a Steam Controller's Steam button produces, so it
        // reaches the launcher already wired to it instead of through a second path.
        //
        // It is NOT the mode-switch button, and turning it into one is a regression this line has
        // already seen once, on 14 August. Launching Steam is what this button is for on this family;
        // taking it for the switch takes the launcher away to solve a problem that belongs to
        // InputModeHandler, where the L3+R3 hold lives.
        if ((buttons & Guide) != 0) result |= SteamControllerButtons.Steam;

        if ((buttons & DPadUp) != 0) result |= SteamControllerButtons.DPadUp;
        if ((buttons & DPadDown) != 0) result |= SteamControllerButtons.DPadDown;
        if ((buttons & DPadLeft) != 0) result |= SteamControllerButtons.DPadLeft;
        if ((buttons & DPadRight) != 0) result |= SteamControllerButtons.DPadRight;

        return result;
    }

    /// <summary>
    /// Normalises a thumb axis pair to -1..1.
    /// </summary>
    /// <remarks>
    /// Divided by 32767 and then clamped, not by 32768. The negative end of the range reaches
    /// -32768, which would otherwise normalise to slightly beyond -1 and let a magnitude exceed 1 —
    /// enough to push a curve or a dead-zone rescale outside the range they were written for.
    ///
    /// No dead zone is applied here. That belongs to whatever consumes the stick, which already has
    /// one and knows whether it is driving a pointer or a game.
    /// </remarks>
    private static NormalizedStick Stick(short x, short y)
        => new(
            Math.Clamp(x / AxisScale, -1.0, 1.0),
            Math.Clamp(y / AxisScale, -1.0, 1.0));
}
