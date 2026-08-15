namespace Sc2Xboxed.Core.Output;

/// <summary>
/// The buttons a DualShock 4 report carries, as a bitfield.
/// </summary>
/// <remarks>
/// The d-pad is not here: on a DualShock 4 it is a hat switch, carried separately in
/// <see cref="DS4Report"/>. The PlayStation and touchpad buttons live in the report's special byte
/// and are carried as <see cref="DS4Report.PlayStation"/>. The bit positions match the report layout
/// a DualShock 4 writes, so <see cref="ViGEmDS4Sink"/> can hand them to ViGEm verbatim.
/// </remarks>
[Flags]
public enum DS4Buttons : ushort
{
    None = 0,

    ThumbRight = 1 << 0,    // R3
    ThumbLeft = 1 << 1,     // L3
    Options = 1 << 2,
    Share = 1 << 3,
    TriggerRight = 1 << 4,  // R2, digital press
    TriggerLeft = 1 << 5,   // L2, digital press
    ShoulderRight = 1 << 6, // R1
    ShoulderLeft = 1 << 7,  // L1
    Triangle = 1 << 8,
    Circle = 1 << 9,
    Cross = 1 << 10,
    Square = 1 << 11,
}
