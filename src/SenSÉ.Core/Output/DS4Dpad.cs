namespace SenSÉ.Core.Output;

/// <summary>
/// Where the d-pad hat points on a DualShock 4.
/// </summary>
/// <remarks>
/// The values follow the report layout a DualShock 4 writes: 0 is up and the dial runs clockwise,
/// with <see cref="None"/> as the released position. ViGEm numbers its directions differently
/// (counter-clockwise, with <c>None</c> first), so <see cref="ViGEmDS4Sink"/> translates.
/// </remarks>
public enum DS4Dpad : byte
{
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7,
    None = 8,
}
