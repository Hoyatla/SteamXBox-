using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Capture d'ecran et de fenetre, en PNG/JPG/BMP.
/// </summary>
/// <remarks>
/// <b>PrintWindow plutot que BitBlt sur les fenetres.</b> BitBlt rate les
/// fenetres qui utilisent le DWM (Composition) ou qui sont minimisees.
/// PrintWindow avec PW_RENDERFULLCONTENT capture la composition reelle
/// et marche aussi pour les fenetres cachees derriere d'autres.
///
/// <para><b>Le chemin du fichier.</b> Toujours sous AppContext.BaseDirectory,
/// jamais un chemin arbitraire que le modele aurait invente : le manifeste
/// peut proposer un nom, le code impose le repertoire.</para>
/// </remarks>
public static class Capture
{
    private const int PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Capture tout l'ecran, ou un moniteur precis. Retourne le chemin du fichier ecrit.</summary>
    public static string Ecran(int moniteur, string format)
    {
        var bounds = moniteur <= 0
            ? SystemInformation.VirtualScreen
            : Screen.AllScreens[Math.Min(moniteur - 1, Screen.AllScreens.Length - 1)].Bounds;

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        return Ecrire(bitmap, format);
    }

    /// <summary>Capture la fenetre identifiee par son titre (regex partielle). Retourne le chemin, ou vide.</summary>
    /// <remarks>
    /// <b>Un echec se dit, il ne se rend pas vide.</b> Cette methode rendait "" quand aucune
    /// fenetre ne correspondait, et le serveur l'emballait en <c>{"ok":true,"data":""}</c> — un
    /// succes annonce pour un travail non fait. L'Assistant n'avait aucun moyen de distinguer
    /// « fenetre absente » de « capture prise », et c'est le genre de reponse qui le fait tourner
    /// en rond : il enchaine sur l'etape suivante avec un chemin vide dans les mains.
    ///
    /// <para>Les exceptions remontent au serveur, qui les rend en HTTP 500 avec leur message.
    /// C'est ce que fait deja pc-agent pour le meme cas, avec le meme texte.</para>
    /// </remarks>
    public static string FenetreParTitre(string titre, string format)
    {
        var hwnd = TrouverFenetre(titre);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"no window with title containing '{titre}'");
        }

        GetWindowRect(hwnd, out var rect);
        var largeur = rect.Right - rect.Left;
        var hauteur = rect.Bottom - rect.Top;
        if (largeur <= 0 || hauteur <= 0)
        {
            throw new InvalidOperationException(
                $"la fenetre '{titre}' n'a pas de surface visible ({largeur}x{hauteur}) : "
                + "elle est probablement reduite.");
        }

        using var bitmap = new Bitmap(largeur, hauteur, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
        return Ecrire(bitmap, format);
    }

    /// <summary>Capture la fenetre par son handle (HWND) directement.</summary>
    public static string FenetreParHandle(IntPtr hwnd, string format)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return "";
        GetWindowRect(hwnd, out var rect);
        var largeur = rect.Right - rect.Left;
        var hauteur = rect.Bottom - rect.Top;
        if (largeur <= 0 || hauteur <= 0) return "";

        using var bitmap = new Bitmap(largeur, hauteur, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
        finally { graphics.ReleaseHdc(hdc); }
        return Ecrire(bitmap, format);
    }

    /// <summary>Liste les fenetres visibles avec leur titre, pour que l'Assistant puisse choisir.</summary>
    public static IReadOnlyList<(IntPtr Handle, string Titre)> ListerFenetres()
    {
        var result = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            var titre = sb.ToString();
            if (titre.Length > 0) result.Add((h, titre));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private static IntPtr TrouverFenetre(string titre)
    {
        IntPtr trouve = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString().Contains(titre, StringComparison.OrdinalIgnoreCase))
            {
                trouve = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return trouve;
    }

    private static string Ecrire(Bitmap bitmap, string format)
    {
        // A la racine du produit, et non sous AppContext.BaseDirectory : mcp-saisie vit dans
        // Outils\McpSaisie\, ou cette composition creait un Outils\McpSaisie\Captures que
        // l'utilisateur ne trouve pas et que rien ne purge, pendant que pc-agent ecrivait dans
        // Captures a la racine. Deux dossiers pour la meme chose. Voir Emplacements.
        var dir = SenSÉ.Mcp.Bus.Emplacements.Captures();
        Directory.CreateDirectory(dir);
        var nom = $"capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{NormaliserFormat(format)}";
        var path = Path.Combine(dir, nom);
        var fmt = NormaliserFormat(format) switch
        {
            "jpg" => ImageFormat.Jpeg,
            "bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
        bitmap.Save(path, fmt);
        return path;
    }

    private static string NormaliserFormat(string format)
        => format.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "jpg",
            "bmp" => "bmp",
            _ => "png",
        };
}