namespace Sc2Xboxed.Core.Input;

/// <summary>
/// What a button is called on the controller in the user's hands.
/// </summary>
/// <remarks>
/// The internal value never changes: a DualSense circle and an Xbox B produce the same
/// <see cref="SteamControllerButtons"/> and the same virtual gamepad output, because they are the
/// same button in the same place. Only the printed name differs.
///
/// <para>
/// That difference is worth carrying all the way to the screen. Editing a binding labelled "B"
/// while holding a pad marked with a circle forces a translation on every row, and a setting the
/// user has to translate is one they will eventually get wrong — or, worse, one they will believe is
/// broken when it is correct.
/// </para>
///
/// <para>
/// The Xbox names are the fallback rather than a fourth table, because they are what the rest of
/// SteamXBox speaks and what an unrecognised pad is most likely to be.
/// </para>
/// </remarks>
public static class ControllerButtonLabels
{
    /// <summary>The name printed on the pad, with the Xbox equivalent when it differs.</summary>
    /// <remarks>
    /// Both, not just the PlayStation name. The user is editing a mapping whose output is an Xbox
    /// button, so hiding that would trade one translation for another — they would know which button
    /// they are pressing and no longer which one the game receives.
    /// </remarks>
    public static string Describe(ControllerKind kind, SteamControllerButtons button)
    {
        var native = For(kind, button);
        var xbox = For(ControllerKind.XInput, button);

        return native == xbox ? native : $"{native} ({xbox})";
    }

    /// <summary>The name printed on the pad.</summary>
    public static string For(ControllerKind kind, SteamControllerButtons button)
        => kind == ControllerKind.DualSense ? PlayStation(button) : Xbox(button);

    private static string PlayStation(SteamControllerButtons button) => button switch
    {
        // By position, which is what the thumb knows: cross is where A is, circle where B is.
        SteamControllerButtons.A => "Croix",
        SteamControllerButtons.B => "Rond",
        SteamControllerButtons.X => "Carré",
        SteamControllerButtons.Y => "Triangle",
        SteamControllerButtons.LeftBumper => "L1",
        SteamControllerButtons.RightBumper => "R1",
        SteamControllerButtons.LeftStick => "L3",
        SteamControllerButtons.RightStick => "R3",
        SteamControllerButtons.Menu => "Options",
        SteamControllerButtons.View => "Create",
        _ => Xbox(button),
    };

    private static string Xbox(SteamControllerButtons button) => button switch
    {
        SteamControllerButtons.A => "A",
        SteamControllerButtons.B => "B",
        SteamControllerButtons.X => "X",
        SteamControllerButtons.Y => "Y",
        SteamControllerButtons.LeftBumper => "LB",
        SteamControllerButtons.RightBumper => "RB",
        SteamControllerButtons.LeftStick => "L3",
        SteamControllerButtons.RightStick => "R3",
        SteamControllerButtons.Menu => "Menu",
        SteamControllerButtons.View => "View",
        SteamControllerButtons.DPadUp => "Croix dir. ↑",
        SteamControllerButtons.DPadDown => "Croix dir. ↓",
        SteamControllerButtons.DPadLeft => "Croix dir. ←",
        SteamControllerButtons.DPadRight => "Croix dir. →",
        _ => button.ToString(),
    };
}
