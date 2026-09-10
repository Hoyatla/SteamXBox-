using System;
using System.IO;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace SenSÉ.EditeurTexte.Format;

/// <summary>
/// Helpers pour la serialisation des images WPF dans les formats .docx et .odt.
/// Charge, redimensionne (1600px max), encode en PNG, gere le cache LocalAppData
/// pour la relecture (evite d'avoir a re-extraire l'image du ZIP/docx a chaque ouverture).
/// </summary>
public static class ImageHelper
{
    public static readonly string CacheDir = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ", "Éditeur", "cache");

    public const int DefaultMaxDim = 1600;

    static ImageHelper()
    {
        try { System.IO.Directory.CreateDirectory(CacheDir); } catch { /* best effort */ }
    }

    /// <summary>Charge l'image, redimensionne a maxDim max sur la plus grande dimension,
/// encode en PNG, retourne les bytes. null si le fichier n'existe pas ou n'est pas lisible.</summary>
    public static byte[]? ChargerImage(string chemin, int maxDim = DefaultMaxDim)
    {
        if (string.IsNullOrEmpty(chemin) || !System.IO.File.Exists(chemin)) return null;
        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new System.Uri(chemin, System.UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            int w = src.PixelWidth, h = src.PixelHeight;
            int longMax = System.Math.Max(w, h);
            double scale = longMax > maxDim ? (double)maxDim / longMax : 1.0;
            int dstW = System.Math.Max(1, (int)System.Math.Round(w * scale));
            int dstH = System.Math.Max(1, (int)System.Math.Round(h * scale));

            var render = new RenderTargetBitmap(dstW, dstH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            // Pour redimensionner proprement, on rend l'image source vers le RenderTargetBitmap.
            // L'image source doit etre clonee et dimensionnee, sinon WPF la dessine a sa taille naturelle.
            var img = new System.Windows.Controls.Image { Source = src, Width = dstW, Height = dstH, Stretch = System.Windows.Media.Stretch.Uniform };
            img.Measure(new System.Windows.Size(dstW, dstH));
            img.Arrange(new System.Windows.Rect(new System.Windows.Size(dstW, dstH)));
            render.Render(img);
            img.Source = null;

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(render));
            using var ms = new System.IO.MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>Produit un data URI base64 a partir de bytes et d'un MIME type.</summary>
    public static string VersDataUri(byte[] pngBytes, string mimeType = "image/png")
    {
        if (pngBytes is null || pngBytes.Length == 0) return "";
        return "data:" + mimeType + ";base64," + System.Convert.ToBase64String(pngBytes);
    }

    /// <summary>Essaie d'extraire le chemin source d'un InlineUIContainer contenant un Image WPF.
    /// Retourne null si l'image a ete creee programmatiquement (pas de UriSource).</summary>
    public static string? ResoudreCheminSource(InlineUIContainer container)
    {
        if (container?.Child is not System.Windows.Controls.Image img) return null;
        if (img.Source is BitmapImage bi && bi.UriSource is not null)
        {
            try { return bi.UriSource.LocalPath; } catch { return null; }
        }
        return null;
    }

    /// <summary>Sauvegarde les bytes dans le cache et retourne le path absolu. Le fichier
/// est nomme <c>img_{guid}.png</c> dans CacheDir. Cree le dossier si necessaire.</summary>
    public static string SauverDansCache(byte[] pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0) return "";
        System.IO.Directory.CreateDirectory(CacheDir);
        var name = "img_" + System.Guid.NewGuid().ToString("N") + ".png";
        var path = System.IO.Path.Combine(CacheDir, name);
        System.IO.File.WriteAllBytes(path, pngBytes);
        return path;
    }
}
