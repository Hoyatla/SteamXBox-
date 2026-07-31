namespace Sc2Xboxed.Core.Diagnostics;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3,
    Trace = 4,
}

/// <summary>
/// Subsystem tags. Filtering by category is what makes per-frame tracing usable: it can be turned
/// on for one subsystem without drowning the log in everything else.
/// </summary>
[Flags]
public enum LogCategory
{
    None = 0,

    /// <summary>HID enumeration, device open/close, feature reports.</summary>
    Hid = 1 << 0,

    /// <summary>Pad and stick mapping, mouse and wheel output.</summary>
    Mapping = 1 << 1,

    Haptics = 1 << 2,

    /// <summary>Overlay keyboard lifecycle and typing.</summary>
    Osk = 1 << 3,

    /// <summary>Controller ownership transitions between SteamXBox and Steam.</summary>
    Owner = 1 << 4,

    /// <summary>Output mode switching, manual and automatic.</summary>
    Mode = 1 << 5,

    /// <summary>Named pipe servers and clients.</summary>
    Pipe = 1 << 6,

    /// <summary>
    /// Raw per-frame controller state. Extremely verbose — roughly 200 lines per second — so it is
    /// excluded from <see cref="Default"/> and normally reached through the frame ring buffer instead.
    /// </summary>
    Frame = 1 << 7,

    /// <summary>Periodic runtime counter summaries.</summary>
    Counters = 1 << 8,

    /// <summary>Startup identity and resolved settings.</summary>
    Session = 1 << 9,

    All = Hid | Mapping | Haptics | Osk | Owner | Mode | Pipe | Frame | Counters | Session,

    Default = All & ~Frame,
}
