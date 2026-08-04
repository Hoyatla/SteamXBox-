namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// One profile mapper per controller.
/// </summary>
/// <remarks>
/// The last shared piece of a pipeline that is otherwise per-controller, and the one that made
/// reading two pads at once unusable.
///
/// <para>
/// A <see cref="ProfileMapper"/> is stateful by necessity: it detects button edges by comparing each
/// frame to the one before it, and it carries trackball inertia and scroll momentum across frames.
/// All of that assumes the frames belong to one hand. Feed it two controllers and the comparison
/// happens across them — pad A reports B held, pad B's next frame reports it released, pad A's
/// reports it held again — so every button is pressed and released dozens of times a second. On
/// screen that is a controller that works for a moment and then does something arbitrary, and it
/// happens with two ordinary Xbox pads just as readily as with a pad and a phantom.
/// </para>
///
/// <para>
/// Normalising the input does not help and was never the problem: every source already converges on
/// one common frame type before anything else sees it. What has to be per-controller is not the
/// data, it is the <i>memory</i> — and memory cannot be normalised away.
/// </para>
///
/// <para>
/// The factory is injected, so a mapper built from a controller's own profile can be supplied
/// per controller rather than one profile serving everybody.
/// </para>
/// </remarks>
public sealed class ProfileMapperSet
{
    private readonly Dictionary<string, ProfileMapper> _mappers = new(StringComparer.Ordinal);
    private readonly Func<string, ProfileMapper> _factory;

    /// <param name="factory">
    /// Builds the mapper for one controller, given its id. The id is passed in so the caller can
    /// look up that controller's own profile rather than applying one to everybody.
    /// </param>
    public ProfileMapperSet(Func<string, ProfileMapper> factory) => _factory = factory;

    /// <summary>How many controllers have a mapper.</summary>
    public int Count => _mappers.Count;

    /// <summary>The mapper belonging to one controller, built on first use.</summary>
    public ProfileMapper For(string controllerId)
    {
        if (!_mappers.TryGetValue(controllerId, out var mapper))
        {
            mapper = _factory(controllerId);
            _mappers[controllerId] = mapper;
        }

        return mapper;
    }

    /// <summary>Whether this controller already has a mapper.</summary>
    public bool Has(string controllerId) => _mappers.ContainsKey(controllerId);

    /// <summary>
    /// Drops a controller's mapper.
    /// </summary>
    /// <remarks>
    /// For a pad that disconnects. Its mapper holds the last frame it sent, so a controller that
    /// leaves mid-press would come back to a mapper still believing that button is held — and the
    /// release would never be seen.
    /// </remarks>
    public void Forget(string controllerId) => _mappers.Remove(controllerId);

    /// <summary>Rebuilds every mapper, for when the profiles on disk have changed.</summary>
    public void Reload()
    {
        foreach (var id in _mappers.Keys.ToList())
        {
            _mappers[id] = _factory(id);
        }
    }
}
