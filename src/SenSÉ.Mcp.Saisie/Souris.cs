using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Pilotage de la souris : deplacement, clic, double-clic, molette.
/// </summary>
/// <remarks>
/// <b>SendInput plutot que mouse_event.</b> mouse_event est obsolete depuis
/// Vista et ne marche pas correctement avec les applications qui utilisent
/// raw input. SendInput est la voie documentee, supportee par toutes les
/// applications Windows actuelles.
///
/// <para><b>Mode exclusif obligatoire.</b> Cette classe refuse de bouger
/// le curseur ou de cliquer si <see cref="ModeExclusif.EstActif"/> est
/// faux. C'est le seul garde-fou entre l'Assistant et le reste de
/// l'ordinateur : sans lui, l'Assistant deplacerait la souris pendant
/// que l'utilisateur tape au clavier, et le bordel serait immediate.</para>
/// </remarks>
public static class Souris
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public MOUSEINPUT Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>Deplace le curseur a une position absolue en coordonnees ecran.</summary>
    public static string Deplacer(int x, int y)
    {
        if (!ModeExclusif.EstActif) return "refuse: mode exclusif inactif";
        SetCursorPos(x, y);
        return $"deplace a ({x},{y})";
    }

    /// <summary>Clic a la position courante, ou aux coordonnees indiquees.</summary>
    public static string Cliquer(string bouton, bool doubles, int? x, int? y)
    {
        if (!ModeExclusif.EstActif) return "refuse: mode exclusif inactif";

        if (x is not null && y is not null)
        {
            SetCursorPos(x.Value, y.Value);
            Thread.Sleep(20);
        }

        var (down, up) = bouton.ToLowerInvariant() switch
        {
            "droit" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "milieu" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        Envoyer(down | MOUSEEVENTF_MOVE);
        Envoyer(up | MOUSEEVENTF_MOVE);

        if (doubles)
        {
            Thread.Sleep(50);
            Envoyer(down | MOUSEEVENTF_MOVE);
            Envoyer(up | MOUSEEVENTF_MOVE);
        }

        return $"{(doubles ? "double" : "")}clic {bouton} a ({GetPositionActuelle().X},{GetPositionActuelle().Y})";
    }

    /// <summary>Fait tourner la molette. delta positif = haut, negatif = bas. axe = "vertical" ou "horizontal".</summary>
    public static string Molette(int delta, string axe)
    {
        if (!ModeExclusif.EstActif) return "refuse: mode exclusif inactif";
        var flag = axe.ToLowerInvariant() == "horizontal" ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;
        Envoyer(flag, (uint)delta);
        return $"molette {axe} delta={delta}";
    }

    /// <summary>Position courante du curseur.</summary>
    public static (int X, int Y) GetPositionActuelle()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    private static void Envoyer(uint flags, uint data = 0)
    {
        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            Mi = new MOUSEINPUT
            {
                Dx = 0,
                Dy = 0,
                MouseData = data,
                DwFlags = flags,
                Time = 0,
                DwExtraInfo = IntPtr.Zero,
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}