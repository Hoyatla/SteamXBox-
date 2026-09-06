using System.Runtime.InteropServices;
using System.Text;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Pilotage du clavier : taper du texte, appuyer sur des touches speciales
/// avec modificateurs.
/// </summary>
/// <remarks>
/// <b>SendInput pour les touches virtuelles.</b> keybd_event est obsolete
/// et SendInput est la voie documentee. Pour taper du texte, on utilise
/// Unicode SendInput (KEYEVENTF_UNICODE) qui passe par toutes les couches
/// sans dependre du layout actif.
///
/// <para><b>Mode exclusif optionnel.</b> Le bandeau ModeExclusif est purement
/// visuel (avertissement a l'utilisateur). Les actions clavier/souris
/// marchent sans lui, pour permettre un tap rapide dans une webapp deja
/// ouverte (cas frequent : ecrire dans le chat de MiniMax Code, OpenCode, etc.).</para>
/// </remarks>
public static class Clavier
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>Tape une chaine de caracteres, un caractere a la fois, via Unicode SendInput.</summary>
    public static string Taper(string texte)
    {
        if (string.IsNullOrEmpty(texte)) return "rien a taper";

        foreach (var c in texte)
        {
            EnvoyerTouche(0, c, false);
            EnvoyerTouche(0, c, true);
            Thread.Sleep(5);
        }
        return $"tape {texte.Length} caractere(s)";
    }

    /// <summary>Appuie sur une touche speciale (Entree, Echap, Tab, F1..F12, fleches) avec modificateurs.</summary>
    /// <remarks>
    /// <b>« Ctrl+S » passe aussi.</b> Cette methode attend la touche et ses modificateurs
    /// separement, alors que la capacite voisine debug_uia_press accepte la combinaison
    /// ecrite d'un seul tenant. Deux verbes qui font la meme chose avec deux conventions,
    /// et le modele choisit la mauvaise : le 6 septembre 2026 il a essaye
    /// <c>touche="Ctrl+S"</c> deux fois, recu « touche inconnue » deux fois, puis a
    /// renonce a enregistrer. Plutot que d'esperer qu'il retienne la difference, on
    /// accepte les deux formes.
    /// </remarks>
    public static string Toucher(string touche, IReadOnlyList<string> modificateurs)
    {
        // Une combinaison ecrite dans « touche » : on la scinde, le dernier morceau est la
        // touche, les precedents s'ajoutent aux modificateurs recus par ailleurs.
        if (touche.Contains('+'))
        {
            var morceaux = touche.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (morceaux.Length >= 2)
            {
                touche = morceaux[^1];
                modificateurs = [.. modificateurs, .. morceaux[..^1]];
            }
        }

        var vk = ParseTouche(touche);
        if (vk == 0) return $"touche inconnue: {touche}";

        var mods = modificateurs
            .Select(m => m.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 0x11u,
                "shift" => 0x10u,
                "alt" => 0x12u,
                "win" => 0x5Bu,
                _ => 0u,
            })
            .Where(v => v != 0)
            .ToList();

        foreach (var mod in mods)
        {
            EnvoyerTouche((ushort)mod, '\0', false);
        }
        EnvoyerTouche((ushort)vk, '\0', false);
        Thread.Sleep(10);
        EnvoyerTouche((ushort)vk, '\0', true);
        foreach (var mod in mods.AsEnumerable().Reverse())
        {
            EnvoyerTouche((ushort)mod, '\0', true);
        }
        return $"touche {touche} avec {mods.Count} modificateur(s)";
    }

    private static ushort ParseTouche(string touche)
    {
        return touche.ToLowerInvariant() switch
        {
            "entree" or "enter" or "return" => 0x0D,
            "echap" or "escape" or "esc" => 0x1B,
            "tab" => 0x09,
            "espace" or "space" => 0x20,
            "retour" or "backspace" => 0x08,
            "suppr" or "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "haut" or "up" => 0x26,
            "bas" or "down" => 0x28,
            "gauche" or "left" => 0x25,
            "droite" or "right" => 0x27,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            _ => 0,
        };
    }

    private static void EnvoyerTouche(ushort vk, char unicode, bool relacher)
    {
        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            Ki = new KEYBDINPUT
            {
                Vk = vk,
                Scan = vk,
                Flags = (relacher ? KEYEVENTF_KEYUP : 0u) | KEYEVENTF_UNICODE | KEYEVENTF_SCANCODE,
                Time = 0,
                DwExtraInfo = IntPtr.Zero,
            },
        };
        if (unicode != '\0')
        {
            input.Ki.Scan = unicode;
            input.Ki.Flags = (relacher ? KEYEVENTF_KEYUP : 0u) | KEYEVENTF_UNICODE;
            input.Ki.Vk = 0;
        }
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}