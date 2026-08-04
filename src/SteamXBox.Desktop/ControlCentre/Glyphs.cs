namespace SteamXBox.Desktop.ControlCentre;

/// <summary>
/// The Segoe Fluent Icons code points the control centre uses.
/// </summary>
/// <remarks>
/// Written as escapes and named, rather than pasted as characters. The glyphs live in the Unicode
/// private-use area, where they render as empty boxes in most editors, survive copy and paste
/// badly, and tell a reader nothing about what they are.
///
/// Segoe Fluent Icons ships with Windows 11; on Windows 10 the font falls back to Segoe MDL2
/// Assets, which carries these same code points, so both render correctly.
/// </remarks>
internal static class Glyphs
{
    public const string Controller = "\uE7FC";
    public const string Volume = "\uE767";
    public const string Wifi = "\uE701";
    public const string Bluetooth = "\uE702";
    public const string Brightness = "\uE706";
    public const string Clipboard = "\uE8C8";
    public const string Snip = "\uE722";
    public const string Calculator = "\uE8EF";
    public const string TaskView = "\uE7C4";
    public const string Settings = "\uE713";
    public const string QuietHours = "\uE7ED";
}