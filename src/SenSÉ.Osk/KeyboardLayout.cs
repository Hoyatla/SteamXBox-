using SenSÉ.Core.Osk;

namespace SenSÉ.Osk;

public enum SpecialAction { None, Shift, Backspace, Enter, Space, Tab, Sym }

/// <summary>
/// Shift behaviour, following the phone convention: one press capitalises the next character only,
/// a second press locks capitals until pressed again.
/// </summary>
/// <remarks>
/// The state picks the shifted character directly instead of holding the physical Shift key down.
/// Holding it meant that closing the overlay while shift was latched left the whole system stuck in
/// uppercase.
/// </remarks>
public enum ShiftMode
{
    Off,
    OneShot,
    Locked,
}

public sealed class KeyDef
{
    public int Row { get; }
    public int Col { get; }
    public string Label { get; }
    public char NormalChar { get; }
    public char ShiftedChar { get; }
    public char SymChar { get; }
    public SpecialAction Action { get; }
    public int Width { get; }

    public KeyDef(int row, int col, string label, char normal, char shifted, char sym = '\0', SpecialAction action = SpecialAction.None, int width = 1)
    {
        Row = row; Col = col; Label = label;
        NormalChar = normal; ShiftedChar = shifted; SymChar = sym;
        Action = action; Width = width;
    }
}

public static class KeyboardLayout
{
    public const int Rows = 5;

    /// <summary>
    /// Key count of the widest row, which is what the stick anchors divide.
    /// </summary>
    /// <remarks>
    /// Measured from the loaded layout rather than fixed: AZERTY, QWERTZ and the detected system
    /// layout do not all have the same number of keys, and a hard-coded width would leave the last
    /// column of the widest one unreachable.
    /// </remarks>
    public static int WidestRow =>
        Keys.Count == 0 ? 1 : Keys.GroupBy(k => k.Row).Max(g => g.Count());
    public const int MaxCols = 11;

    /// <summary>
    /// The columns one pad owns: the same halves the sticks aim from.
    /// </summary>
    /// <remarks>
    /// Taken from <see cref="StickAnchorLayout"/> rather than from a constant of its own, because
    /// "the same split as the sticks" has to be true by construction. Two independent definitions
    /// agreed on paper and disagreed in fact: the pads split at a <i>shared</i> column 5 — owned by
    /// both — while the sticks split 0-5 and 6-10 with no overlap, and the pads left the last row
    /// undivided so either thumb could reach Enter. Three differences between two keyboards that are
    /// supposed to feel like one.
    /// </remarks>
    public static (int First, int Last) ZoneFor(bool isLeftPad)
    {
        var anchors = StickAnchorLayout.Build(MaxCols, Rows);
        var anchor = anchors[Math.Min(StickAnchorLayout.AnchorFor(isLeftPad), anchors.Count - 1)];

        return (anchor.FirstColumn, anchor.LastColumn);
    }

    /// <summary>
    /// How far from the centre a thumb comfortably reaches, as a fraction of the reported range.
    /// </summary>
    /// <remarks>
    /// Not 1.0, and the difference is the whole point. Measured on the hardware by sweeping each pad
    /// edge to edge, the extremes reported were left X -0.88 to +0.81, Y -0.79 to +0.92; right X
    /// -0.87 to +0.98, Y -0.91 to +0.96. A mapping built on ±1.00 therefore places the outer columns
    /// past anything the thumb actually produces: the last key of each row can only be reached by
    /// pushing onto the very rim, if at all.
    ///
    /// <para>
    /// The four bounds also disagree with each other, so no single measured value would be right for
    /// all of them. This is deliberately a little inside the smallest of them: everything beyond
    /// clamps to the outer column, which costs a sliver of unused rim and buys an outer column that
    /// is comfortably reachable on every edge of both pads.
    /// </para>
    /// </remarks>
    public const double UsableReach = 0.80;

    /// <summary>
    /// Maps a point on the round pad to a point on the square zone, each axis 0 to 1.
    /// </summary>
    /// <remarks>
    /// The pads are discs and the zones are rectangles, so a direct mapping leaves the four corner
    /// keys of every zone outside anything a finger can produce: on a circle <c>|X|</c> and
    /// <c>|Y|</c> cannot both be large, which the measured extremes show plainly — Y stopped at 0.92
    /// while X was already at -0.88.
    ///
    /// <para>
    /// So the disc is stretched onto the square along each direction. The direction the thumb points
    /// is kept exactly; only how far out the rim lies is rescaled, from the circle's radius of 1 to
    /// the square's edge in that same direction, which is <c>1 / max(|x|,|y|)</c> of it. On the axes
    /// nothing changes at all; on the diagonals the rim now reaches the corner.
    /// </para>
    ///
    /// <para>
    /// The cost, worth naming: a given distance travelled by the thumb covers more keys diagonally
    /// than straight, because the diagonal was stretched the most. That is inherent to putting a
    /// square inside a circle, and the alternative is corner keys nobody can reach.
    /// </para>
    /// </remarks>
    public static (double X, double Y) NormalizePoint(double padX, double padY)
    {
        var x = padX / UsableReach;
        var y = padY / UsableReach;

        // Past the usable rim: pulled back onto it, keeping the direction. Clamping each axis on its
        // own would bend a diagonal push towards the nearest edge.
        var radius = Math.Sqrt((x * x) + (y * y));
        if (radius > 1.0)
        {
            x /= radius;
            y /= radius;
            radius = 1.0;
        }

        var longest = Math.Max(Math.Abs(x), Math.Abs(y));
        if (longest > 1e-9)
        {
            var stretch = radius / longest;
            x *= stretch;
            y *= stretch;
        }

        return (Math.Clamp((x + 1.0) / 2.0, 0.0, 1.0), Math.Clamp((y + 1.0) / 2.0, 0.0, 1.0));
    }

    /// <summary>The row a pad point selects.</summary>
    public static int RowFor(double padX, double padY)
    {
        var (_, y) = NormalizePoint(padX, padY);

        return Math.Clamp((int)(y * Rows), 0, Rows - 1);
    }

    /// <summary>
    /// Maps a pad point to a column within that pad's zone.
    /// </summary>
    /// <remarks>
    /// Edge to edge over the usable travel: the leftmost the thumb comfortably reaches is the zone's
    /// first column, the rightmost is its last, and past that it clamps. The thumb never has to
    /// leave the surface to reach a key.
    /// </remarks>
    public static int ColumnFor(double padX, double padY, bool isLeftPad)
    {
        var (x, _) = NormalizePoint(padX, padY);
        var (first, last) = ZoneFor(isLeftPad);
        var span = last - first + 1;

        return Math.Clamp(first + (int)(x * span), first, last);
    }

    /// <summary>Horizontal position of a pad's cursor, in pixels, for drawing it.</summary>
    /// <remarks>
    /// The same mapping as <see cref="ColumnFor"/>, in pixels rather than columns. Anything else and
    /// the dot the user is steering would sit somewhere other than the key it selects.
    /// </remarks>
    public static double CursorXFor(double padX, double padY, bool isLeftPad, double keyWidth)
    {
        var (x, _) = NormalizePoint(padX, padY);
        var (first, last) = ZoneFor(isLeftPad);
        var span = last - first + 1;

        return (first + (x * span)) * keyWidth;
    }

    /// <summary>Vertical position of a pad's cursor, in pixels, matching <see cref="RowFor"/>.</summary>
    public static double CursorYFor(double padX, double padY, double keyHeight)
    {
        var (_, y) = NormalizePoint(padX, padY);

        return y * keyHeight * Rows;
    }

    private static IReadOnlyList<KeyDef>? _detected;

    /// <summary>
    /// Layout the overlay imitates. Setting it discards the cached keys so the next paint rebuilds.
    /// </summary>
    /// <remarks>
    /// Detection is the default, but it cannot be the only option: someone typing French on a machine
    /// set to English still wants AZERTY under their thumbs, and an overlay whose legends do not match
    /// the user's mental model is slower than no overlay at all.
    /// </remarks>
    public static OskKeyboardLayout Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            _detected = null;
        }
    }

    private static OskKeyboardLayout _selected = OskKeyboardLayout.Auto;

    public static IReadOnlyList<KeyDef> Keys
    {
        get
        {
            if (_detected is not null) return _detected;

            if (_selected != OskKeyboardLayout.Auto)
            {
                _detected = KeyboardLayouts.Build(_selected);
                return _detected;
            }

            try { _detected = SystemKeyboardLayout.DetectLayout(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Layout detection failed: {ex.Message}");
                _detected = BuildFallback();
            }
            return _detected;
        }
    }

    public static KeyDef? FindKeyAt(int row, int col)
    {
        foreach (var k in Keys)
        {
            if (k.Row == row && col >= k.Col && col < k.Col + k.Width)
                return k;
        }
        return null;
    }

    private static List<KeyDef> BuildFallback()
    {
        var keys = new List<KeyDef>();

        // Row 0: numbers + '
        keys.Add(new KeyDef(0, 0, "1", '1', '+', '!'));
        keys.Add(new KeyDef(0, 1, "2", '2', '\"', '@'));
        keys.Add(new KeyDef(0, 2, "3", '3', '*', '#'));
        keys.Add(new KeyDef(0, 3, "4", '4', '\u00E7', '$'));
        keys.Add(new KeyDef(0, 4, "5", '5', '%', '%'));
        keys.Add(new KeyDef(0, 5, "6", '6', '&', '^'));
        keys.Add(new KeyDef(0, 6, "7", '7', '/', '&'));
        keys.Add(new KeyDef(0, 7, "8", '8', '(', '*'));
        keys.Add(new KeyDef(0, 8, "9", '9', ')', '('));
        keys.Add(new KeyDef(0, 9, "0", '0', '=', ')'));
        keys.Add(new KeyDef(0, 10, "'", '\'', '?', '?'));

        // Row 1: QWERTZ + ü/è
        keys.Add(new KeyDef(1, 0, "Q", 'q', 'Q', '['));
        keys.Add(new KeyDef(1, 1, "W", 'w', 'W', ']'));
        keys.Add(new KeyDef(1, 2, "E", 'e', 'E', '{'));
        keys.Add(new KeyDef(1, 3, "R", 'r', 'R', '}'));
        keys.Add(new KeyDef(1, 4, "T", 't', 'T', '~'));
        keys.Add(new KeyDef(1, 5, "Z", 'z', 'Z', '`'));
        keys.Add(new KeyDef(1, 6, "U", 'u', 'U', '|'));
        keys.Add(new KeyDef(1, 7, "I", 'i', 'I', '\\'));
        keys.Add(new KeyDef(1, 8, "O", 'o', 'O', '<'));
        keys.Add(new KeyDef(1, 9, "P", 'p', 'P', '>'));
        keys.Add(new KeyDef(1, 10, "Ü", 'ü', '\u00E8', '\u20AC')); // ü/è, €

        // Row 2: home row + é/ö + à/ä
        keys.Add(new KeyDef(2, 0, "A", 'a', 'A', ';'));
        keys.Add(new KeyDef(2, 1, "S", 's', 'S', ':'));
        keys.Add(new KeyDef(2, 2, "D", 'd', 'D', '\u00B0')); // °
        keys.Add(new KeyDef(2, 3, "F", 'f', 'F', '\u00A7')); // §
        keys.Add(new KeyDef(2, 4, "G", 'g', 'G', '\u00B1')); // ±
        keys.Add(new KeyDef(2, 5, "H", 'h', 'H', '\u00D7')); // ×
        keys.Add(new KeyDef(2, 6, "J", 'j', 'J', '\u00F7')); // ÷
        keys.Add(new KeyDef(2, 7, "K", 'k', 'K', '='));
        keys.Add(new KeyDef(2, 8, "L", 'l', 'L', '+'));
        keys.Add(new KeyDef(2, 9, "É", 'é', '\u00F6', '\u00A3')); // é/ö, £
        keys.Add(new KeyDef(2, 10, "À", 'à', '\u00E4', '\u00A9')); // à/ä, ©

        // Row 3: bottom row + $/£
        keys.Add(new KeyDef(3, 0, "Y", 'y', 'Y', '-'));
        keys.Add(new KeyDef(3, 1, "X", 'x', 'X', '_'));
        keys.Add(new KeyDef(3, 2, "C", 'c', 'C', '/'));
        keys.Add(new KeyDef(3, 3, "V", 'v', 'V', '\"'));
        keys.Add(new KeyDef(3, 4, "B", 'b', 'B', '\''));
        keys.Add(new KeyDef(3, 5, "N", 'n', 'N', '\u00B5')); // µ
        keys.Add(new KeyDef(3, 6, "M", 'm', 'M', '\u00AE')); // ®
        keys.Add(new KeyDef(3, 7, ",", ',', ';', ','));
        keys.Add(new KeyDef(3, 8, ".", '.', ':', '.'));
        keys.Add(new KeyDef(3, 9, "-", '-', '_', '?'));
        keys.Add(new KeyDef(3, 10, "$", '$', '\u00A3', '\u00A3')); // £

        // Row 4: special (11 cols: 2+2+2+2+3)
        keys.Add(new KeyDef(4, 0, "SHIFT", '\0', '\0', '\0', SpecialAction.Shift, 2));
        keys.Add(new KeyDef(4, 2, "BSP", '\0', '\0', '\0', SpecialAction.Backspace, 2));
        keys.Add(new KeyDef(4, 4, "SYM", '\0', '\0', '\0', SpecialAction.Sym, 2));
        keys.Add(new KeyDef(4, 6, "ESPACE", ' ', ' ', ' ', SpecialAction.Space, 2));
        keys.Add(new KeyDef(4, 8, "ENTRÉE", '\0', '\0', '\0', SpecialAction.Enter, 3));

        return keys;
    }
}
