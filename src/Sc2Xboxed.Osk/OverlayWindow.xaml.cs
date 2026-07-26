using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sc2Xboxed.Osk;

public partial class OverlayWindow : Window
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int GwlExstyle = -20;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private double _scaleX = 1.0;
    private double _scaleY = 1.0;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExstyle);
        SetWindowLong(handle, GwlExstyle, exStyle | WsExTransparent | WsExLayered);

        var ps = PresentationSource.FromVisual(this);
        if (ps?.CompositionTarget != null)
        {
            _scaleX = ps.CompositionTarget.TransformToDevice.M11;
            _scaleY = ps.CompositionTarget.TransformToDevice.M22;
        }
    }

    public void SetBounds(double vx, double vy, double vw, double vh)
    {
        Left = vx / _scaleX;
        Top = vy / _scaleY;
        Width = vw / _scaleX;
        Height = vh / _scaleY;
    }

    public void SetRightCursor(double physicalX, double physicalY)
    {
        Canvas.SetLeft(RightCursor, physicalX / _scaleX - 10);
        Canvas.SetTop(RightCursor, physicalY / _scaleY - 10);
    }

    public void SetLeftCursor(double physicalX, double physicalY)
    {
        Canvas.SetLeft(LeftCursor, physicalX / _scaleX - 10);
        Canvas.SetTop(LeftCursor, physicalY / _scaleY - 10);
    }

    public void FlashRight()
    {
        RightCursor.StrokeThickness = 3;
        RightCursor.Stroke = new SolidColorBrush(System.Windows.Media.Colors.White);
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            RightCursor.StrokeThickness = 2;
            RightCursor.Stroke = new SolidColorBrush(System.Windows.Media.Colors.White);
            timer.Stop();
        };
        timer.Start();
    }

    public void FlashLeft()
    {
        LeftCursor.StrokeThickness = 3;
        LeftCursor.Stroke = new SolidColorBrush(System.Windows.Media.Colors.White);
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            LeftCursor.StrokeThickness = 2;
            LeftCursor.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            timer.Stop();
        };
        timer.Start();
    }
}
