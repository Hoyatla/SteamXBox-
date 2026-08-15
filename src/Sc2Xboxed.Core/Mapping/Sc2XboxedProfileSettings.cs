namespace Sc2Xboxed.Core.Mapping;

public sealed record Sc2XboxedProfileSettings
{
    /// <summary>
    /// Les valeurs reglees contre le materiel, sans opinion de famille.
    /// </summary>
    /// <remarks>
    /// Le point de depart des trois fichiers de famille, et le seul endroit ou ces nombres sont
    /// ecrits. Ce qu'il ne dit pas : si la manette a des trackpads, et ce que font ses sticks. Ces
    /// trois-la definissent une famille et chaque famille les ecrit elle-meme.
    ///
    /// <para>
    /// A dire honnetement : les initialiseurs bruts de cet enregistrement portent encore des valeurs
    /// de forme "manette Steam" — <c>HasTrackpads</c> a vrai, le stick gauche sur les fleches. C'est
    /// exactement pourquoi chaque famille reecrit ces champs-la au lieu de les heriter. Un champ
    /// ajoute ici sans etre repris dans les trois fichiers repartira en valeur manette Steam pour
    /// tout le monde.
    /// </para>
    /// </remarks>
    public static Sc2XboxedProfileSettings Bare { get; } = new()
    {
        LeftStickDeadZone = 0.06,
        RightStickDeadZone = 0.06,
        GamepadLeftStickDeadZone = 0.018,
        GamepadRightStickDeadZone = 0.018,
        RightPadTrackball = RightTouchpadTrackballSettings.Default with
        {
            InvertY = true,
            PixelsPerPadUnit = 380.0,
            MotionDeadZone = 0.00015,
            AccelerationExponent = 2.0,
            EdgeSpeedPixelsPerSecond = 750.0,
            InertiaDecayPerSecond = 2.0,
        },
        LeftPadTrackball = RightTouchpadTrackballSettings.Default with
        {
            InvertY = true,
            AccelerationExponent = 2.0,
            InertiaDecayPerSecond = 2.0,
        },
        LeftPadScroll = LeftTouchpadScrollSettings.Default with
        {
            InvertVertical = true,
            WheelDeltaPerPadUnit = 4.8,
            MotionDeadZone = 0.002,
            AccelerationExponent = 1.5,
            InertiaDecayPerSecond = 2.0,
        },
    };

    /// <summary>
    /// The settings a controller of this family starts with when it has no profile of its own.
    /// </summary>
    /// <remarks>
    /// <see cref="Default"/> describes a Steam Controller: its trackpads drive the pointer, so its
    /// sticks are free for the arrow keys. A DualSense or an Xbox pad has no trackpads, and applying
    /// that same profile to one wires its left stick to the arrows — a stick resting a few percent
    /// off centre then holds a direction down for as long as the controller is switched on, and the
    /// user's keyboard is unusable while nothing appears to be touching it.
    ///
    /// <para>
    /// So those two families arrive with their sticks quiet. The pointer stays on the right stick,
    /// which is the one thing a pad with no trackpad can reasonably do on a desktop; the left stick
    /// does nothing until its owner asks it to. Reported 14 August: "clavier et souris physique
    /// cassé", after the PS5 and Steam profiles were deleted and both pads fell back on this.
    /// </para>
    ///
    /// <para>
    /// A fallback, never an override. A family with a profile of its own is read from that profile
    /// and never comes here.
    /// </para>
    /// </remarks>
    /// <para>
    /// Un aiguillage et rien d'autre. Chaque famille tient ses valeurs dans son propre fichier, une
    /// branche par famille, jamais deux familles sur une branche. La branche
    /// <c>DualSense or XInput</c> qui se trouvait ici servait deux materiels differents avec une
    /// seule expression : regler l'un reglait l'autre, et il n'y avait aucun endroit ou ecrire ce
    /// qui n'est vrai que d'une DualSense.
    /// </para>
    public static Sc2XboxedProfileSettings DefaultFor(Input.ControllerKind kind) => kind switch
    {
        Ps5ControllerDefaults.Kind => Ps5ControllerDefaults.Settings,
        XboxControllerDefaults.Kind => XboxControllerDefaults.Settings,
        _ => SteamControllerDefaults.Settings,
    };

    /// <summary>
    /// Les reglages d'une manette Steam.
    /// </summary>
    /// <remarks>
    /// Conserve sous ce nom parce que beaucoup d'appelants le lisent, mais ce n'est plus une base
    /// commune : c'est une famille parmi trois, et elle vit dans <see cref="SteamControllerDefaults"/>.
    /// Les familles PS5 et Xbox partaient d'ici et retiraient ce qui ne leur allait pas — une famille
    /// construite en soustrayant d'une autre n'est pas une famille separee.
    /// </remarks>
    public static Sc2XboxedProfileSettings Default => SteamControllerDefaults.Settings;

    /// <summary>
    /// Dead zone of the left stick in Profile mode, as a fraction of its travel.
    /// </summary>
    /// <remarks>
    /// One setting used to govern both sticks, and a single number cannot describe two pieces of
    /// hardware. The two sticks of one controller do not wear at the same rate, are not held the
    /// same way, and rarely do the same job — one walks, the other aims. A dead zone wide enough to
    /// silence a drifting left stick then blunts a right stick that was fine, and the user is left
    /// choosing which of the two to spoil.
    /// </remarks>
    public double LeftStickDeadZone { get; init; } = 0.06;

    /// <summary>Dead zone of the right stick in Profile mode. See <see cref="LeftStickDeadZone"/>.</summary>
    public double RightStickDeadZone { get; init; } = 0.06;

    /// <summary>Dead zone of the left stick in Xbox mode, as a fraction of its travel.</summary>
    public double GamepadLeftStickDeadZone { get; init; } = 0.018;

    /// <summary>Dead zone of the right stick in Xbox mode.</summary>
    public double GamepadRightStickDeadZone { get; init; } = 0.018;

    /// <summary>
    /// Whether this controller has trackpads at all.
    /// </summary>
    /// <remarks>
    /// A DualSense and an Xbox pad have none that this project reads — both report their pads
    /// permanently released — so every pad rule below is inert for them and every pad setting in
    /// their profile describes something that cannot happen. Kept as a flag rather than inferred per
    /// frame: the mapper holds state across frames, and "no touch yet" is not "no pad".
    /// </remarks>
    public bool HasTrackpads { get; init; } = true;

    /// <summary>
    /// Whether this controller's on-screen keyboard follows the text, or stays pinned.
    /// </summary>
    /// <remarks>
    /// Per controller, because it is a preference of the person holding it and not of the machine.
    /// One user types on a pad in the corner of a large screen and wants the board beside the text;
    /// another holds a DualSense on a sofa and wants it in the same place every time. A single
    /// shared flag made the second controller obey the first one's choice.
    /// </remarks>
    public bool OskFloating { get; init; } = true;

    /// <summary>Pointer speed at full stick deflection, in pixels per second.</summary>
    /// <remarks>
    /// For a controller driving the desktop pointer from a stick rather than a trackpad. It was a
    /// hardcoded constant shared by every controller until the profile gained a key for it, so a
    /// DualSense and an Xbox pad could not be tuned apart and the GUI had nothing to bind to.
    /// </remarks>
    public double StickPointerSpeed { get; init; } = 1400.0;

    /// <summary>
    /// Exponent applied to stick deflection before it becomes speed. 1 is linear.
    /// </summary>
    /// <remarks>
    /// Above 1 the first part of the travel is finer than the last, which is what makes a stick
    /// usable for pointing: aiming needs resolution near the centre, crossing the screen needs speed
    /// at the edge, and a linear stick gives neither.
    /// </remarks>
    public double StickPointerCurve { get; init; } = 2.0;

    // ---- What each control drives ----
    // Previously hardcoded in ProfileMapper while the profile editor wrote a "motions" section that
    // nothing ever read, so the three dropdowns had no effect at all.

    public PadMotionMode RightPadMode { get; init; } = PadMotionMode.Trackball;

    public PadMotionMode LeftPadMode { get; init; } = PadMotionMode.Scroll;

    public StickMotionMode LeftStickMode { get; init; } = StickMotionMode.ArrowKeys;

    /// <summary>
    /// Drives the mouse pointer on stick-only controllers (PS5/Xbox) while Pointer mode is active.
    /// Never mapped onto a touchpad: those families have no pads.
    /// </summary>
    public StickMotionMode RightStickMode { get; init; } = StickMotionMode.Pointer;

    public LeftTouchpadScrollSettings LeftPadScroll { get; init; } = LeftTouchpadScrollSettings.Default;

    public RightTouchpadTrackballSettings RightPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    /// <summary>
    /// Used when the left pad is set to trackball. Separate from the right pad's settings, which it
    /// used to borrow wholesale, including that pad's sensitivity and invert flags.
    /// </summary>
    public RightTouchpadTrackballSettings LeftPadTrackball { get; init; } = RightTouchpadTrackballSettings.Default;

    public PadHapticSettings LeftPadHaptics { get; init; } = PadHapticSettings.Default;

    public PadHapticSettings RightPadHaptics { get; init; } = PadHapticSettings.Default;

    public TouchpadTapSettings TouchpadTap { get; init; } = TouchpadTapSettings.Default;

    // ---- Xbox360 mode ----
    // Carried inside the desktop profile since the merge: one file moves desktop mapping and the
    // gamepad layout together, and the runtime no longer needs a separate --xbox-profile argument.

    /// <summary>Physical button name to Xbox 360 button name.</summary>
    public Dictionary<string, string> XboxButtons { get; init; } = XboxButtonMap.Default.ToDictionary();

    public XboxTuning XboxTuning { get; init; } = new();
}
