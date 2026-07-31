namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Where one resolved setting came from. Knowing that a key was absent and silently defaulted is the
/// difference between "the profile says 600" and "the profile editor writes a key nobody reads".
/// </summary>
public sealed record ProfileValueOrigin(string Key, string Value, bool FromFile)
{
    public override string ToString() => $"{Key} = {Value} {(FromFile ? "(file)" : "(default)")}";
}

public sealed record ProfileLoadResult(
    Sc2XboxedProfileSettings Settings,
    string FilePath,
    bool FileFound,
    string? Error,
    IReadOnlyList<ProfileValueOrigin> Values);
