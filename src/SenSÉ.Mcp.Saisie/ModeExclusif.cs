using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Bandeau "L'ASSISTANT PILOTE" en overlay Win32 natif (pas de WPF, pas de
/// thread STA dedie, pas d'Application). Juste CreateWindowEx +
/// UpdateLayeredWindow sur le thread principal qui est deja STA grace a
/// <c>[STAThread]</c> dans Program.cs.
/// </summary>
/// <remarks>
/// <b>Pourquoi pas WPF ici.</b> WPF exige une Application vivante et un
/// dispatcher qui tourne. Dans un subprocess stdio comme mcp-saisie, le
/// main thread est deja occupe par <c>await StreamReader.ReadLineAsync</c>
/// (qui libere le thread entre les awaits, mais ne fait pas tourner un
/// dispatcher WPF). Toute Window WPF appelee depuis ce contexte bloque
/// sur Show() jusqu'au timeout. Win32 + UpdateLayeredWindow n'a pas ce
/// probleme : on peint dans un memory DC et on pousse le bitmap alpha-
/// blended vers le HWND en un seul appel, pas de message pump a faire
/// tourner.
///
/// <para><b>Topmost, click-through.</b> WS_EX_TOPMOST + WS_EX_LAYERED +
/// WS_EX_TRANSPARENT : la fenetre reste au-dessus de tout mais laisse
/// passer les clics (l'utilisateur peut continuer a utiliser l'app
/// pilotee pendant que le bandeau est visible). WS_EX_NOACTIVATE :
/// ShowWindow n'active pas la fenetre, donc pas de focus shift.</para>
///
/// <para><b>Pas de reagir a Echap.</b> Le code precedent (WPF) avait un
/// OnPreviewKeyDown qui interceptait Echap. La version Win32 ne le fait
/// pas : l'interruption Echap se fait au niveau de Souris/Clavier (qui
/// verifient <see cref="EstActif"/> avant d'agir). Si tu veux un vrai
/// Echap interrupt, il faudra ajouter une message pump minimale ou
/// intercepter via RegisterHotKey.</para>
/// </remarks>
public static class ModeExclusif
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;

    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const int TRANSPARENT = 1;

    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int SM_CXSCREEN = 0;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static string _serveur = "";
    private static string _sequence = "";
    private static readonly object _gate = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int W, H; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int c);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool FillRgn(IntPtr hdc, IntPtr hrgn, IntPtr hbrush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeft, int nTop, int nRight, int nBottom);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>Une fenetre est-elle deja ouverte ?</summary>
    public static bool EstActif => _hwnd != IntPtr.Zero;

    /// <summary>Ouvre l'overlay. Si une instance est deja ouverte, on ne fait rien
    /// (le texte est fixe pour cette version Win32 ; pas de ChangerTexte).</summary>
    public static void Ouvrir(string serveur, string sequence)
    {
        lock (_gate)
        {
            _serveur = serveur;
            _sequence = sequence;
            if (_hwnd != IntPtr.Zero) return; // deja ouvert

            const int width = 600;
            const int height = 60;
            int screenW = GetSystemMetrics(SM_CXSCREEN);
            int x = (screenW - width) / 2;
            int y = 0;

            _hwnd = CreateWindowExW(
                WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                "Static",
                "Assistant pilote",
                WS_POPUP | WS_VISIBLE,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                // Fallback : on n'a pas pu creer la fenetre, mais on continue
                // (le mode exclusif est juste visuel ; les actions souris/clavier
                // verifient quand meme EstActif).
                return;
            }

            Dessiner();
            SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    /// <summary>Ferme l'overlay et desabonne le mode exclusif.</summary>
    public static void Fermer()
    {
        lock (_gate)
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }

    private static void Dessiner()
    {
        if (_hwnd == IntPtr.Zero) return;

        const int width = 600;
        const int height = 60;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;
        IntPtr memDc = CreateCompatibleDC(screenDc);
        if (memDc == IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); return; }
        IntPtr bmp = CreateCompatibleBitmap(screenDc, width, height);
        if (bmp == IntPtr.Zero) { DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc); return; }
        IntPtr oldBmp = SelectObject(memDc, bmp);
        IntPtr region = IntPtr.Zero;
        IntPtr brush = IntPtr.Zero;

        try
        {
            // Fond orange BGR (0x002A6CFF = RGB(255, 108, 42) en little-endian 0x00BBGGRR)
            region = CreateRectRgn(0, 0, width, height);
            brush = CreateSolidBrush(0x002A6CFF);
            FillRgn(memDc, region, brush);

            // Texte blanc
            SetTextColor(memDc, 0x00FFFFFF);
            SetBkMode(memDc, TRANSPARENT);
            string text = $"L'ASSISTANT PILOTE  |  {serveur_actuel()} / {sequence_actuelle()}";
            TextOutW(memDc, 16, 22, text, text.Length);

            var ptSrc = new POINT { X = 0, Y = 0 };
            var ptDst = new POINT { X = 0, Y = 0 };
            var sz = new SIZE { W = width, H = height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 230,
                AlphaFormat = AC_SRC_ALPHA,
            };

            UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref ptDst, ref sz,
                memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (brush != IntPtr.Zero) DeleteObject(brush);
            if (region != IntPtr.Zero) DeleteObject(region);
            SelectObject(memDc, oldBmp);
            DeleteObject(bmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static string serveur_actuel() => _serveur;
    private static string sequence_actuelle() => _sequence;
}
