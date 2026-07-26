namespace Sc2Xboxed.Osk;

public enum SpecialAction { None, Shift, Backspace, Enter, Space, Tab }

public sealed class KeyDef
{
    public int Row { get; }
    public int Col { get; }
    public string Label { get; }
    public char NormalChar { get; }
    public char ShiftedChar { get; }
    public SpecialAction Action { get; }
    public int Width { get; }

    public KeyDef(int row, int col, string label, char normal, char shifted, SpecialAction action = SpecialAction.None, int width = 1)
    {
        Row = row; Col = col; Label = label;
        NormalChar = normal; ShiftedChar = shifted;
        Action = action; Width = width;
    }
}

public static class KeyboardLayout
{
    public const int Rows = 5;
    public const int MaxCols = 10;

    public static IReadOnlyList<KeyDef> Keys { get; } = BuildAzerty();

    private static List<KeyDef> BuildAzerty()
    {
        var keys = new List<KeyDef>();

        // Row 0: numbers
        keys.Add(new KeyDef(0, 0, "1", '1', '!'));
        keys.Add(new KeyDef(0, 1, "2", '2', '@'));
        keys.Add(new KeyDef(0, 2, "3", '3', '#'));
        keys.Add(new KeyDef(0, 3, "4", '4', '$'));
        keys.Add(new KeyDef(0, 4, "5", '5', '%'));
        keys.Add(new KeyDef(0, 5, "6", '6', '^'));
        keys.Add(new KeyDef(0, 6, "7", '7', '&'));
        keys.Add(new KeyDef(0, 7, "8", '8', '*'));
        keys.Add(new KeyDef(0, 8, "9", '9', '('));
        keys.Add(new KeyDef(0, 9, "0", '0', ')'));

        // Row 1: AZERTY top
        keys.Add(new KeyDef(1, 0, "A", 'a', 'A'));
        keys.Add(new KeyDef(1, 1, "Z", 'z', 'Z'));
        keys.Add(new KeyDef(1, 2, "E", 'e', 'E'));
        keys.Add(new KeyDef(1, 3, "R", 'r', 'R'));
        keys.Add(new KeyDef(1, 4, "T", 't', 'T'));
        keys.Add(new KeyDef(1, 5, "Y", 'y', 'Y'));
        keys.Add(new KeyDef(1, 6, "U", 'u', 'U'));
        keys.Add(new KeyDef(1, 7, "I", 'i', 'I'));
        keys.Add(new KeyDef(1, 8, "O", 'o', 'O'));
        keys.Add(new KeyDef(1, 9, "P", 'p', 'P'));

        // Row 2: home row
        keys.Add(new KeyDef(2, 0, "Q", 'q', 'Q'));
        keys.Add(new KeyDef(2, 1, "S", 's', 'S'));
        keys.Add(new KeyDef(2, 2, "D", 'd', 'D'));
        keys.Add(new KeyDef(2, 3, "F", 'f', 'F'));
        keys.Add(new KeyDef(2, 4, "G", 'g', 'G'));
        keys.Add(new KeyDef(2, 5, "H", 'h', 'H'));
        keys.Add(new KeyDef(2, 6, "J", 'j', 'J'));
        keys.Add(new KeyDef(2, 7, "K", 'k', 'K'));
        keys.Add(new KeyDef(2, 8, "L", 'l', 'L'));
        keys.Add(new KeyDef(2, 9, "M", 'm', 'M'));

        // Row 3: bottom alpha
        keys.Add(new KeyDef(3, 0, "W", 'w', 'W'));
        keys.Add(new KeyDef(3, 1, "X", 'x', 'X'));
        keys.Add(new KeyDef(3, 2, "C", 'c', 'C'));
        keys.Add(new KeyDef(3, 3, "V", 'v', 'V'));
        keys.Add(new KeyDef(3, 4, "B", 'b', 'B'));
        keys.Add(new KeyDef(3, 5, "N", 'n', 'N'));
        keys.Add(new KeyDef(3, 6, ",", ',', '?'));
        keys.Add(new KeyDef(3, 7, ".", '.', '>'));
        keys.Add(new KeyDef(3, 8, ";", ';', ':'));
        keys.Add(new KeyDef(3, 9, ":", ':', '/'));

        // Row 4: special keys (wider)
        keys.Add(new KeyDef(4, 0, "SHIFT", '\0', '\0', SpecialAction.Shift, 2));
        keys.Add(new KeyDef(4, 2, "←", '\0', '\0', SpecialAction.Backspace, 2));
        keys.Add(new KeyDef(4, 4, "ESPACE", ' ', ' ', SpecialAction.Space, 3));
        keys.Add(new KeyDef(4, 7, "TAB", '\0', '\0', SpecialAction.Tab, 1));
        keys.Add(new KeyDef(4, 8, "ENTRÉE", '\0', '\0', SpecialAction.Enter, 2));

        return keys;
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
}
