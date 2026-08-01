using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Which Xbox 360 button each physical Steam Controller button produces in Xbox360 mode.
/// </summary>
/// <remarks>
/// Steam and Quick Access are deliberately absent: they drive SteamXBox itself — launching Steam and
/// switching modes — so letting a profile rebind them would be a way to lock yourself out of the
/// application from the controller.
///
/// <see cref="Default"/> reproduces the mapping that was previously hard-coded, so a profile that
/// changes nothing behaves exactly as before. Note that it also mirrors the four back paddles onto
/// the face buttons (L4→X, R4→Y, L5→A, R5→B), which is what makes them usable at all: the Xbox 360
/// layout has no paddles to send them to.
/// </remarks>
public sealed class XboxButtonMap
{
    /// <summary>Every physical button a profile may rebind, in the order the interface shows them.</summary>
    public static readonly SteamControllerButtons[] LeftSide =
    [
        SteamControllerButtons.LeftBumper,
        SteamControllerButtons.L4,
        SteamControllerButtons.L5,
        SteamControllerButtons.DPadUp,
        SteamControllerButtons.DPadLeft,
        SteamControllerButtons.DPadRight,
        SteamControllerButtons.DPadDown,
        SteamControllerButtons.LeftStick,
        SteamControllerButtons.View,
    ];

    public static readonly SteamControllerButtons[] RightSide =
    [
        SteamControllerButtons.RightBumper,
        SteamControllerButtons.R4,
        SteamControllerButtons.R5,
        SteamControllerButtons.Y,
        SteamControllerButtons.X,
        SteamControllerButtons.B,
        SteamControllerButtons.A,
        SteamControllerButtons.RightStick,
        SteamControllerButtons.Menu,
    ];

    public static IEnumerable<SteamControllerButtons> All => LeftSide.Concat(RightSide);

    private readonly Dictionary<SteamControllerButtons, Xbox360Buttons> _map = [];

    /// <summary>The mapping SteamXBox used before any of this was configurable.</summary>
    public static XboxButtonMap Default
    {
        get
        {
            var map = new XboxButtonMap();

            map[SteamControllerButtons.A] = Xbox360Buttons.A;
            map[SteamControllerButtons.B] = Xbox360Buttons.B;
            map[SteamControllerButtons.X] = Xbox360Buttons.X;
            map[SteamControllerButtons.Y] = Xbox360Buttons.Y;

            map[SteamControllerButtons.LeftBumper] = Xbox360Buttons.LeftShoulder;
            map[SteamControllerButtons.RightBumper] = Xbox360Buttons.RightShoulder;
            map[SteamControllerButtons.LeftStick] = Xbox360Buttons.LeftThumb;
            map[SteamControllerButtons.RightStick] = Xbox360Buttons.RightThumb;

            // Menu is the right-hand hamburger and View the left-hand two-panes icon, so they follow
            // the Xbox convention: Menu is Start, View is Back. Shipped inverted until 3.2; a saved
            // profile keeps whatever it stored, only this default changed.
            map[SteamControllerButtons.Menu] = Xbox360Buttons.Start;
            map[SteamControllerButtons.View] = Xbox360Buttons.Back;

            map[SteamControllerButtons.DPadUp] = Xbox360Buttons.DPadUp;
            map[SteamControllerButtons.DPadDown] = Xbox360Buttons.DPadDown;
            map[SteamControllerButtons.DPadLeft] = Xbox360Buttons.DPadLeft;
            map[SteamControllerButtons.DPadRight] = Xbox360Buttons.DPadRight;

            map[SteamControllerButtons.L4] = Xbox360Buttons.X;
            map[SteamControllerButtons.R4] = Xbox360Buttons.Y;
            map[SteamControllerButtons.L5] = Xbox360Buttons.A;
            map[SteamControllerButtons.R5] = Xbox360Buttons.B;

            return map;
        }
    }

    public Xbox360Buttons this[SteamControllerButtons physical]
    {
        get => _map.TryGetValue(physical, out var output) ? output : Xbox360Buttons.None;
        set => _map[physical] = value;
    }

    /// <summary>Translates a frame's pressed buttons into the Xbox 360 buttons to report.</summary>
    public Xbox360Buttons Apply(SteamControllerButtons pressed)
    {
        var mapped = Xbox360Buttons.None;

        foreach (var physical in All)
        {
            if (pressed.HasFlag(physical))
            {
                mapped |= this[physical];
            }
        }

        // Steam is not rebindable but still has to reach the game as Guide.
        if (pressed.HasFlag(SteamControllerButtons.Steam))
        {
            mapped |= Xbox360Buttons.Guide;
        }

        return mapped;
    }

    /// <summary>Serialisable form: physical button name to Xbox button name.</summary>
    public Dictionary<string, string> ToDictionary()
        => All.ToDictionary(b => b.ToString(), b => this[b].ToString());

    /// <summary>
    /// Rebuilds a map from stored names, falling back to the default for anything missing or
    /// unrecognised. A profile written by a newer build, or edited by hand, must never produce a
    /// controller with dead buttons.
    /// </summary>
    public static XboxButtonMap FromDictionary(IReadOnlyDictionary<string, string>? stored)
    {
        var map = Default;
        if (stored is null)
        {
            return map;
        }

        foreach (var physical in All)
        {
            if (stored.TryGetValue(physical.ToString(), out var name)
                && Enum.TryParse<Xbox360Buttons>(name, ignoreCase: true, out var output))
            {
                map[physical] = output;
            }
        }

        return map;
    }
}
