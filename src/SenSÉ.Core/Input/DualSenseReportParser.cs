namespace SenSÉ.Core.Input;

/// <summary>
/// Turns a DualSense input report into the state the rest of SenSÉ speaks.
/// </summary>
/// <remarks>
/// A PlayStation 5 pad is not an XInput device and never will be: XInput is a Microsoft API that
/// enumerates Xbox-compatible pads only. Windows exposes the DualSense as a plain HID gamepad, so
/// it has to be read and decoded here rather than handed to the same code as an Xbox pad. That is
/// why "the DualSense does nothing" was never a bug in the XInput path — there was no path at all.
///
/// <para>
/// The two transports carry the same fields at different offsets. Over USB the report is
/// <c>0x01</c> and the axes start at byte 1; over Bluetooth the full report is <c>0x31</c>, which
/// inserts a sequence byte and pushes everything one further along. Getting that offset wrong does
/// not fail loudly — it silently reads the Y axis as X — so the transport is decided from the
/// report id rather than assumed, and both are tested.
/// </para>
///
/// <para>
/// Only what an Xbox pad also has is mapped. The touchpad, the gyroscope, the adaptive triggers and
/// the mute button have no equivalent in <see cref="ControllerState"/> and inventing one would
/// mean guessing what the user wants them to do.
/// </para>
/// </remarks>
public static class DualSenseReportParser
{
    /// <summary>Report id of the compact USB report.</summary>
    public const byte UsbReportId = 0x01;

    /// <summary>Report id of the full Bluetooth report.</summary>
    public const byte BluetoothReportId = 0x31;

    /// <summary>Sticks rest near the middle of a byte; anything closer than this is noise.</summary>
    /// <remarks>
    /// A DualSense at rest does not report exactly 128. Left raw, a pad sitting untouched on a desk
    /// produces a slow permanent drift — which on this project has already been mistaken for a bug
    /// in the mapping three times over.
    ///
    /// <para>
    /// Six, not four. Measured on the bench on 15 August over two twelve-second rest recordings, a
    /// pad lying on the table drifts up to <b>five counts</b> from centre (left Y down to 123). At
    /// four, that leaked <c>0,04</c> of stick out of a controller nobody was touching — which is
    /// exactly what "the pointer moves on its own" looks like from the outside. Six absorbs the
    /// measured drift with one count to spare; going wider starts eating deliberate slow movement.
    /// See <c>mesures/dualsense-bt/</c>.
    /// </para>
    /// </remarks>
    private const double RestBand = 6.0 / 128.0;

    /// <summary>Whether a report can be decoded at all.</summary>
    public static bool CanParse(ReadOnlySpan<byte> report)
        => report.Length >= MinimumLength(report.Length == 0 ? (byte)0 : report[0]);

    /// <summary>Shortest USB <c>0x01</c> report; anything shorter under that id is the Bluetooth one.</summary>
    /// <remarks>
    /// The whole reason this constant exists. Report <c>0x01</c> means two different things
    /// depending on the transport, and the id alone cannot tell them apart — only the length can.
    /// </remarks>
    private const int UsbReportLength = 32;

    /// <summary>
    /// The <c>0x01</c> report a DualSense sends over Bluetooth in compatibility mode.
    /// </summary>
    /// <remarks>
    /// Axes and buttons sit in the USB places, then a per-frame counter stands where the USB report
    /// has the system byte. 78 bytes — the same size as the full report, which is exactly what makes
    /// the two hard to tell apart. Measured on a DualSense connected over Bluetooth, byte 7 ran
    /// <c>0x24, 0x28, 0x2C, … 0x3C, 0x00, 0x04, …</c>: a six-bit counter stepping by four. Decoded as
    /// the system byte, its bit <c>0x04</c> fired the mute button — Quick Access, the mode-switch
    /// chord — on and off by itself, and the controller changed mode about twice a second with nobody
    /// touching it.
    ///
    /// <para>
    /// <b>The counter is the top of the byte, not the whole of it.</b> Measured on the bench on
    /// 15 August across thirteen manoeuvres: the counter occupies bits <c>0x3C</c>, and the two low
    /// bits are real inputs. <c>0x02</c> follows the touchpad being <i>touched</i> — it is the only
    /// bit that moves during a slide with no click, and the pad reports no coordinates at all in this
    /// shape, so the touchpad here is a contact and nothing more. <c>0x01</c> is the PS button:
    /// manoeuvre 14 of <c>docs/protocole-dualsense-bt.md</c> pressed PS, then mute, then the pad
    /// click, in that order, and only the PS presses moved the bit — the mute button is not emitted
    /// in this report shape at all. The two rest manoeuvres moved neither low bit (bits <c>0x3C</c>
    /// and nothing else), so decoding <c>0x01</c> cannot fire a button on a pad nobody is touching.
    /// </para>
    ///
    /// <para>
    /// <c>0x01</c> is decoded as <see cref="SteamControllerButtons.Steam"/> in <see cref="Parse"/>.
    /// The contact bit <c>0x02</c> is not decoded: the touchpad is a contactor, not a surface, in
    /// this shape, and it has no Xbox equivalent to be wired to without guessing. This does not
    /// touch the full Bluetooth report <c>0x31</c>, which is what turned the pipeline into lag on
    /// 12 August; the bit is read from the compatibility report the pad already sends at full rate.
    /// </para>
    /// </remarks>
    private const int CompactBluetoothReportLength = 78;

    private static int MinimumLength(byte reportId) => reportId switch
    {
        UsbReportId => 10,
        BluetoothReportId => 12,
        _ => int.MaxValue,
    };

    /// <summary>Where the fields sit in one particular report.</summary>
    private readonly record struct Layout(int Axes, int Buttons, int Triggers);

    /// <summary>
    /// Which of the three report shapes this is.
    /// </summary>
    /// <remarks>
    /// Three, not two. A DualSense on Bluetooth sends a compact <c>0x01</c> report until something
    /// asks it for the full one, and that compact report shares its id with the USB report while
    /// laying the fields out differently: buttons and triggers are swapped. The axes are in the same
    /// place in both, which is exactly what makes the mistake so misleading — the pointer moves
    /// correctly and then every button does something else. Read as a bitfield, a trigger byte is a
    /// fistful of buttons pressed at once.
    /// </remarks>
    private static Layout LayoutOf(ReadOnlySpan<byte> report)
    {
        if (report[0] == BluetoothReportId)
        {
            return new Layout(Axes: 2, Buttons: 9, Triggers: 6);
        }

        // Report 0x01 always carries the compact layout, whatever its length. The length test that
        // stood here was wrong and wrong silently: a real DualSense sends 0x01 in 78-byte frames
        // over Bluetooth, so the USB branch was taken, the buttons were read from byte 8 instead of
        // byte 5, and a resting pad decoded as DPadUp for ever. Measured on the author's pad:
        // [01 80 7F 80 82 08 00 20 ...] — byte 5 is 0x08, a centred hat, exactly where this says.
        return new Layout(Axes: 1, Buttons: 5, Triggers: 8);
    }

    /// <summary>
    /// Decodes one report.
    /// </summary>
    /// <param name="report">The raw HID report, including its id byte.</param>
    /// <param name="timestamp">When it arrived, relative to the start of the session.</param>
    /// <returns>The decoded state, or null when the report is not one this understands.</returns>
    /// <remarks>
    /// Null rather than an exception for an unknown report. A DualSense also emits feature and
    /// audio reports on the same pipe, and they are perfectly normal traffic — throwing would turn
    /// ordinary chatter into a crash.
    /// </remarks>
    public static ControllerState? Parse(ReadOnlySpan<byte> report, TimeSpan timestamp)
    {
        if (!CanParse(report))
        {
            return null;
        }

        var layout = LayoutOf(report);

        if (report.Length <= layout.Buttons + 1 || report.Length <= layout.Triggers + 1)
        {
            return null;
        }

        var leftStick = new NormalizedStick(Axis(report[layout.Axes]), -Axis(report[layout.Axes + 1]));
        var rightStick = new NormalizedStick(Axis(report[layout.Axes + 2]), -Axis(report[layout.Axes + 3]));

        var faceAndDpad = report[layout.Buttons];
        var shoulders = report[layout.Buttons + 1];

        // The triggers, from both places the pad states them.
        //
        // The analogue byte is the real source, and over Bluetooth in compatibility mode it is not
        // analogue at all: measured on 15 August, a slow full-travel press of L2 produced exactly two
        // values on its byte — 0x00 and 0xFF — and R2 three. There is no progression to read in that
        // report shape, and an analogue trigger threshold set in a profile cannot do anything there.
        //
        // The shoulder byte carries L2 and R2 as plain bits at 0x04 and 0x08, unread until now. They
        // agree with the byte whenever both are present, so nothing changes on USB; they are taken as
        // a floor rather than as the answer, so a pad that does send a real analogue value keeps it.
        var leftTrigger = Math.Max(report[layout.Triggers] / 255.0, (shoulders & 0x04) != 0 ? 1.0 : 0.0);
        var rightTrigger = Math.Max(report[layout.Triggers + 1] / 255.0, (shoulders & 0x08) != 0 ? 1.0 : 0.0);

        var buttons = SteamControllerButtons.None;

        // The face buttons sit in the high nibble. Named by position rather than by PlayStation
        // label: cross is where A is on an Xbox pad, and it is the position the user's thumb knows.
        if ((faceAndDpad & 0x20) != 0) buttons |= SteamControllerButtons.A;      // cross
        if ((faceAndDpad & 0x40) != 0) buttons |= SteamControllerButtons.B;      // circle
        if ((faceAndDpad & 0x10) != 0) buttons |= SteamControllerButtons.X;      // square
        if ((faceAndDpad & 0x80) != 0) buttons |= SteamControllerButtons.Y;      // triangle

        buttons |= DpadOf(faceAndDpad & 0x0F);

        if ((shoulders & 0x01) != 0) buttons |= SteamControllerButtons.LeftBumper;
        if ((shoulders & 0x02) != 0) buttons |= SteamControllerButtons.RightBumper;
        // The rule is positional and it is the same on every pad: View is the left of the two small
        // buttons, Menu is the right one. On a DualSense that is Create on the left and Options on
        // the right, which is what these two bits are.
        //
        // Briefly swapped on 12 August on a report of "menu et view inversé", then put back the same
        // minute: the swap was made before the rule had been stated, and it broke a mapping that
        // already obeyed it. Nothing here is to be flipped again without the physical left and right
        // being checked first.
        if ((shoulders & 0x10) != 0) buttons |= SteamControllerButtons.View;     // create, left
        if ((shoulders & 0x20) != 0) buttons |= SteamControllerButtons.Menu;     // options, right
        if ((shoulders & 0x40) != 0) buttons |= SteamControllerButtons.LeftStick;
        if ((shoulders & 0x80) != 0) buttons |= SteamControllerButtons.RightStick;

        // Third button byte: PS, touchpad click, mute. The three button bytes are consecutive in
        // both layouts, so it is always one past the shoulders wherever those are. Over USB it is
        // the system byte; over Bluetooth in compatibility mode the same byte carries a per-frame
        // counter on its top bits, but its low bit is still the PS button (see
        // <see cref="CompactBluetoothReportLength"/>). Both transports are read the same way.
        //
        // The PS button becomes Steam, the same flag a Steam Controller's Steam button produces, so
        // it reaches the launcher already wired to it rather than through a second path.
        //
        // It is NOT the mode-switch button, and turning it into one is a regression this line has
        // already seen once, on 14 August. Launching Steam is what this button is for on this family;
        // taking it for the switch takes the launcher away to solve a problem that belongs to
        // InputModeHandler, where the L3+R3 hold lives.
        //
        // Guarded on length: a truncated report must lose the button, not throw.
        if (report.Length > layout.Buttons + 2)
        {
            var system = report[layout.Buttons + 2];
            if ((system & 0x01) != 0) buttons |= SteamControllerButtons.Steam;   // PS

            // The mute button becomes Quick Access, and without this the DualSense could not change
            // mode at all: the switch chord is QuickAccess by default, and nothing on this pad
            // produced that flag — the whole session it was simply a button the controller did not
            // have. Reported 12 August as "switch profil xbox fonctionne mal".
            //
            // Mute rather than the touchpad click, which is the other unused control here. On a
            // Steam Controller Quick Access is a small dedicated button pressed on purpose; the
            // DualSense touchpad is a wide surface under the thumbs during play, and putting a mode
            // switch under it would change mode mid-game by accident. Mute is the pad's equivalent
            // spare button.
            // MUTE -> QuickAccess : retire le 14 aout, apres avoir ete ajoute le 12 sans preuve.
            //
            // L'intention etait bonne : la DualSense n'a aucun bouton produisant QuickAccess, donc
            // aucun moyen de changer de mode. Mais cet octet ne porte pas que des boutons. Sur le
            // rapport Bluetooth de compatibilite - celui qui arrive tant que le mode complet est
            // eteint - ses bits hauts sont un compteur de sequence qui change a chaque trame. Lire
            // un bouton dedans fait basculer le mode sans arret.
            //
            // A remettre uniquement apres avoir observe l'octet pendant qu'on presse mute, et rien
            // d'autre. Une manette qui bascule toute seule est pire qu'une manette qui ne bascule
            // pas : le bouton manquant se contourne, le mode qui saute rend le pad inutilisable.
        }

        return new ControllerState(
            timestamp,
            buttons,
            leftStick,
            rightStick,
            leftTrigger,
            rightTrigger,
            TouchpadSample.Released,
            TouchpadSample.Released);
    }

    /// <summary>
    /// The d-pad arrives as a hat switch: a direction from 0 to 7, and 8 for centred.
    /// </summary>
    /// <remarks>
    /// Not a bitfield, which is the mistake this shape invites. Treating it as one makes "up" (0)
    /// indistinguishable from "centred" and leaves the pad walking in one direction forever.
    /// </remarks>
    private static SteamControllerButtons DpadOf(int hat) => hat switch
    {
        0 => SteamControllerButtons.DPadUp,
        1 => SteamControllerButtons.DPadUp | SteamControllerButtons.DPadRight,
        2 => SteamControllerButtons.DPadRight,
        3 => SteamControllerButtons.DPadDown | SteamControllerButtons.DPadRight,
        4 => SteamControllerButtons.DPadDown,
        5 => SteamControllerButtons.DPadDown | SteamControllerButtons.DPadLeft,
        6 => SteamControllerButtons.DPadLeft,
        7 => SteamControllerButtons.DPadUp | SteamControllerButtons.DPadLeft,
        _ => SteamControllerButtons.None,
    };

    /// <summary>A stick byte, centred on 128, as -1..1 with the rest band flattened.</summary>
    private static double Axis(byte raw)
    {
        var value = (raw - 128.0) / 128.0;

        return Math.Abs(value) < RestBand ? 0 : Math.Clamp(value, -1.0, 1.0);
    }
}
