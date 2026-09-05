using SenSÉ.Core.Input;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Mapping;

/// <summary>
/// Transforme l'etat d'UNE manette en sa trame de sortie : le rapport Xbox 360 qui part vers son pad
/// virtuel, et ce que ses pads font au bureau.
/// </summary>
/// <remarks>
/// Cette classe ne connait aucune famille. Elle recoit des reglages, une correspondance de boutons
/// et un reglage natif, et elle applique ce qu'on lui donne. Ce qui differe d'une famille a l'autre
/// est decide ailleurs, dans <see cref="SteamControllerDefaults"/>, <see cref="Ps5ControllerDefaults"/>
/// et <see cref="XboxControllerDefaults"/>, et lui arrive par le constructeur.
///
/// <para>
/// Elle s'appelait <c>DefaultSteamControllerMapper</c> alors qu'elle est aussi le mapper d'une
/// manette Xbox. Un nom qui nomme une famille sur du code qui en sert plusieurs est une invitation
/// permanente a y remettre une regle de famille — c'est par la que l'inversion Menu/View mesuree sur
/// une manette Steam avait fini par croiser Options et Start sur les deux autres.
/// </para>
///
/// <para>
/// Une instance par manette. Partagee, elle partagerait aussi l'etat que gardent ses mappers de pads
/// d'une trame a l'autre.
/// </para>
/// </remarks>
public sealed class ControllerOutputMapper
{
    private readonly SenSÉProfileSettings _settings;
    private readonly LeftTouchpadScrollMapper _leftPad;
    private readonly RightTouchpadTrackballMapper _rightPad;
    private readonly TouchpadTapDetector _leftTap;
    private readonly TouchpadTapDetector _rightTap;

    public ControllerOutputMapper()
        : this(SenSÉProfileSettings.Default)
    {
    }

    public ControllerOutputMapper(SenSÉProfileSettings settings)
    {
        _settings = settings;
        _leftPad = new LeftTouchpadScrollMapper(settings.LeftPadScroll);
        _rightPad = new RightTouchpadTrackballMapper(settings.RightPadTrackball);
        _leftTap = new TouchpadTapDetector(settings.TouchpadTap);
        _rightTap = new TouchpadTapDetector(settings.TouchpadTap);

        // From the settings handed in, not from the process-wide default.
        //
        // This constructor takes a controller's own profile and wired every part of it except this
        // one: Tuning kept its initialiser, which is the static DefaultTuning, written once from
        // whichever profile the bridge was launched with. So a mapper built from a DualSense profile
        // ran that profile's pads and that profile's buttons — and the launch profile's stick dead
        // zones, curve, sensitivity, trigger points and vibration.
        //
        // Every controller on the machine therefore shared one set of Xbox-mode tuning values, and
        // editing them in any profile appeared to change all of them at once. The same fault was
        // found and fixed for ButtonMap; the line below it was left behind.
        Tuning = settings.XboxTuning;
    }

    public ControllerOutputFrame Map(ControllerState state)
    {
        state = state.Normalize();

        // The tuning owns the dead zone now: it is radial rather than per-axis, and carries the
        // curve and sensitivity with it. The profile's own stick dead zone remains the fallback.
        var left = Tuning.ApplyStick(state.LeftStick.X, state.LeftStick.Y);
        var right = Tuning.ApplyStick(state.RightStick.X, state.RightStick.Y);

        var report = new Xbox360Report(
            ButtonMap.Apply(state.Buttons),
            ToByteTrigger(Tuning.ApplyTrigger(state.LeftTrigger)),
            ToByteTrigger(Tuning.ApplyTrigger(state.RightTrigger)),
            ToThumbAxis(left.X),
            ToThumbAxis(left.Y),
            ToThumbAxis(right.X),
            ToThumbAxis(right.Y));

        var mouse = _leftPad
            .Update(state.Timestamp, state.LeftPad)
            .Add(_rightPad.Update(state.Timestamp, state.RightPad));

        return new ControllerOutputFrame(
            report,
            mouse,
            _leftTap.Update(state.Timestamp, state.LeftPad),
            _rightTap.Update(state.Timestamp, state.RightPad));
    }

    public void ResetTransientState()
    {
        _leftPad.Reset();
        _rightPad.Reset();
        _leftTap.Reset();
        _rightTap.Reset();
    }

    /// <summary>
    /// This mapper's Xbox360-mode button mapping.
    /// </summary>
    /// <remarks>
    /// Per instance, so each controller can send a game its own layout — which is the whole of what
    /// "un profil Xbox par manette" means. It was static, and a static here meant one layout for the
    /// entire process: the assignment could be recorded, displayed and persisted, and every pad went
    /// on emitting whatever profile the bridge had been launched with.
    ///
    /// It defaults to <see cref="DefaultButtonMap"/>, which the startup path still sets once. A
    /// mapper built before any profile is chosen therefore behaves exactly as before, and only a
    /// mapper deliberately given its own map departs from it.
    /// </remarks>
    public XboxButtonMap ButtonMap { get; set; } = DefaultButtonMap;

    /// <summary>This mapper's stick, trigger and vibration tuning for Xbox360 mode.</summary>
    /// <inheritdoc cref="ButtonMap"/>
    public XboxTuning Tuning { get; set; } = DefaultTuning;

    /// <summary>
    /// What a mapper starts with when nothing has been chosen for it.
    /// </summary>
    /// <remarks>
    /// Still process-wide, and legitimately so: it is the profile the bridge was launched with, the
    /// answer for every controller that has none of its own. What was wrong was not having a shared
    /// default — it was having <i>only</i> a shared value.
    /// </remarks>
    public static XboxButtonMap DefaultButtonMap { get; set; } = XboxButtonMap.Default;

    /// <inheritdoc cref="DefaultButtonMap"/>
    public static XboxTuning DefaultTuning { get; set; } = new();

    public static Xbox360Buttons MapButtons(SteamControllerButtons buttons) => DefaultButtonMap.Apply(buttons);

    private double ApplyDeadZone(double value)
    {
        return Math.Abs(value) < _settings.GamepadLeftStickDeadZone ? 0.0 : value;
    }

    private static byte ToByteTrigger(double normalized)
    {
        return (byte)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * byte.MaxValue);
    }

    private static short ToThumbAxis(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        return normalized < 0.0
            ? (short)Math.Round(normalized * 32768.0)
            : (short)Math.Round(normalized * 32767.0);
    }
}
