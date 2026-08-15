namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Which profile belongs to which family of controllers.
/// </summary>
/// <remarks>
/// Steam, PlayStation and Xbox each have their own settings, because the three have genuinely
/// different capabilities — a profile written for trackpads means nothing on a pad that has none.
/// Every connected controller looks its family up here; a family with no assignment runs the launch
/// profile.
///
/// <para>
/// It used to be one profile per controller, filed under a key built from the device. Then one
/// DualSense turned up under two different Bluetooth addresses on the author's machine, the profile
/// was filed under the address it happened to connect with, and the next connection with the other
/// address ran on the defaults with nothing saying so. The family is the one thing that never
/// changes under a controller, so the family is what is filed under — see
/// <see cref="ControllerIdentityFactory.IsStable"/>.
/// </para>
/// </remarks>
public sealed class ControllerProfileBook
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);

    /// <param name="defaultProfile">Used by any family with no assignment of its own.</param>
    public ControllerProfileBook(string defaultProfile = "default")
    {
        DefaultProfile = string.IsNullOrWhiteSpace(defaultProfile) ? "default" : defaultProfile;
    }

    /// <summary>The profile a family falls back to when it has none of its own.</summary>
    public string DefaultProfile { get; }

    /// <summary>How many families have an assignment.</summary>
    public int Count => _byKey.Count;

    /// <summary>
    /// The profile this family should use.
    /// </summary>
    /// <remarks>
    /// Never null and never throws. A family nobody has configured is the normal case — a guest
    /// plugging in a pad — and it must simply work on the defaults rather than be refused.
    /// </remarks>
    public string ProfileFor(string key)
        => _byKey.TryGetValue(key, out var profile) ? profile : DefaultProfile;

    /// <summary>Whether this family has an assignment of its own.</summary>
    public bool HasOwnProfile(string key) => _byKey.ContainsKey(key);

    /// <summary>Assigns a profile to a family.</summary>
    /// <remarks>
    /// An empty profile name clears the assignment rather than filing the family under a blank
    /// profile that no longer exists — which is what happens when the user deletes the profile a
    /// family was using.
    /// </remarks>
    public void Assign(string key, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            _byKey.Remove(key);
            return;
        }

        _byKey[key] = profileName;
    }

    /// <summary>Drops a family's assignment.</summary>
    public void Clear(string key) => _byKey.Remove(key);

    /// <summary>
    /// Drops every assignment naming a profile that no longer exists.
    /// </summary>
    /// <remarks>
    /// Deleting a profile in the interface would otherwise leave families pointing at it, and they
    /// would silently fall back to the defaults with nothing saying why their settings changed.
    /// </remarks>
    public void DropMissingProfiles(IEnumerable<string> existingProfiles)
    {
        var known = existingProfiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _byKey
                     .Where(pair => !known.Contains(pair.Value))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _byKey.Remove(key);
        }
    }

    /// <summary>
    /// The assignments worth writing to disk.
    /// </summary>
    /// <remarks>
    /// Only the family keys. A file written by an older build may still hold per-controller keys —
    /// a Bluetooth address, a slot — and trusting those is the silent loss all over again: the same
    /// pad connected under the other address tomorrow and the settings were never found.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Persistable()
        => _byKey
            .Where(pair => ControllerIdentityFactory.IsStable(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Every assignment, including the ones that last only for this session.</summary>
    public IReadOnlyDictionary<string, string> All()
        => new Dictionary<string, string>(_byKey, StringComparer.Ordinal);

    /// <summary>Restores saved assignments, ignoring any that are not durable.</summary>
    public void Load(IReadOnlyDictionary<string, string>? saved)
    {
        if (saved is null)
        {
            return;
        }

        foreach (var (key, profile) in saved)
        {
            // Guarded on the way in as well as out: a file hand-edited or written by an older build
            // may hold per-controller keys, and trusting them is the silent loss all over again.
            if (ControllerIdentityFactory.IsStable(key) && !string.IsNullOrWhiteSpace(profile))
            {
                _byKey[key] = profile;
            }
        }
    }
}
