namespace SenSÉ.Core.Mapping;

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

/// <summary>What the sticks drive in Profile mode.</summary>
public enum StickMotionMode
{
    /// <summary>Holds the arrow keys while pushed past the dead zone.</summary>
    ArrowKeys,

    /// <summary>Drives the mouse pointer (right stick) or the wheel (left stick).</summary>
    Pointer,

    /// <summary>Drives the wheel (left stick only; the right stick has no wheel of its own).</summary>
    /// <remarks>
    /// Explicitly separate from <see cref="ArrowKeys"/>: before this existed the left stick was
    /// wired to both the arrows and the wheel at once, so "Aucun" was the only way to stop the
    /// wheel and the arrows kept firing anyway. One motion per stick per frame, and the choice is
    /// what the Mouvements card shows.
    /// </remarks>
    Wheel,

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
