namespace SteamXBox.Tools.Search;

/// <summary>
/// Recognises the same key pressed twice in quick succession.
/// </summary>
/// <remarks>
/// Separated from the keyboard hook so it can be tested. The hook cannot: it needs a message loop, a
/// real keyboard and a machine nobody else is typing on. Yet the shape of the gesture is exactly
/// where this fails in ways nobody notices — a repeat counted as a second press, a release missed,
/// a window that opens while somebody is holding shift to type a capital.
///
/// <para>
/// Fed edges, not states. The caller reports each press and each release; this decides. Nothing here
/// reads a clock either — the time comes in — so a test can hold a key for a simulated minute.
/// </para>
/// </remarks>
public sealed class DoubleTapDetector
{
    private readonly long _windowMs;
    private long _lastPress;
    private bool _held;
    private bool _seenOne;

    /// <param name="windowMs">
    /// How long the second press may take. 350 ms is the value the PowerShell original settled on,
    /// kept so the gesture feels the same after the port.
    /// </param>
    public DoubleTapDetector(long windowMs = 350) => _windowMs = windowMs;

    /// <summary>
    /// Reports a key-down, and says whether that completed the gesture.
    /// </summary>
    /// <remarks>
    /// Auto-repeat is ignored, and it has to be: Windows sends key-down over and over while a key is
    /// held, so counting those would fire the gesture at the keyboard's repeat rate the moment
    /// somebody leant on shift. Only a press that follows a release counts.
    /// </remarks>
    public bool Press(long nowMs)
    {
        if (_held)
        {
            return false;
        }

        _held = true;

        if (_seenOne && nowMs - _lastPress <= _windowMs)
        {
            // Consumed. Without this a third press would pair with the second and fire again, so
            // holding the key down and tapping would open and close the window repeatedly.
            _seenOne = false;
            return true;
        }

        _seenOne = true;
        _lastPress = nowMs;

        return false;
    }

    /// <summary>Reports a key-up.</summary>
    public void Release() => _held = false;

    /// <summary>
    /// Forgets a pending first press, when another key was struck in between.
    /// </summary>
    /// <remarks>
    /// Shift is a modifier before it is a shortcut. Typing <c>Hello</c> presses shift, releases it,
    /// then presses it again for a later capital — a double tap by the clock, and not one by
    /// intention. Any other key in between says the first press was modifying something.
    /// </remarks>
    public void Interrupt() => _seenOne = false;
}
