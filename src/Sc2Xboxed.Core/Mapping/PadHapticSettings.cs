namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Per-pad haptic feel: how hard each pulse is, and how often pulses occur.
/// </summary>
/// <remarks>
/// These affect the vibration only. Cursor movement and wheel output are produced before any of this
/// is consulted, so changing the feel of the feedback can never change where the pointer goes or how
/// far the page scrolls.
/// </remarks>
public sealed record PadHapticSettings
{
    public static PadHapticSettings Default { get; } = new();

    /// <summary>Strength, 0-1. 0 disables this pad's haptics entirely.</summary>
    public double Force { get; init; } = 0.5;

    /// <summary>Rate, 0-1. Higher means pulses closer together.</summary>
    public double Frequency { get; init; } = 0.5;

    public bool Enabled => Force > 0.0;

    /// <summary>
    /// Pulse on-time in microseconds. The pulse report carries no gain field on this firmware, so
    /// on-time is what perceived strength comes from.
    /// </summary>
    public ushort PulseWidthUs => (ushort)Lerp(60.0, 600.0, Force);

    /// <summary>Cursor travel between ticks, in pixels. Used by pads driving the pointer.</summary>
    public double TravelPerTickPixels => Lerp(400.0, 40.0, Frequency);

    /// <summary>Minimum gap between detents, in milliseconds. Used by pads driving the wheel.</summary>
    public double DetentIntervalMs => Lerp(140.0, 20.0, Frequency);

    private static double Lerp(double from, double to, double t) => from + (to - from) * Math.Clamp(t, 0.0, 1.0);
}
