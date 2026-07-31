namespace Sc2Xboxed.Core.Mapping;

/// <summary>What a touchpad drives in Profile mode.</summary>
public enum PadMotionMode
{
    /// <summary>Relative pointer movement with inertia.</summary>
    Trackball,

    /// <summary>Mouse wheel.</summary>
    Scroll,

    /// <summary>Nothing; the pad still reports clicks but produces no motion.</summary>
    None,
}

/// <summary>What the left stick drives in Profile mode.</summary>
public enum StickMotionMode
{
    /// <summary>Holds the arrow keys while pushed past the dead zone.</summary>
    ArrowKeys,

    None,
}

/// <summary>
/// Button that halves sensitivity while held. Its usual binding is suppressed for as long as it is
/// assigned here, so the two never fire together.
/// </summary>
public enum PrecisionButton
{
    None,
    L4,
    R4,
    L5,
    R5,
    LeftBumper,
    RightBumper,
}
