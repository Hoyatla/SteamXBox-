namespace Sc2Xboxed.Osk;

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
    public const int MaxCols = 11;

    /// <summary>
    /// Column shared by both pads: 6 / Z / H / N on the detected layouts. The left pad owns columns
    /// 0 to this one, the right pad this one to the last, so each thumb covers its own half of the
    /// board and neither has to cross the whole width.
    /// </summary>
    public const int SharedColumn = 5;

    /// <summary>The last row holds the editing keys and stays reachable from both pads.</summary>
    public static bool IsSpecialRow(int row) => row == Rows - 1;

    /// <summary>
    /// Maps a pad's horizontal position to a column within that pad's zone.
    /// </summary>
    public static int ColumnFor(double padX, bool isLeftPad, int row)
    {
        var normalized = Math.Clamp((padX + 1.0) / 2.0, 0.0, 1.0);

        // Space, Enter, Backspace, Shift and Sym must stay under either thumb, so the special row is
        // not split: both pads span its full width.
        if (IsSpecialRow(row))
        {
            return Math.Clamp((int)(normalized * MaxCols), 0, MaxCols - 1);
        }

        if (isLeftPad)
        {
            var span = SharedColumn + 1;
            return Math.Clamp((int)(normalized * span), 0, SharedColumn);
        }

        var rightSpan = MaxCols - SharedColumn;
        return Math.Clamp(SharedColumn + (int)(normalized * rightSpan), SharedColumn, MaxCols - 1);
    }

    /// <summary>Horizontal centre of a pad's zone, in pixels, for drawing its cursor.</summary>
    public static double CursorXFor(double padX, bool isLeftPad, int row, double keyWidth)
    {
        var normalized = Math.Clamp((padX + 1.0) / 2.0, 0.0, 1.0);

        if (IsSpecialRow(row))
        {
            return normalized * keyWidth * MaxCols;
        }

        return isLeftPad
            ? normalized * keyWidth * (SharedColumn + 1)
            : keyWidth * SharedColumn + normalized * keyWidth * (MaxCols - SharedColumn);
    }

    private static IReadOnlyList<KeyDef>? _detected;

    public static IReadOnlyList<KeyDef> Keys
    {
        get
        {
            if (_detected is not null) return _detected;
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
