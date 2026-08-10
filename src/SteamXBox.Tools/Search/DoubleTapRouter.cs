namespace SteamXBox.Tools.Search;

/// <summary>
/// Watches several double-tap gestures at once, and says which of them a keystroke completed.
/// </summary>
/// <remarks>
/// Separated from the keyboard hook for the same reason <see cref="DoubleTapDetector"/> is: the hook
/// needs a message loop, a real keyboard and a machine nobody else is typing on, while the rule
/// being applied is plain logic that a test can pin down.
///
/// <para>
/// And it is the rule between gestures, not within one, that is easy to get wrong. Each gesture is
/// interrupted by any key that is not its own, so tapping Escape, then Shift, then Escape is three
/// keystrokes and no gesture — which is what the user meant. Getting that backwards fires a shortcut
/// in the middle of ordinary typing, and the user has no way to tell why.
/// </para>
/// </remarks>
/// <typeparam name="T">Whatever the caller wants back when a gesture completes.</typeparam>
public sealed class DoubleTapRouter<T>
{
    private readonly (T Binding, IReadOnlySet<int> Keys, DoubleTapDetector Detector)[] _gestures;

    public DoubleTapRouter(
        IEnumerable<(T Binding, IReadOnlySet<int> Keys)> gestures, long windowMs = 350)
        => _gestures = gestures
            .Select(g => (g.Binding, g.Keys, new DoubleTapDetector(windowMs)))
            .ToArray();

    /// <summary>Reports a key-down, and returns the gestures it completed.</summary>
    public IReadOnlyList<T> Press(int key, long nowMs)
    {
        List<T>? fired = null;

        foreach (var (binding, keys, detector) in _gestures)
        {
            if (!keys.Contains(key))
            {
                detector.Interrupt();
                continue;
            }

            if (detector.Press(nowMs))
            {
                // Allocated only when something fires, which is almost never: this runs on every
                // keystroke typed on the machine.
                (fired ??= []).Add(binding);
            }
        }

        return (IReadOnlyList<T>?)fired ?? [];
    }

    /// <summary>Reports a key-up.</summary>
    public void Release(int key)
    {
        foreach (var (_, keys, detector) in _gestures)
        {
            if (keys.Contains(key))
            {
                detector.Release();
            }
        }
    }
}
