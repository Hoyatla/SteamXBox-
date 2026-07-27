using System.Runtime.InteropServices;
using System.Text;

namespace Sc2Xboxed.Osk;

public static class SystemKeyboardLayout
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private const int MAPVK_VK_TO_VSC = 0;

    private struct KeyMapping
    {
        public int Row, Col, Width;
        public uint Vk;
        public string? Label;
        public SpecialAction Action;
    }

    private static readonly KeyMapping[] Grid =
    [
        // Row 0: numbers + ' (11 cols, Swiss French)
        new() { Row = 0, Col = 0, Vk = 0x31 },
        new() { Row = 0, Col = 1, Vk = 0x32 },
        new() { Row = 0, Col = 2, Vk = 0x33 },
        new() { Row = 0, Col = 3, Vk = 0x34 },
        new() { Row = 0, Col = 4, Vk = 0x35 },
        new() { Row = 0, Col = 5, Vk = 0x36 },
        new() { Row = 0, Col = 6, Vk = 0x37 },
        new() { Row = 0, Col = 7, Vk = 0x38 },
        new() { Row = 0, Col = 8, Vk = 0x39 },
        new() { Row = 0, Col = 9, Vk = 0x30 },
        new() { Row = 0, Col = 10, Vk = 0xDB },  // OEM_4 ('/?) Swiss French

        // Row 1: QWERTZ + ü (11 cols)
        new() { Row = 1, Col = 0, Vk = 0x51 },
        new() { Row = 1, Col = 1, Vk = 0x57 },
        new() { Row = 1, Col = 2, Vk = 0x45 },
        new() { Row = 1, Col = 3, Vk = 0x52 },
        new() { Row = 1, Col = 4, Vk = 0x54 },
        new() { Row = 1, Col = 5, Vk = 0x59 },  // Y→Z on QWERTZ
        new() { Row = 1, Col = 6, Vk = 0x55 },
        new() { Row = 1, Col = 7, Vk = 0x49 },
        new() { Row = 1, Col = 8, Vk = 0x4F },
        new() { Row = 1, Col = 9, Vk = 0x50 },
        new() { Row = 1, Col = 10, Vk = 0xBA },  // OEM_1 (ü/è) Swiss French

        // Row 2: home row + é + à (11 cols)
        new() { Row = 2, Col = 0, Vk = 0x41 },
        new() { Row = 2, Col = 1, Vk = 0x53 },
        new() { Row = 2, Col = 2, Vk = 0x44 },
        new() { Row = 2, Col = 3, Vk = 0x46 },
        new() { Row = 2, Col = 4, Vk = 0x47 },
        new() { Row = 2, Col = 5, Vk = 0x48 },
        new() { Row = 2, Col = 6, Vk = 0x4A },
        new() { Row = 2, Col = 7, Vk = 0x4B },
        new() { Row = 2, Col = 8, Vk = 0x4C },
        new() { Row = 2, Col = 9, Vk = 0xC0 },  // OEM_3 (é/ö) Swiss French
        new() { Row = 2, Col = 10, Vk = 0xDE },  // OEM_7 (à/ä) Swiss French

        // Row 3: bottom row (Z→Y + punctuation, 11 cols)
        new() { Row = 3, Col = 0, Vk = 0x5A },  // Z→Y on QWERTZ
        new() { Row = 3, Col = 1, Vk = 0x58 },
        new() { Row = 3, Col = 2, Vk = 0x43 },
        new() { Row = 3, Col = 3, Vk = 0x56 },
        new() { Row = 3, Col = 4, Vk = 0x42 },
        new() { Row = 3, Col = 5, Vk = 0x4E },
        new() { Row = 3, Col = 6, Vk = 0x4D },
        new() { Row = 3, Col = 7, Vk = 0xBC },  // ,/;
        new() { Row = 3, Col = 8, Vk = 0xBE },  // .//
        new() { Row = 3, Col = 9, Vk = 0xBD },  // -/_
        new() { Row = 3, Col = 10, Vk = 0xBF },  // OEM_2 ($/£) Swiss French

        // Row 4: special (11 cols: 2+2+2+2+3)
        new() { Row = 4, Col = 0, Vk = 0xA0, Width = 2, Label = "SHIFT", Action = SpecialAction.Shift },
        new() { Row = 4, Col = 2, Vk = 0x08, Width = 2, Label = "←", Action = SpecialAction.Backspace },
        new() { Row = 4, Col = 4, Vk = 0xDF, Width = 2, Label = "SYM", Action = SpecialAction.Sym },
        new() { Row = 4, Col = 6, Vk = 0x20, Width = 2, Label = "ESPACE", Action = SpecialAction.Space },
        new() { Row = 4, Col = 8, Vk = 0x0D, Width = 3, Label = "ENTRÉE", Action = SpecialAction.Enter },
    ];

    private static readonly char[] SymLayer =
    [
        // Row 0: ! @ # $ % ^ & * ( ) ?
        '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '?',
        // Row 1: [ ] { } ~ ` | \ < > € µ
        '[', ']', '{', '}', '~', '`', '|', '\\', '<', '>', '\u20AC',
        // Row 2: ; : ° § ± × ÷ = + £ ©
        ';', ':', '\u00B0', '\u00A7', '\u00B1', '\u00D7', '\u00F7', '=', '+', '\u00A3', '\u00A9',
        // Row 3: - _ / " ' µ ® , . ? ¢
        '-', '_', '/', '"', '\'', '\u00B5', '\u00AE', ',', '.', '?', '\u00A2',
    ];

    public static IReadOnlyList<KeyDef> DetectLayout()
    {
        var hkl = GetKeyboardLayout(0);
        var state = new byte[256];
        GetKeyboardState(state);
        var keys = new List<KeyDef>();
        int symIdx = 0;

        foreach (var m in Grid)
        {
            if (m.Action != SpecialAction.None)
            {
                keys.Add(new KeyDef(m.Row, m.Col, m.Label ?? "", '\0', '\0', '\0', m.Action, m.Width));
                continue;
            }

            uint sc = MapVirtualKeyW(m.Vk, MAPVK_VK_TO_VSC);
            string normal = GetChar(state, m.Vk, sc, hkl, false);
            string shifted = GetChar(state, m.Vk, sc, hkl, true);

            string label;
            char normalChar, shiftedChar;

            if (!string.IsNullOrEmpty(shifted) && shifted[0] >= 0x20)
            {
                label = shifted.ToUpperInvariant();
                shiftedChar = shifted[0];
                normalChar = string.IsNullOrEmpty(normal) || normal[0] < 0x20 ? shifted[0] : normal[0];
            }
            else if (!string.IsNullOrEmpty(normal) && normal[0] >= 0x20)
            {
                label = normal.ToUpperInvariant();
                normalChar = normal[0];
                shiftedChar = normal[0];
            }
            else
            {
                label = $"0x{m.Vk:X2}";
                normalChar = '\0';
                shiftedChar = '\0';
            }

            char symChar = symIdx < SymLayer.Length ? SymLayer[symIdx++] : '\0';

            keys.Add(new KeyDef(m.Row, m.Col, label, normalChar, shiftedChar, symChar, SpecialAction.None, m.Width));
        }

        return keys;
    }

    private static string GetChar(byte[] state, uint vk, uint sc, IntPtr hkl, bool shift)
    {
        var buf = new StringBuilder(8);
        var ks = (byte[])state.Clone();
        if (shift) ks[0x10] |= 0x80;

        int ret = ToUnicodeEx(vk, sc, ks, buf, buf.Capacity, 0, hkl);
        return ret > 0 ? buf.ToString(0, ret) : "";
    }
}
