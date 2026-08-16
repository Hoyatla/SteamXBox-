namespace Sc2Xboxed.Core.Output;

/// <summary>
/// The buttons a DualShock 4 report carries, as a bitfield.
/// </summary>
/// <remarks>
/// The d-pad is not here: on a DualShock 4 it is a hat switch, carried separately in
/// <see cref="DS4Report"/>. The PlayStation and touchpad buttons live in the report's special byte
/// and are carried as <see cref="DS4Report.PlayStation"/>.
///
/// <para>
/// <b>The values are ViGEm's, and they are checked against it.</b> The sink hands this bitfield to
/// <c>SetButtonsFull</c> verbatim, so a wrong bit here is not a compile error and not a crash — it is
/// a game reading a different button than the one pressed. That is what happened: the first version
/// of this table ran the same twelve names from bit 0 to bit 11, the exact mirror of ViGEm's bit 15
/// down to bit 4. Cross fired L2, L1 fired Triangle, and Share, Options, L3 and R3 landed in the
/// low nibble — the d-pad hat, overwritten by <c>SetDPadDirection</c> on the next line — so those
/// four did nothing at all. Read against Nefarius.ViGEm.Client 1.21.256 on 15 August;
/// <c>DS4ButtonsTests</c> holds the same numbers so the table cannot drift back.
/// </para>
/// </remarks>
[Flags]
public enum DS4Buttons : ushort
{
    None = 0,

    Square = 1 << 4,
    Cross = 1 << 5,
    Circle = 1 << 6,
    Triangle = 1 << 7,
    ShoulderLeft = 1 << 8,   // L1
    ShoulderRight = 1 << 9,  // R1
    TriggerLeft = 1 << 10,   // L2, digital press
    TriggerRight = 1 << 11,  // R2, digital press
    Share = 1 << 12,
    Options = 1 << 13,
    ThumbLeft = 1 << 14,     // L3
    ThumbRight = 1 << 15,    // R3
}
