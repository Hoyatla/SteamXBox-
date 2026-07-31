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

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
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
            key.SetValue(ValueName, $"\"{exePath}\"");
            return true;
        }
        catch
        {
            // A locked-down or roaming profile can refuse this; the caller reports it rather than
            // leaving the checkbox claiming something that did not happen.
            return false;
        }
    }
}
