using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Everything the bridge remembers about one controller between two of its frames.
/// </summary>
/// <remarks>
/// The rule this type exists to enforce, arrived at after fixing the same class of bug four times:
///
/// <list type="bullet">
/// <item>
/// <b>Anything that compares a frame to the previous one must be per controller.</b> Button edges,
/// chord latches, sub-pixel carry, frame gaps. Shared between two pads, the comparison happens
/// across them: pad A reports a button held, pad B's next frame reports it released, pad A's reports
/// it held again. Every button then fires dozens of times a second, the mode chord latches and
/// unlatches, and the measured frame gap is the interval between two controllers rather than one
/// controller's own.
/// </item>
/// <item>
/// <b>Anything driving a single desktop resource must be global and arbitrated.</b> The pointer, the
/// wheel, the overlay keyboard, the output mode. Those live in the loop, not here.
/// </item>
/// </list>
///
/// <para>
/// Mixing the two categories is what made every fix reveal another fault: the symptoms differed but
/// the cause was one, and correcting them one at a time could never converge.
/// </para>
///
/// <para>
/// The frame gap deserves its own note. <see cref="LastFrame"/> shared between controllers meant the
/// pointer measured the time since <i>somebody else's</i> frame — a few hundred microseconds instead
/// of eight milliseconds — so the stick moved the cursor a fraction of what it should, or the guard
/// rejected the frame outright. With no trackpads on Xbox or PlayStation pads, the sticks are the
/// only pointer there is, so this was not an edge case: it was the pointer not working whenever a
/// second pad was attached, including a phantom one.
/// </para>
/// </remarks>
public sealed class ControllerSession
{
    public ControllerSession(
        ProfileMapper profileMapper,
        InputModeHandler modeSwitcher,
        ControllerOutputMapper? xboxMapper = null,
        DualSenseGamepadMapper? dualSenseMapper = null)
    {
        ProfileMapper = profileMapper;
        ModeSwitcher = modeSwitcher;
        XboxMapper = xboxMapper ?? new ControllerOutputMapper();
        DualSenseMapper = dualSenseMapper ?? new DualSenseGamepadMapper();
    }

    /// <summary>
    /// What this controller sends a game in Xbox mode, when it is an Xbox-family pad.
    /// </summary>
    /// <remarks>
    /// Per controller for two reasons at once. It carries the button layout and tuning, which are
    /// the point of "one Xbox profile per controller" — held statically, one layout served the whole
    /// process and every assignment was recorded, displayed, persisted and ignored. And it is
    /// stateful across frames like everything else here, so sharing it would blur two players'
    /// inputs into one even with identical layouts.
    /// </remarks>
    public ControllerOutputMapper XboxMapper { get; }

    /// <summary>
    /// What a DualSense sends a game in Xbox mode: the same profile values, emitted as a DualShock 4
    /// report so the game sees the PlayStation pad its labels promise.
    /// </summary>
    public DualSenseGamepadMapper DualSenseMapper { get; }

    /// <summary>Desktop mapping, and the button edges it detects.</summary>
    public ProfileMapper ProfileMapper { get; }

    /// <summary>
    /// Mode chord detection for this controller.
    /// </summary>
    /// <remarks>
    /// Per controller although the mode it switches is global: the chord is a sequence of presses by
    /// one pair of thumbs, and detecting it across two players' frames latches on inputs nobody made.
    /// The resulting switch still applies to everyone — one desktop, one mode.
    /// </remarks>
    public InputModeHandler ModeSwitcher { get; }

    /// <summary>
    /// Power-off chord detection for this controller.
    /// </summary>
    /// <remarks>
    /// Per controller for the same reason the mode chord is: Menu + View held by one player must not
    /// be interrupted by a second controller's idle frames. Before this was per session, a pad sat
    /// next to the one holding the chord reset <c>_heldSince</c> every frame of its own, so a two
    /// second hold never accumulated — the chord read as never fired. The power-off it triggers is
    /// still applied to this controller only.
    /// </remarks>
    public ButtonChordDetector PowerOffChordDetector { get; set; } = new(
        SteamControllerButtons.Menu | SteamControllerButtons.View,
        TimeSpan.FromSeconds(2));

    /// <summary>Withholds the chord buttons of this controller until they resolve into a chord or a press.</summary>
    public ChordButtonGate PowerOffChordGate { get; set; } = new(
        SteamControllerButtons.Menu | SteamControllerButtons.View);

    /// <summary>Sub-pixel remainder of this controller's stick motion.</summary>
    public StickPointerCarry Carry;

    /// <summary>
    /// Cursor travel accumulated by this controller's motion since the last haptic tick.
    /// </summary>
    /// <remarks>
    /// Per controller: the quantisation is a gesture's own travelled distance, and pooled across pads
    /// one controller's motion would trip the other's tick — the pointer on the first pad buzzing in
    /// time with a second pad's stick.
    /// </remarks>
    public double HapticTravel { get; set; }

    /// <summary>When this controller's scroll detent haptics last ticked.</summary>
    /// <remarks>Per controller for the same reason <see cref="HapticTravel"/> is.</remarks>
    public DateTimeOffset LastScrollTick { get; set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// How this controller's sticks drive the pointer: dead zone, speed, curve.
    /// </summary>
    /// <remarks>
    /// Per controller, and read from this controller's own profile. It used to be one instance built
    /// once outside the loop from the hardcoded defaults — a dead zone of 0.15 while the profile on
    /// disk said 0.06, the same speed and the same curve for a DualSense and an Xbox pad, and the
    /// sensitivity controls in the GUI changing nothing at all because nothing ever read them.
    /// </remarks>
    public StickPointerSettings StickPointer { get; set; } = new();

    /// <summary>Timestamp of this controller's previous frame.</summary>
    public TimeSpan LastFrame { get; set; } = TimeSpan.Zero;

    /// <summary>Which controller this session belongs to, for the diagnostics.</summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Largest right-stick deflection seen since the last counter line, and the last frame gap.
    /// </summary>
    /// <remarks>
    /// Per controller for the same reason everything else here is, and it was the last measurement
    /// still pooled across all of them. A single peak over every pad answers "did any stick move",
    /// which is not the question: with a phantom pad reporting zeros beside a real one, the pooled
    /// figure can read zero while a stick is at full deflection — and it did, which is what sent
    /// this investigation after the wrong suspect.
    /// </remarks>
    public double PeakX { get; set; }

    /// <inheritdoc cref="PeakX"/>
    public double PeakY { get; set; }

    /// <inheritdoc cref="PeakX"/>
    public double LastGapMs { get; set; }
}

/// <summary>
/// One session per controller, built on first sight.
/// </summary>
/// <remarks>
/// The factory takes the controller's id so a session can be built from that controller's own
/// profile rather than one profile serving everybody.
/// </remarks>
public sealed class ControllerSessionSet
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ControllerSession> _sessions = new(StringComparer.Ordinal);
    private readonly Func<string, ControllerSession> _factory;

    public ControllerSessionSet(Func<string, ControllerSession> factory) => _factory = factory;

    /// <summary>How many controllers have been seen.</summary>
    public int Count
    {
        get { lock (_gate) return _sessions.Count; }
    }

    /// <summary>Every session, for the transitions that must reach all controllers at once.</summary>
    public IReadOnlyCollection<ControllerSession> All
    {
        get { lock (_gate) return _sessions.Values.ToArray(); }
    }

    public ControllerSession For(string controllerId)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(controllerId, out var session))
            {
                session = _factory(controllerId);
                _sessions[controllerId] = session;
            }

            return session;
        }
    }

    /// <summary>Forgets a controller that has gone away, so it comes back with a clean memory.</summary>
    public void Forget(string controllerId)
    {
        lock (_gate)
        {
            _sessions.Remove(controllerId);
        }
    }

    /// <summary>Rebuilds every session, for when the profiles on disk have changed.</summary>
    public void Reload() => Reload(_ => true);

    /// <summary>
    /// Rebuilds only the sessions the caller names, and returns how many were touched.
    /// </summary>
    /// <remarks>
    /// Rebuilding a session is not free and not invisible: it throws away the chord timers, the
    /// trackball's inertia, the sub-pixel carry and the first-frame flag. Doing it to every
    /// controller because one profile was edited means a hand holding a two-second chord on one pad
    /// loses it whenever someone touches a slider for another — and a chord that never completes is
    /// indistinguishable from a button that does not work.
    ///
    /// <para>
    /// The overlay state is not thrown away with the rest. A rebuild that closed the keyboard would
    /// make assigning a profile look like a crash, so <see cref="ProfileMapper.OskActive"/> and
    /// <see cref="ProfileMapper.DaisywheelActive"/> are carried from the old mapper onto the new one,
    /// each session for itself — never read from a neighbour's, which is what a process-wide capture
    /// did before.
    /// </para>
    ///
    /// <para>
    /// The whole set is guarded by one lock, not per session. Rumble handlers run on background
    /// threads and call <see cref="For"/> while the main loop builds sessions, forgets departed pads
    /// and reloads profiles; a dictionary written and read concurrently is exactly the corruption a
    /// single gate rules out, and the handover of a completed rebuild is atomic with its publication.
    /// </para>
    /// </remarks>
    public int Reload(Func<string, bool> shouldReload)
    {
        lock (_gate)
        {
            var reloaded = 0;

            foreach (var id in _sessions.Keys.ToList())
            {
                if (!shouldReload(id))
                {
                    continue;
                }

                var wasOskActive = _sessions[id].ProfileMapper.OskActive;
                var wasDaisywheel = _sessions[id].ProfileMapper.DaisywheelActive;

                var rebuilt = _factory(id);
                rebuilt.ProfileMapper.OskActive = wasOskActive;
                rebuilt.ProfileMapper.DaisywheelActive = wasDaisywheel;
                _sessions[id] = rebuilt;
                reloaded++;
            }

            return reloaded;
        }
    }
}
