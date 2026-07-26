using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Sc2Xboxed.Osk;

public partial class OverlayWindow : Window
{
    private readonly Dictionary<KeyDef, Rectangle> _keyRects = new();
    private readonly Dictionary<KeyDef, TextBlock> _keyLabels = new();
    private double _keyW, _keyH, _boardX, _boardY;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var screen = SystemParameters.PrimaryScreenWidth;
        var sh = SystemParameters.PrimaryScreenHeight;

        Left = 0; Top = 0;
        Width = screen; Height = sh;

        _boardY = sh - 260;
        _boardX = 0;
        _keyW = screen / KeyboardLayout.MaxCols;
        _keyH = 50;

        DrawKeyboard();
    }

    private void DrawKeyboard()
    {
        foreach (var key in KeyboardLayout.Keys)
        {
            double x = _boardX + key.Col * _keyW;
            double y = _boardY + key.Row * _keyH;
            double w = key.Width * _keyW - 2;

            var rect = new Rectangle
            {
                Width = w,
                Height = _keyH - 2,
                Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x20, 0x30)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x80, 0x80, 0x90)),
                StrokeThickness = 1,
                RadiusX = 4,
                RadiusY = 4
            };
            Canvas.SetLeft(rect, x + 1);
            Canvas.SetTop(rect, y + 1);
            Canvas.Children.Add(rect);
            _keyRects[key] = rect;

            var label = new TextBlock
            {
                Text = key.Label,
                Foreground = Brushes.White,
                FontSize = key.Action != SpecialAction.None ? 13 : 20,
                FontWeight = key.Action != SpecialAction.None ? FontWeights.Normal : FontWeights.Bold,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lx = x + 1 + (w - label.DesiredSize.Width) / 2;
            double ly = y + 1 + (_keyH - 2 - label.DesiredSize.Height) / 2;
            Canvas.SetLeft(label, lx);
            Canvas.SetTop(label, ly);
            Canvas.Children.Add(label);
            _keyLabels[key] = label;
        }
    }

    public void SetRightCursor(double px, double py)
    {
        Canvas.SetLeft(RightCursor, px - 12);
        Canvas.SetTop(RightCursor, py - 12);
        RightCursor.Visibility = Visibility.Visible;
    }

    public void SetLeftCursor(double px, double py)
    {
        Canvas.SetLeft(LeftCursor, px - 12);
        Canvas.SetTop(LeftCursor, py - 12);
        LeftCursor.Visibility = Visibility.Visible;
    }

    public void HideRightCursor() => RightCursor.Visibility = Visibility.Collapsed;
    public void HideLeftCursor() => LeftCursor.Visibility = Visibility.Collapsed;

    public void HighlightKey(KeyDef? key)
    {
        ClearHighlight();
        if (key is null) return;
        if (_keyRects.TryGetValue(key, out var rect))
            rect.Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0x40, 0x80, 0xFF));
    }

    public void FlashKey(KeyDef? key)
    {
        if (key is null) return;
        if (_keyRects.TryGetValue(key, out var rect))
        {
            rect.Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
            _ = Task.Delay(120).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (rect.Fill is SolidColorBrush)
                        rect.Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x20, 0x30));
                });
            });
        }
    }

    private void ClearHighlight()
    {
        foreach (var kv in _keyRects)
            kv.Value.Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x20, 0x30));
    }

    public void HideAll()
    {
        HideRightCursor();
        HideLeftCursor();
        ClearHighlight();
    }

    public (double BoardX, double BoardY, double KeyW, double KeyH) GetMetrics()
        => (_boardX, _boardY, _keyW, _keyH);
}
