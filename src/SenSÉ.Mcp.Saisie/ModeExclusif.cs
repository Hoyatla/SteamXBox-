using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Bandeau "L'ASSISTANT PILOTE" en overlay Win32 natif (pas de WPF, pas
/// d'Application), tenu par un thread dedie qui possede la fenetre et pompe
/// ses messages.
/// </summary>
/// <remarks>
/// <b>Pourquoi pas WPF ici.</b> WPF exige une Application vivante et un
/// dispatcher qui tourne. Dans un subprocess stdio comme mcp-saisie, le
/// main thread est deja occupe par <c>await StreamReader.ReadLineAsync</c>
/// (qui libere le thread entre les awaits, mais ne fait pas tourner un
/// dispatcher WPF). Toute Window WPF appelee depuis ce contexte bloque
/// sur Show() jusqu'au timeout. Win32 + UpdateLayeredWindow n'a pas ce
/// probleme : on peint dans un memory DC et on pousse le bitmap alpha-
/// blended vers le HWND en un seul appel.
///
/// <para><b>Pourquoi un thread a lui, et pas le thread principal.</b> Le
/// commentaire precedent disait "le thread principal qui est deja STA grace a
/// <c>[STAThread]</c>". C'etait vrai jusqu'au premier <c>await</c> : dans un
/// <c>async Task Main</c>, la suite s'execute sur un thread du pool, et depuis
/// que les connexions partent en taches, chaque requete arrive sur un thread
/// different encore. Deux consequences, toutes deux constatees :
/// <c>CreateWindowExW</c> creait une fenetre sur un thread sans file de
/// messages — donc jamais repeinte, et fantomisee par le gestionnaire de
/// fenetres au bout de quelques secondes — et surtout <c>DestroyWindow</c>,
/// qui ne detruit QUE depuis le thread createur, echouait silencieusement des
/// que la fermeture arrivait sur un autre thread que l'ouverture. Le bandeau
/// restait alors colle a l'ecran jusqu'a la fin du processus.
///
/// D'ou ce thread unique, STA, avec un <c>GetMessage</c> : il cree la fenetre,
/// la repeint, la detruit, et rien d'autre ne la touche. <see cref="Ouvrir"/>
/// et <see cref="Fermer"/> ne font plus que lui poster un message.</para>
///
/// <para><b>Topmost, click-through.</b> WS_EX_TOPMOST + WS_EX_LAYERED +
/// WS_EX_TRANSPARENT : la fenetre reste au-dessus de tout mais laisse
/// passer les clics (l'utilisateur peut continuer a utiliser l'app
/// pilotee pendant que le bandeau est visible). WS_EX_NOACTIVATE :
/// ShowWindow n'active pas la fenetre, donc pas de focus shift.</para>
///
/// <para><b>Pas de reagir a Echap.</b> Le code precedent (WPF) avait un
/// OnPreviewKeyDown qui interceptait Echap. Rien ne le fait plus ici, et
/// Souris/Clavier ne consultent plus <see cref="EstActif"/> avant d'agir : le
/// bandeau est purement informatif. La pompe de messages existe desormais, donc
/// un vrai Echap interrupt ne demande plus qu'un <c>RegisterHotKey</c> et un cas
/// <c>WM_HOTKEY</c> dans <see cref="Pomper"/>.</para>
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

    /// <summary>Messages postes au thread du bandeau. WM_APP = 0x8000.</summary>
    private const uint WM_APP_OUVRIR = 0x8000 + 1;
    private const uint WM_APP_FERMER = 0x8000 + 2;

    private const uint PM_NOREMOVE = 0x0000;

    /// <summary>Ne doit etre lu et ecrit que par <see cref="Pomper"/>.</summary>
    private static IntPtr _hwnd = IntPtr.Zero;

    /// <summary>Le miroir de <see cref="_hwnd"/> lisible depuis n'importe quel thread.</summary>
    private static volatile bool _actif;

    private static volatile string _serveur = "";
    private static volatile string _sequence = "";
    private static readonly object _gate = new();

    private static Thread? _pompe;
    private static volatile uint _pompeId;
    private static readonly ManualResetEventSlim _pompePrete = new(false);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>Une fenetre est-elle deja ouverte ?</summary>
    public static bool EstActif => _actif;

    /// <summary>Demande l'ouverture de l'overlay au thread du bandeau. Si une instance est
    /// deja ouverte, on ne fait rien (le texte est fixe pour cette version Win32 ; pas de
    /// ChangerTexte).</summary>
    /// <remarks>
    /// Ne cree rien elle-meme : elle demarre la pompe au besoin, puis lui poste
    /// WM_APP_OUVRIR. Creer la fenetre ici la lierait au thread de la requete HTTP en
    /// cours, qui n'a ni file de messages ni existence garantie a la fermeture.
    /// </remarks>
    public static void Ouvrir(string serveur, string sequence)
    {
        lock (_gate)
        {
            _serveur = serveur;
            _sequence = sequence;
            if (!DemarrerPompe()) return;
            PostThreadMessageW(_pompeId, WM_APP_OUVRIR, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Demande la fermeture de l'overlay au thread du bandeau.</summary>
    public static void Fermer()
    {
        lock (_gate)
        {
            if (_pompeId == 0) return;
            PostThreadMessageW(_pompeId, WM_APP_FERMER, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Demarre le thread du bandeau s'il ne tourne pas. Vrai si sa file est prete.</summary>
    /// <remarks>
    /// L'attente n'est pas une precaution de style : PostThreadMessage echoue tant que le
    /// thread vise n'a pas de file de messages, et une file n'existe qu'apres le premier
    /// appel a une fonction qui en cree une. D'ou le PeekMessage dans <see cref="Pomper"/>,
    /// et l'evenement qu'on attend ici. Deux secondes, puis on renonce : un bandeau absent
    /// vaut mieux qu'un serveur bloque.
    /// </remarks>
    private static bool DemarrerPompe()
    {
        if (_pompe is null)
        {
            _pompe = new Thread(Pomper)
            {
                IsBackground = true,
                Name = "mcp-saisie bandeau",
            };
            _pompe.SetApartmentState(ApartmentState.STA);
            _pompe.Start();
        }
        return _pompePrete.Wait(TimeSpan.FromSeconds(2)) && _pompeId != 0;
    }

    /// <summary>Le thread du bandeau : cree la file, puis sert les messages jusqu'a l'arret.</summary>
    private static void Pomper()
    {
        _pompeId = GetCurrentThreadId();

        // Force la creation de la file de messages du thread avant d'annoncer qu'il est
        // pret : sans cela, le premier PostThreadMessage se perd sans erreur visible.
        PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        _pompePrete.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            // Un message poste au thread (et non a une fenetre) a hwnd nul : c'est le
            // notre. DispatchMessage n'en ferait rien, faute de fenetre destinataire.
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_OUVRIR)
            {
                Creer();
                continue;
            }
            if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_FERMER)
            {
                Detruire();
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>Cree la fenetre. Thread du bandeau uniquement.</summary>
    private static void Creer()
    {
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
            // On n'a pas pu creer la fenetre. Le bandeau etant purement informatif,
            // rien d'autre n'en depend : les actions souris/clavier partiront quand meme.
            return;
        }

        _actif = true;
        Dessiner();
        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Detruit la fenetre. Thread du bandeau uniquement.</summary>
    /// <remarks>
    /// C'est le point du fichier qui justifie tout le reste : DestroyWindow ne detruit une
    /// fenetre que si l'appel vient du thread qui l'a creee. Appele depuis ailleurs, il
    /// echoue en rendant false, et le bandeau reste a l'ecran.
    /// </remarks>
    private static void Detruire()
    {
        if (_hwnd == IntPtr.Zero) return;
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
        _actif = false;
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
