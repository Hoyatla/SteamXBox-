using System.Runtime.InteropServices;
using System.Text;

namespace SenSÉ.Desktop.Input;

/// <summary>
/// Finds which physical key produces a character on the keyboard the user actually has.
/// </summary>
/// <remarks>
/// A shortcut bound to a virtual key code is bound to a different character on every layout. The
/// section key is <c>0xBF</c> on Swiss French, somewhere else on German, and on a US keyboard it
/// does not exist at all — so the code has to be looked up on the machine rather than written down.
/// </remarks>
public static class KeyboardLayout
{
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint MAPVK_VSC_TO_VK_EX = 3;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    /// <summary>
    /// The key left of <c>1</c>, by position rather than by what it prints.
    /// </summary>
    /// <remarks>
    /// A scan code is the key itself, the same hole in the plastic whatever the layout prints on it.
    /// This one holds the section key on Swiss and German keyboards, <c>²</c> on French, a backquote
    /// on US — always a key nothing important is bound to, which is what makes it the right fallback.
    /// </remarks>
    private const uint ScanCodeLeftOfOne = 0x29;

    /// <summary>
    /// The virtual key that types <paramref name="character"/> with no modifier held, or the key
    /// left of <c>1</c> when this layout has no such key.
    /// </summary>
    /// <remarks>
    /// The whole scan, rather than <c>VkKeyScanEx</c>, and that is not caution — it is a measured
    /// correction. On Swiss French the section sign has two routes: the dedicated key, pressed
    /// alone, and <c>AltGr+5</c>. <c>VkKeyScanEx</c> walks the virtual key codes in order and answers
    /// with the first route it meets, which is the digit — so the obvious one-line implementation
    /// returns a key needing two modifiers, and a double tap on it can never be recognised.
    ///
    /// <para>
    /// A gesture also has to be reachable with nothing held down. A key needing Shift or AltGr puts a
    /// modifier press between the two taps, and any key between them cancels the gesture — as it
    /// must, or shortcuts would fire in the middle of ordinary typing.
    /// </para>
    /// </remarks>
    public static int VirtualKeyFor(char character, Action<string>? log = null)
    {
        var layout = GetKeyboardLayout(0);

        for (uint vk = 0x08; vk <= 0xFF; vk++)
        {
            var scan = MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC, layout);

            if (scan == 0 || !Types(vk, scan, layout, character))
            {
                continue;
            }

            log?.Invoke($"'{character}' is VK 0x{vk:X2} on this layout (0x{layout.ToInt64():X}).");

            return (int)vk;
        }

        var fallback = (int)MapVirtualKeyExW(ScanCodeLeftOfOne, MAPVK_VSC_TO_VK_EX, layout);

        log?.Invoke(
            $"'{character}' needs a modifier on this layout (0x{layout.ToInt64():X}), which a double "
            + $"tap cannot use. Falling back to the key left of 1, VK 0x{fallback:X2}.");

        return fallback;
    }

    /// <summary>Whether this key, pressed alone, types that character.</summary>
    private static bool Types(uint vk, uint scan, IntPtr layout, char character)
    {
        var state = new byte[256];

        state[VK_SHIFT] = 0;
        state[VK_CONTROL] = 0;
        state[VK_MENU] = 0;

        var buffer = new StringBuilder(8);

        // Called twice, and only the second answer is trusted. A dead key — the circumflex, the
        // trema — leaves the layout waiting for the letter it will accent, and that leftover state
        // corrupts whichever key is probed next. The second call clears it.
        ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, 0, layout);
        buffer.Clear();

        var count = ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, 0, layout);

        return count == 1 && buffer[0] == character;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint vk, uint scan, byte[] state, StringBuilder buffer, int size, uint flags, IntPtr layout);
}
