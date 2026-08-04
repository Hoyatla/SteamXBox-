namespace Sc2Xboxed.Osk;

/// <summary>Which physical keyboard the overlay imitates.</summary>
public enum OskKeyboardLayout
{
    /// <summary>Read the layout Windows is currently using.</summary>
    Auto,

    /// <summary>Swiss QWERTZ. The US layout is close enough to it to share a mental model.</summary>
    SwissQwertz,

    /// <summary>French AZERTY.</summary>
    FrenchAzerty,

    /// <summary>US QWERTY.</summary>
    UsQwerty,
}

/// <summary>
/// Explicit key layouts, so the overlay can imitate a real keyboard instead of an approximation.
/// </summary>
/// <remarks>
/// Detection from the Windows layout stays available and is the default, but it is not enough on its
/// own: a user typing French on a machine set to English still wants AZERTY under their thumbs, and
/// the overlay is only useful if its keys sit where the eye expects them.
///
/// All three share the same 11 by 5 grid so the pad-to-key mapping, the two cursors and the shared
/// column split do not have to change per layout.
/// </remarks>
public static class KeyboardLayouts
{
    public static List<KeyDef> Build(OskKeyboardLayout layout) => layout switch
    {
        OskKeyboardLayout.FrenchAzerty => FrenchAzerty(),
        OskKeyboardLayout.UsQwerty => UsQwerty(),
        _ => SwissQwertz(),
    };

    /// <summary>Rows 4 is identical everywhere: it carries the modifiers, not characters.</summary>
    private static void AddSpecialRow(List<KeyDef> keys)
    {
        keys.Add(new KeyDef(4, 0, "SYM", '\0', '\0', '\0', SpecialAction.Sym, 2));
        keys.Add(new KeyDef(4, 2, "Tab", '\0', '\0', '\0', SpecialAction.Tab));
        keys.Add(new KeyDef(4, 3, "Space", ' ', ' ', ' ', SpecialAction.Space, 4));
        keys.Add(new KeyDef(4, 7, "Shift", '\0', '\0', '\0', SpecialAction.Shift, 2));
        keys.Add(new KeyDef(4, 9, "Back", '\0', '\0', '\0', SpecialAction.Backspace));
        keys.Add(new KeyDef(4, 10, "Enter", '\0', '\0', '\0', SpecialAction.Enter));
    }

    private static List<KeyDef> SwissQwertz()
    {
        var k = new List<KeyDef>();

        Row(k, 0, [("1", '1', '+'), ("2", '2', '"'), ("3", '3', '*'), ("4", '4', 'ç'), ("5", '5', '%'),
                   ("6", '6', '&'), ("7", '7', '/'), ("8", '8', '('), ("9", '9', ')'), ("0", '0', '='),
                   ("'", '\'', '?')],
             ['!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '?']);

        Row(k, 1, [("Q", 'q', 'Q'), ("W", 'w', 'W'), ("E", 'e', 'E'), ("R", 'r', 'R'), ("T", 't', 'T'),
                   ("Z", 'z', 'Z'), ("U", 'u', 'U'), ("I", 'i', 'I'), ("O", 'o', 'O'), ("P", 'p', 'P'),
                   ("Ü", 'ü', 'è')],
             ['[', ']', '{', '}', '~', '`', '|', '\\', '<', '>', '€']);

        Row(k, 2, [("A", 'a', 'A'), ("S", 's', 'S'), ("D", 'd', 'D'), ("F", 'f', 'F'), ("G", 'g', 'G'),
                   ("H", 'h', 'H'), ("J", 'j', 'J'), ("K", 'k', 'K'), ("L", 'l', 'L'), ("É", 'é', 'ö'),
                   ("À", 'à', 'ä')],
             [';', ':', '°', '§', '±', '×', '÷', '=', '+', '£', '©']);

        Row(k, 3, [("Y", 'y', 'Y'), ("X", 'x', 'X'), ("C", 'c', 'C'), ("V", 'v', 'V'), ("B", 'b', 'B'),
                   ("N", 'n', 'N'), ("M", 'm', 'M'), (",", ',', ';'), (".", '.', ':'), ("-", '-', '_'),
                   ("$", '$', '£')],
             ['-', '_', '/', '"', '\'', '!', '?', '<', '>', '#', '€']);

        AddSpecialRow(k);
        return k;
    }

    private static List<KeyDef> FrenchAzerty()
    {
        var k = new List<KeyDef>();

        // The digit row is shifted on AZERTY: unshifted gives the accented letters, shift gives the
        // digits. Reproduced as printed, because a keyboard that lies about its own legends is worse
        // than no keyboard.
        Row(k, 0, [("&", '&', '1'), ("É", 'é', '2'), ("\"", '"', '3'), ("'", '\'', '4'), ("(", '(', '5'),
                   ("-", '-', '6'), ("È", 'è', '7'), ("_", '_', '8'), ("Ç", 'ç', '9'), ("À", 'à', '0'),
                   (")", ')', '°')],
             ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', ']']);

        Row(k, 1, [("A", 'a', 'A'), ("Z", 'z', 'Z'), ("E", 'e', 'E'), ("R", 'r', 'R'), ("T", 't', 'T'),
                   ("Y", 'y', 'Y'), ("U", 'u', 'U'), ("I", 'i', 'I'), ("O", 'o', 'O'), ("P", 'p', 'P'),
                   ("^", '^', '¨')],
             ['@', '€', '#', '{', '[', '|', '`', '\\', '^', '@', ']']);

        Row(k, 2, [("Q", 'q', 'Q'), ("S", 's', 'S'), ("D", 'd', 'D'), ("F", 'f', 'F'), ("G", 'g', 'G'),
                   ("H", 'h', 'H'), ("J", 'j', 'J'), ("K", 'k', 'K'), ("L", 'l', 'L'), ("M", 'm', 'M'),
                   ("Ù", 'ù', '%')],
             [';', ':', '°', '§', '±', '×', '÷', '=', '+', '£', '¤']);

        Row(k, 3, [("W", 'w', 'W'), ("X", 'x', 'X'), ("C", 'c', 'C'), ("V", 'v', 'V'), ("B", 'b', 'B'),
                   ("N", 'n', 'N'), (",", ',', '?'), (";", ';', '.'), (":", ':', '/'), ("!", '!', '§'),
                   ("=", '=', '+')],
             ['<', '>', '/', '"', '\'', '!', '?', '.', '/', '#', '€']);

        AddSpecialRow(k);
        return k;
    }

    private static List<KeyDef> UsQwerty()
    {
        var k = new List<KeyDef>();

        Row(k, 0, [("1", '1', '!'), ("2", '2', '@'), ("3", '3', '#'), ("4", '4', '$'), ("5", '5', '%'),
                   ("6", '6', '^'), ("7", '7', '&'), ("8", '8', '*'), ("9", '9', '('), ("0", '0', ')'),
                   ("-", '-', '_')],
             ['!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_']);

        Row(k, 1, [("Q", 'q', 'Q'), ("W", 'w', 'W'), ("E", 'e', 'E'), ("R", 'r', 'R'), ("T", 't', 'T'),
                   ("Y", 'y', 'Y'), ("U", 'u', 'U'), ("I", 'i', 'I'), ("O", 'o', 'O'), ("P", 'p', 'P'),
                   ("[", '[', '{')],
             ['[', ']', '{', '}', '~', '`', '|', '\\', '<', '>', '€']);

        Row(k, 2, [("A", 'a', 'A'), ("S", 's', 'S'), ("D", 'd', 'D'), ("F", 'f', 'F'), ("G", 'g', 'G'),
                   ("H", 'h', 'H'), ("J", 'j', 'J'), ("K", 'k', 'K'), ("L", 'l', 'L'), (";", ';', ':'),
                   ("'", '\'', '"')],
             [';', ':', '°', '§', '±', '×', '÷', '=', '+', '£', '©']);

        Row(k, 3, [("Z", 'z', 'Z'), ("X", 'x', 'X'), ("C", 'c', 'C'), ("V", 'v', 'V'), ("B", 'b', 'B'),
                   ("N", 'n', 'N'), ("M", 'm', 'M'), (",", ',', '<'), (".", '.', '>'), ("/", '/', '?'),
                   ("\\", '\\', '|')],
             ['-', '_', '/', '"', '\'', '!', '?', '<', '>', '#', '€']);

        AddSpecialRow(k);
        return k;
    }

    private static void Row(
        List<KeyDef> keys,
        int row,
        (string Label, char Normal, char Shifted)[] cells,
        char[] symbols)
    {
        for (var column = 0; column < cells.Length; column++)
        {
            var symbol = column < symbols.Length ? symbols[column] : '\0';
            keys.Add(new KeyDef(row, column, cells[column].Label, cells[column].Normal, cells[column].Shifted, symbol));
        }
    }
}
