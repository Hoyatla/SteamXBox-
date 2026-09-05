using System.Diagnostics;
using SenSÉ.Tools.Declarative;

namespace SenSÉ.Desktop.Tools;

/// <summary>
/// Carries out the three things a declarative tool may ask the host to open.
/// </summary>
/// <remarks>
/// The deciding was done in <see cref="LaunchVocabulary"/>, which is reachable by the tests; this
/// only performs what was already judged valid. Nothing here re-reads the manifest, and nothing here
/// accepts a string: it takes a <see cref="LaunchTarget"/>, which cannot be built by hand from
/// something the parser refused.
///
/// <para>
/// Every launch goes through the shell, so it opens with whatever the user has chosen — their
/// browser, their file explorer. None of them starts a program of the tool's choosing: that is the
/// one launch the vocabulary deliberately has no word for.
/// </para>
/// </remarks>
public static class ToolLauncher
{
    /// <summary>Opens the target, and says what happened for the status line.</summary>
    /// <remarks>
    /// Never throws. A tool asking for something the machine cannot open — no browser, a settings
    /// page this build of Windows does not have — is a line in the environment, not a crash of it.
    /// </remarks>
    public static string Open(LaunchTarget target, Action<string>? log = null)
    {
        try
        {
            switch (target.Kind)
            {
                case LaunchKind.Settings:
                    // Composed here, never taken from the manifest. The parser accepts a page name
                    // and nothing else precisely so that this scheme is the only one that can be
                    // built — a scheme is how any program gets launched on Windows.
                    Shell($"ms-settings:{target.Argument}");
                    return $"Réglages Windows : {target.Argument}";

                case LaunchKind.Web:
                    Shell(target.Argument);
                    return target.Argument;

                case LaunchKind.Folder:
                    return OpenFolder(target.Argument);

                default:
                    log?.Invoke($"tool launch refused: nothing valid named ({target.Kind}).");
                    return "";
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"tool launch failed for {target.Kind} '{target.Argument}': "
                        + $"{exception.GetType().Name}: {exception.Message}");

            return "";
        }
    }

    /// <remarks>
    /// Resolved from the name at the moment of opening rather than stored, so a machine whose
    /// Documents folder has been moved opens the one the user actually has.
    /// </remarks>
    private static string OpenFolder(string name)
    {
        if (!LaunchVocabulary.KnownFolders.TryGetValue(name, out var folder))
        {
            return "";
        }

        var path = Environment.GetFolderPath(folder);

        // Downloads has no SpecialFolder of its own, so it is reached from the profile. Checked
        // rather than assumed: a profile without one must open nothing instead of the profile root,
        // which would show the user their whole home directory when they asked for a subfolder.
        if (name == "downloads")
        {
            path = System.IO.Path.Combine(path, "Downloads");
        }

        if (!System.IO.Directory.Exists(path))
        {
            return "";
        }

        Shell(path);
        return path;
    }

    private static void Shell(string target)
        => Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
}
