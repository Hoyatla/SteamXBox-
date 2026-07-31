using System.IO;
using Microsoft.Win32;

namespace SteamXBox.Gui.Services;

/// <summary>
/// Registers SteamXBox in the per-user Run key so Windows launches it at sign-in.
/// </summary>
/// <remarks>
/// The GUI previously had a checkbox reading "start automatically" that only started the core once a
/// controller was detected; starting with Windows was left to a separate .cmd script. This gives the
/// setting a real implementation so the label can be honest.
/// </remarks>
public static class WindowsStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SteamXBox";

    public static bool IsEnabled() => RegisteredCommand() is not null;

    /// <summary>
    /// The command Windows will actually run at sign-in, or null when nothing is registered.
    /// Surfaced in the GUI so a stale entry left by an older install is visible rather than guessed at.
    /// </summary>
    public static string? RegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0 ? value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns true when the registry now matches the requested state.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            // Quoted: the install path routinely contains spaces.
            var command = $"\"{exePath}\"";
            key.SetValue(ValueName, command);

            // Read back rather than trusting the write: a policy or a sync agent can revert the
            // value immediately, and the checkbox must not claim something that did not stick.
            return string.Equals(RegisteredCommand(), command, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // A locked-down or roaming profile can refuse this; the caller reports it rather than
            // leaving the checkbox claiming something that did not happen.
            return false;
        }
    }
}
