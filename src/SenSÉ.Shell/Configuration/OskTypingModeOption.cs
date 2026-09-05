using SenSÉ.Core.Osk;

namespace SenSÉ.Shell.Configuration;

/// <summary>
/// A typing mode paired with the label shown for it.
/// </summary>
/// <remarks>
/// In Shell rather than beside one of the two screens that use it: the profiles tab of the
/// configuration window offers it, and so does the settings screen in Desktop. Two processes, one
/// list of modes.
/// </remarks>
public sealed record OskTypingModeOption(string Display, OskTypingMode Value);