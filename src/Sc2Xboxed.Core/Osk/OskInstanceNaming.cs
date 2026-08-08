namespace Sc2Xboxed.Core.Osk;

/// <summary>
/// Every channel name for one controller's own keyboard.
/// </summary>
/// <remarks>
/// The overlay was built for a single keyboard: one pipe called <c>SteamXBox_OskPad</c>, one haptic
/// pipe, one <c>osk-show.signal</c>, one resident process. Six fixed names, each of which silently
/// assumes there is only ever one typist. Two players asking for a keyboard at the same time would
/// fight over all six — the second pipe server would fail to bind, the second window would answer
/// the first player's show signal, and the frames of both would land in the same reader.
///
/// <para>
/// So the names are derived from the controller rather than fixed. One place decides the whole set,
/// because a suffix that disagrees between the two ends is a channel that never connects — a silent
/// failure, and the sort this project has already paid for repeatedly.
/// </para>
///
/// <para>
/// A durable key is not usable as-is: it contains characters a pipe name accepts but a file name
/// does not, and its length is unbounded. The suffix is therefore a short, stable digest of the key —
/// same controller, same suffix, across sessions and across the two processes that must agree on it.
/// </para>
/// </remarks>
public sealed class OskInstanceNaming
{
    /// <summary>The keyboard nobody claimed — the pre-warmed instance, and the historical names.</summary>
    /// <remarks>
    /// Kept identical to what shipped before so an overlay started without an instance still finds
    /// its pipes. It is also what a single-controller session uses, which is the common case.
    /// </remarks>
    public static OskInstanceNaming Default { get; } = new("");

    private OskInstanceNaming(string suffix) => Suffix = suffix;

    /// <summary>Empty for the default instance, otherwise a short digest of the controller key.</summary>
    public string Suffix { get; }

    /// <summary>The naming for one controller.</summary>
    public static OskInstanceNaming For(string? controllerId)
        => string.IsNullOrWhiteSpace(controllerId) ? Default : new OskInstanceNaming(Digest(controllerId));

    /// <summary>Rebuilds the naming from a suffix passed on a command line.</summary>
    /// <remarks>
    /// The overlay receives the suffix rather than the controller key: it has no business knowing
    /// which pad it serves, only which channels to open. Passing the key would also put a device
    /// identifier on a command line visible to every process on the machine.
    /// </remarks>
    public static OskInstanceNaming FromSuffix(string? suffix)
        => string.IsNullOrWhiteSpace(suffix) ? Default : new OskInstanceNaming(Clean(suffix));

    /// <summary>Pipe carrying controller frames to this keyboard.</summary>
    public string PadPipeName => Suffix.Length == 0 ? "SteamXBox_OskPad" : $"SteamXBox_OskPad_{Suffix}";

    /// <summary>Pipe carrying this keyboard's haptic requests back to the bridge.</summary>
    public string HapticPipeName => Suffix.Length == 0 ? "SteamXBox_OskHaptic" : $"SteamXBox_OskHaptic_{Suffix}";

    /// <summary>File signal asking this keyboard to appear.</summary>
    public string ShowSignalFile => SignalFile("osk-show");

    /// <summary>File signal asking this keyboard to hide.</summary>
    public string CloseSignalFile => SignalFile("osk-close");

    /// <summary>File signal asking this keyboard to quit.</summary>
    public string ExitSignalFile => SignalFile("osk-exit");

    private string SignalFile(string stem)
        => Suffix.Length == 0 ? $"{stem}.signal" : $"{stem}-{Suffix}.signal";

    /// <summary>
    /// A short, stable digest of a controller key.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than a cryptographic hash: this needs to be identical in two processes and
    /// stable across releases, not secret. Written out rather than taken from the runtime, because
    /// <c>string.GetHashCode</c> is randomised per process — the two ends would derive different
    /// names and the pipe would simply never connect, with nothing to say why.
    /// </remarks>
    public static string Digest(string controllerId)
    {
        var hash = 2166136261u;

        foreach (var c in controllerId)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash.ToString("x8");
    }

    /// <summary>Keeps a suffix to what both a pipe name and a file name accept.</summary>
    private static string Clean(string suffix)
        => new(suffix.Where(char.IsLetterOrDigit).Take(16).ToArray());
}
