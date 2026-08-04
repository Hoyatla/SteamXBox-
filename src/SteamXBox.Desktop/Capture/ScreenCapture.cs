using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SteamXBox.Desktop.Capture;

/// <summary>Where a capture ended up.</summary>
/// <param name="Path">File it was written to, or empty when saving failed.</param>
/// <param name="Width">Captured width in physical pixels.</param>
/// <param name="Height">Captured height in physical pixels.</param>
public readonly record struct CaptureResult(string Path, int Width, int Height);

/// <summary>
/// Copies a region of the screen.
/// </summary>
/// <remarks>
/// SteamXBox's own capture rather than Win+Shift+S. The Windows tool opens its own window and takes
/// the foreground, which is exactly what breaks here: the overlay has to get out of the way, come
/// back, and hope the focus lands where the user expected. Doing the capture in-process means the
/// overlay controls the whole sequence — hide, grab the pixels, reappear — with nothing else
/// competing for the foreground.
///
/// <c>CopyFromScreen</c> reads the composited desktop, so it captures whatever is actually on
/// screen, including other applications. It does not read protected content (DRM video shows
/// black), which is the operating system's decision and not something to work around.
/// </remarks>
public static class ScreenCapture
{
    /// <summary>Folder captures are written to, created on first use.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SteamXBox");

    /// <summary>
    /// Captures a rectangle given in physical screen pixels.
    /// </summary>
    /// <param name="region">Region to grab, in physical pixels relative to the virtual screen.</param>
    /// <param name="stamp">Timestamp used for the file name; passed in so the caller owns the clock.</param>
    public static CaptureResult Capture(Int32Rect region, DateTimeOffset stamp)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return new CaptureResult("", 0, 0);
        }

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.X, region.Y, 0, 0, new System.Drawing.Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy);
        }

        // The clipboard first: it is the thing most captures are for, and it must happen even if
        // writing to disk fails — a read-only Pictures folder should not lose the capture.
        TrySetClipboard(bitmap);

        var path = TrySave(bitmap, stamp);
        return new CaptureResult(path, region.Width, region.Height);
    }

    private static void TrySetClipboard(Bitmap bitmap)
    {
        try
        {
            // Through a BitmapSource rather than System.Windows.Forms.Clipboard: this is a WPF
            // process, and mixing the two clipboard APIs in one application is a known source of
            // formats that paste into some targets and not others.
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            Clipboard.SetImage(source);
        }
        catch
        {
            // Another process can hold the clipboard open. Not worth failing the capture over: the
            // file on disk is still there.
        }
    }

    private static string TrySave(Bitmap bitmap, DateTimeOffset stamp)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"capture-{stamp:yyyy-MM-dd-HHmmss}.png");
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return "";
        }
    }
}
