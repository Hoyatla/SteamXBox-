namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Builds the command line the core expects.
/// </summary>
/// <remarks>
/// Beside the core rather than beside a caller, because it is the core's own contract: the producer
/// of these arguments and their consumer cannot drift apart if they live together.
///
/// It exists because they did drift. The configuration window launched the core with
/// <c>xbox-run --restart --start-mode … --profile …</c>, while the environment launched the same
/// executable with no arguments at all. Without <c>xbox-run</c> there is no virtual gamepad, so
/// switching to Xbox did nothing; without <c>--profile</c> there is no mapping, so the profile
/// shortcuts did nothing. One broken controller, two symptoms, and two places that each thought
/// they knew how to start the core.
///
/// Takes primitives rather than the profile object: the core cannot reference the layer that
/// defines it, and this way the arguments can be tested without any of it.
/// </remarks>
public static class CoreCommandLine
{
    /// <summary>Arguments for a normal run: virtual gamepad up, mapping loaded.</summary>
    /// <param name="profileName">Profile whose mapping and Xbox layout the core loads.</param>
    /// <param name="mode">Starting mode, <c>profile</c> or <c>xbox360</c>.</param>
    /// <param name="switchButton">Button that toggles between the two modes.</param>
    public static string BuildRun(string profileName, string mode, string switchButton)
    {
        // Quoted: profile names are user-chosen and routinely contain spaces. Unquoted, "Mon profil"
        // arrives as two arguments and the core loads a profile that does not exist.
        return $"xbox-run --restart --start-mode {Normalise(mode)} --switch-button {Fallback(switchButton, DefaultSwitchButton)} "
             + $"--profile \"{Fallback(profileName, DefaultProfile)}\"";
    }

    /// <summary>Profile used when none is recorded yet.</summary>
    public const string DefaultProfile = "Default";

    /// <summary>Mode switch button used when none is recorded yet.</summary>
    public const string DefaultSwitchButton = "quick-access";

    /// <summary>
    /// Lower-cases the mode, which is stored capitalised.
    /// </summary>
    /// <remarks>
    /// The profile stores <c>Profile</c> and <c>Xbox360</c>; the command line expects them in lower
    /// case. Converting here rather than at each call site is what stops one caller from getting it
    /// right and another from getting it wrong.
    /// </remarks>
    private static string Normalise(string mode)
        => string.IsNullOrWhiteSpace(mode) ? "profile" : mode.Trim().ToLowerInvariant();

    private static string Fallback(string value, string whenEmpty)
        => string.IsNullOrWhiteSpace(value) ? whenEmpty : value.Trim();
}
