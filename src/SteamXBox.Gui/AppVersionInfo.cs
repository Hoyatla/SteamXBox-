using System.Reflection;

namespace SteamXBox.Gui;

/// <summary>
/// The single place the interface gets its version number from.
/// </summary>
/// <remarks>
/// It has been wrong twice: the Debug tab claimed v2.3 while v3.0 shipped, and the About box claimed
/// v3.1 while v3.2 shipped. Both were literals typed into a view, and fixing one did not fix the
/// other. Reading the assembly means the number cannot drift from the build again.
/// </remarks>
public static class AppVersionInfo
{
    /// <summary>Three-part version, e.g. "3.2.0".</summary>
    public static string Number { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Version prefixed for display, e.g. "v3.2.0".</summary>
    public static string Display { get; } = "v" + Number;

    /// <summary>Product name and version, e.g. "SteamXBox v3.2.0".</summary>
    public static string ProductAndVersion { get; } = "SteamXBox " + Display;
}
