namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Which profile belongs to which controller.
/// </summary>
/// <remarks>
/// Every controller is an input, so each carries its own settings. Two players sharing one machine
/// want different sensitivities and different bindings, and until now there was one profile for
/// whatever pad happened to be read.
///
/// <para>
/// The hard part is not the mapping, it is that a wrong answer is silent. A controller filed under
/// the wrong key does not fail — it quietly applies the other player's settings, and the only
/// symptom is that the pad "feels off" in a way nobody can pin down. That is why
/// <see cref="ControllerIdentityFactory.IsStable"/> exists and why it is consulted here rather than
/// assumed: a HID path identifies a device across reboots, an XInput slot identifies only the order
/// somebody switched their controllers on in.
/// </para>
///
/// <para>
/// Assignments to unstable keys are kept, because within one session they are exactly right and
/// refusing them would mean an Xbox pad could never have a profile at all. They are simply not
/// written to disk: an entry filed under <c>xinput-slot:0</c> would be restored tomorrow onto
/// whichever pad connected first, which is the silent swap this class exists to prevent.
/// </para>
/// </remarks>
public sealed class ControllerProfileBook
{
    private readonly Dictionary<string, string> _byController = new(StringComparer.Ordinal);

    /// <param name="defaultProfile">Used by any controller with no assignment of its own.</param>
    public ControllerProfileBook(string defaultProfile = "default")
    {
        DefaultProfile = string.IsNullOrWhiteSpace(defaultProfile) ? "default" : defaultProfile;
    }

    /// <summary>The profile a controller falls back to when it has none of its own.</summary>
    public string DefaultProfile { get; }

    /// <summary>How many controllers have an assignment.</summary>
    public int Count => _byController.Count;

    /// <summary>
    /// The profile this controller should use.
    /// </summary>
    /// <remarks>
    /// Never null and never throws. A controller nobody has configured is the normal case — a guest
    /// plugging in a second pad — and it must simply work on the defaults rather than be refused.
    /// </remarks>
    public string ProfileFor(string controllerId)
        => _byController.TryGetValue(controllerId, out var profile) ? profile : DefaultProfile;

    /// <summary>Whether this controller has an assignment of its own.</summary>
    public bool HasOwnProfile(string controllerId) => _byController.ContainsKey(controllerId);

    /// <summary>Assigns a profile to a controller.</summary>
    /// <remarks>
    /// An empty profile name clears the assignment rather than filing the controller under a blank
    /// profile that no longer exists — which is what happens when the user deletes the profile a
    /// controller was using.
    /// </remarks>
    public void Assign(string controllerId, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(controllerId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            _byController.Remove(controllerId);
            return;
        }

        _byController[controllerId] = profileName;
    }

    /// <summary>Drops a controller's assignment.</summary>
    public void Clear(string controllerId) => _byController.Remove(controllerId);

    /// <summary>
    /// Drops every assignment naming a profile that no longer exists.
    /// </summary>
    /// <remarks>
    /// Deleting a profile in the interface would otherwise leave controllers pointing at it, and
    /// they would silently fall back to the defaults with nothing saying why their settings changed.
    /// </remarks>
    public void DropMissingProfiles(IEnumerable<string> existingProfiles)
    {
        var known = existingProfiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in _byController
                     .Where(pair => !known.Contains(pair.Value))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _byController.Remove(id);
        }
    }

    /// <summary>
    /// The assignments worth writing to disk.
    /// </summary>
    /// <remarks>
    /// Only the ones filed under a key that identifies the same physical device tomorrow. Persisting
    /// an XInput slot would restore one player's settings onto whoever switched their controller on
    /// first — a failure with no symptom other than a pad that feels wrong.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Persistable()
        => _byController
            .Where(pair => ControllerIdentityFactory.IsStable(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Every assignment, including the ones that last only for this session.</summary>
    public IReadOnlyDictionary<string, string> All()
        => new Dictionary<string, string>(_byController, StringComparer.Ordinal);

    /// <summary>Restores saved assignments, ignoring any that are not durable.</summary>
    public void Load(IReadOnlyDictionary<string, string>? saved)
    {
        if (saved is null)
        {
            return;
        }

        foreach (var (id, profile) in saved)
        {
            // Guarded on the way in as well as out: a file hand-edited or written by an older build
            // may hold slot keys, and trusting them is the silent swap all over again.
            if (ControllerIdentityFactory.IsStable(id) && !string.IsNullOrWhiteSpace(profile))
            {
                _byController[id] = profile;
            }
        }
    }
}
