namespace SteamXBox.Tools.Declarative;

/// <summary>What a declarative tool asked the host to open.</summary>
/// <param name="Kind">Which of the known ways to open something.</param>
/// <param name="Argument">The page, address or folder, already validated for that kind.</param>
public readonly record struct LaunchTarget(LaunchKind Kind, string Argument);

/// <summary>The ways a declarative tool may ask for something to be opened.</summary>
/// <remarks>
/// A closed set, and that is the point. An open one — "run this executable" — would mean installing
/// a tool found somewhere is running its author's code, which is exactly what the manifest format
/// exists to avoid.
/// </remarks>
public enum LaunchKind
{
    /// <summary>Nothing valid was named.</summary>
    None,

    /// <summary>A page of the Windows settings application, by its documented <c>ms-settings:</c> name.</summary>
    Settings,

    /// <summary>An address, opened in whatever the user has chosen as their browser.</summary>
    Web,

    /// <summary>One of Windows' own folders, opened in the file explorer.</summary>
    Folder,
}

/// <summary>
/// Reads the <c>does</c> field of a declarative tool and says what it may open.
/// </summary>
/// <remarks>
/// This is the whole of "the vocabulary of launching" for the first tier. Three kinds, each one
/// something the host already does for itself, which is the test the policy sets for any addition:
/// <i>does the host already know how to do this?</i>
///
/// <para>
/// Running an arbitrary executable is deliberately absent. It is the one launch that turns
/// installing a tool into running a stranger's program, and no amount of care in the manifest makes
/// that visible to somebody who is not reading it. A tool that genuinely needs it belongs to a tier
/// that announces itself as code.
/// </para>
///
/// <para>
/// Everything here refuses rather than guesses. An unknown kind, a malformed address, a folder that
/// is not one of Windows' own: all return <see cref="LaunchKind.None"/>, and the caller says so in
/// the log instead of opening something the author did not mean.
/// </para>
/// </remarks>
public static class LaunchVocabulary
{
    /// <summary>
    /// Windows' own folders a tool may ask to open, by the name used in the manifest.
    /// </summary>
    /// <remarks>
    /// Named rather than given as paths, so a manifest cannot point at an arbitrary directory and so
    /// the same tool works on a machine whose folders have been moved.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, Environment.SpecialFolder> KnownFolders =
        new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = Environment.SpecialFolder.MyDocuments,
            ["pictures"] = Environment.SpecialFolder.MyPictures,
            ["music"] = Environment.SpecialFolder.MyMusic,
            ["videos"] = Environment.SpecialFolder.MyVideos,
            ["desktop"] = Environment.SpecialFolder.DesktopDirectory,
            ["downloads"] = Environment.SpecialFolder.UserProfile,
        };

    /// <summary>Reads a <c>does</c> value, or <see cref="LaunchKind.None"/> when it names nothing valid.</summary>
    public static LaunchTarget Parse(string? does)
    {
        if (string.IsNullOrWhiteSpace(does))
        {
            return default;
        }

        var separator = does.IndexOf(':');
        if (separator <= 0 || separator == does.Length - 1)
        {
            return default;
        }

        var kind = does[..separator].Trim();
        var argument = does[(separator + 1)..].Trim();

        if (argument.Length == 0)
        {
            return default;
        }

        return kind.ToLowerInvariant() switch
        {
            "open.settings" => Settings(argument),
            "open.web" => Web(argument),
            "open.folder" => Folder(argument),
            _ => default,
        };
    }

    /// <remarks>
    /// The page name only, never a whole URI. Accepting <c>ms-settings:foo</c> as written would mean
    /// accepting any other scheme spelled the same way, and a scheme is exactly how an arbitrary
    /// program gets launched on Windows.
    /// </remarks>
    private static LaunchTarget Settings(string page)
        => page.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? new LaunchTarget(LaunchKind.Settings, page.ToLowerInvariant())
            : default;

    /// <remarks>
    /// Only <c>http</c> and <c>https</c>. Every other scheme a browser understands is a way to reach
    /// something that is not a web page, and <c>file:</c> in particular would walk straight past the
    /// rule about reading the disk.
    /// </remarks>
    private static LaunchTarget Web(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new LaunchTarget(LaunchKind.Web, uri.AbsoluteUri)
            : default;

    private static LaunchTarget Folder(string name)
        => KnownFolders.ContainsKey(name)
            ? new LaunchTarget(LaunchKind.Folder, name.ToLowerInvariant())
            : default;
}
