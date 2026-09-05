using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SenSÉ.Desktop.Capture;

/// <summary>
/// The region selector: a dimmed sheet over every screen, with a rectangle you drag.
/// </summary>
/// <remarks>
/// Spans the whole virtual screen rather than one monitor. A selector that only covers the primary
/// display is useless the moment the thing worth capturing is on the second one — and this machine
/// has two.
///
/// Everything here is in device-independent units, which is what WPF works in; the conversion to
/// physical pixels happens once, at the end, because <see cref="ScreenCapture"/> reads real pixels.
/// Doing it any earlier would mean carrying two coordinate systems through the drag logic.
/// </remarks>
public partial class CaptureOverlayWindow : Window
{
    private Point _origin;
    private bool _dragging;

    /// <summary>The chosen region in physical pixels, or null when cancelled.</summary>
    public Int32Rect? Region { get; private set; }

    public CaptureOverlayWindow()
    {
        InitializeComponent();

        // SystemParameters gives the virtual screen in DIPs, which is exactly what Left/Top/Width
        // and Height expect, so no conversion is needed to place the window itself.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += (_, _) =>
        {
            ShadeAll();
            PlaceHint();
            Activate();
        };
    }

    /// <summary>Dims the whole surface, the state before any selection exists.</summary>
    private void ShadeAll()
    {
        SetRect(ShadeTop, 0, 0, Width, Height);
        SetRect(ShadeBottom, 0, 0, 0, 0);
        SetRect(ShadeLeft, 0, 0, 0, 0);
        SetRect(ShadeRight, 0, 0, 0, 0);
    }

    private void PlaceHint()
    {
        Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Hint, (Width - Hint.DesiredSize.Width) / 2);
        Canvas.SetTop(Hint, 48);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _origin = e.GetPosition(Surface);
        _dragging = true;
        Hint.Visibility = Visibility.Collapsed;
        Selection.Visibility = Visibility.Visible;
        SizeTag.Visibility = Visibility.Visible;
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Draw(Current(e.GetPosition(Surface)));
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            base.OnMouseLeftButtonUp(e);
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        var rect = Current(e.GetPosition(Surface));

        // A stray click is a cancel, not a one-pixel capture: releasing without really dragging is
        // how someone changes their mind, and it should not produce a file.
        if (rect.Width >= 4 && rect.Height >= 4)
        {
            Region = ToPhysicalPixels(rect);
        }

        DialogResult = Region is not null;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Region = null;
            DialogResult = false;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>The rectangle between the drag origin and the pointer, in either direction.</summary>
    private Rect Current(Point pointer)
    {
        var x = Math.Min(_origin.X, pointer.X);
        var y = Math.Min(_origin.Y, pointer.Y);
        return new Rect(x, y, Math.Abs(pointer.X - _origin.X), Math.Abs(pointer.Y - _origin.Y));
    }

    /// <summary>Moves the selection rectangle, its dimming and its size label.</summary>
    private void Draw(Rect rect)
    {
        SetRect(Selection, rect.X, rect.Y, rect.Width, rect.Height);

        SetRect(ShadeTop, 0, 0, Width, rect.Y);
        SetRect(ShadeBottom, 0, rect.Bottom, Width, Math.Max(0, Height - rect.Bottom));
        SetRect(ShadeLeft, 0, rect.Y, rect.X, rect.Height);
        SetRect(ShadeRight, rect.Right, rect.Y, Math.Max(0, Width - rect.Right), rect.Height);

        var pixels = ToPhysicalPixels(rect);
        SizeText.Text = $"{pixels.Width} × {pixels.Height}";
        SizeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Below the selection, unless that would fall off the bottom edge.
        var top = rect.Bottom + 8;
        if (top + SizeTag.DesiredSize.Height > Height)
        {
            top = Math.Max(0, rect.Y - SizeTag.DesiredSize.Height - 8);
        }

        Canvas.SetLeft(SizeTag, Math.Min(rect.X, Math.Max(0, Width - SizeTag.DesiredSize.Width)));
        Canvas.SetTop(SizeTag, top);
    }

    private static void SetRect(System.Windows.Shapes.Rectangle shape, double x, double y, double w, double h)
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        shape.Width = Math.Max(0, w);
        shape.Height = Math.Max(0, h);
    }

    /// <summary>
    /// Converts a rectangle on this window into physical screen pixels.
    /// </summary>
    /// <remarks>
    /// The scale factor matters as soon as a display is not at 100%: WPF measures in units of 1/96
    /// inch, while the screen capture reads real pixels. Ignoring it would capture a region of the
    /// wrong size and in the wrong place on any scaled monitor.
    /// </remarks>
    private Int32Rect ToPhysicalPixels(Rect rect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);

        var x = (int)Math.Round((Left + rect.X) * dpi.DpiScaleX);
        var y = (int)Math.Round((Top + rect.Y) * dpi.DpiScaleY);
        var w = (int)Math.Round(rect.Width * dpi.DpiScaleX);
        var h = (int)Math.Round(rect.Height * dpi.DpiScaleY);

        return new Int32Rect(x, y, w, h);
    }
}
