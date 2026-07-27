using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sc2Xboxed.Osk;

public sealed class OverlayForm : Form
{
    private readonly double _boardY;
    private readonly double _boardX;
    private readonly double _keyW;
    private readonly double _keyH;
    private readonly int _screenW;
    private readonly int _screenH;

    private double _rightCursorX, _rightCursorY;
    private double _leftCursorX, _leftCursorY;
    private bool _rightVisible, _leftVisible;
    private KeyDef? _highlightedKey;
    private KeyDef? _highlightedLeftKey;
    private KeyDef? _flashingKey;
    private DateTime _flashEnd;
    private bool _symActive;
    private bool _shiftHeld;
    private readonly object _lock = new();
    private readonly System.Windows.Forms.Timer _topMostTimer;

    public double BoardX => _boardX;
    public double BoardY => _boardY;
    public double KeyW => _keyW;
    public double KeyH => _keyH;

    public OverlayForm()
    {
        _screenW = Screen.PrimaryScreen!.Bounds.Width;
        _screenH = Screen.PrimaryScreen.Bounds.Height;
        _boardY = _screenH - 260;
        _boardX = 0;
        _keyW = (double)_screenW / KeyboardLayout.MaxCols;
        _keyH = 50;

        Text = "SteamXBox Keyboard";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(0, 0, _screenW, _screenH);
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        DoubleBuffered = true;

        _topMostTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (IsHandleCreated && Visible)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _topMostTimer.Start();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000020 | 0x00080000 | 0x08000000;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(TransparencyKey);

            DrawKeyboard(g);
            DrawCursors(g);
        }
        catch (Exception ex)
        {
            using var g = e.Graphics;
            g.Clear(Color.FromArgb(30, 0, 0, 0));
            System.Diagnostics.Debug.WriteLine($"Overlay OnPaint error: {ex}");
        }
    }

    private void DrawKeyboard(Graphics g)
    {
        using var keyBrush = new SolidBrush(Color.FromArgb(0x80, 0x20, 0x20, 0x30));
        using var borderPen = new Pen(Color.FromArgb(0x90, 0x80, 0x80, 0x90), 1);
        using var highlightBrush = new SolidBrush(Color.FromArgb(0xC0, 0x40, 0x80, 0xFF));
        using var flashBrush = new SolidBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        using var symBrush = new SolidBrush(Color.FromArgb(0xC0, 0xFF, 0xCC, 0x44));
        using var symHighlightBrush = new SolidBrush(Color.FromArgb(0xFF, 0x18, 0x18, 0x18));
        using var normalFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var specialFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var shiftFont = new Font("Segoe UI", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        using var symFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var shiftBrush = new SolidBrush(Color.FromArgb(0x90, 0xCC, 0xCC, 0xCC));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var sfTopRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
        using var sfBotRight = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };

        lock (_lock)
        {
            bool sym = _symActive;

            foreach (var key in KeyboardLayout.Keys)
            {
                double x = _boardX + key.Col * _keyW;
                double y = _boardY + key.Row * _keyH;
                float w = (float)(key.Width * _keyW - 2);
                float h = (float)(_keyH - 2);
                var rect = new RectangleF((float)(x + 1), (float)(y + 1), w, h);

                Brush fill;
                if (key == _flashingKey && DateTime.UtcNow < _flashEnd)
                    fill = flashBrush;
                else if (key == _highlightedKey || key == _highlightedLeftKey)
                    fill = highlightBrush;
                else if (sym && key.Action == SpecialAction.Sym)
                    fill = highlightBrush;
                else
                    fill = keyBrush;

                g.FillRectangle(fill, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);

                if (sym && key.Action == SpecialAction.None && key.SymChar != '\0')
                {
                    g.DrawString(key.SymChar.ToString(), symFont, symBrush, rect, sf);
                }
                else if (sym && key.Action == SpecialAction.Sym)
                {
                    g.DrawString("SYM", specialFont, highlightBrush, rect, sf);
                }
                else
                {
                    var font = key.Action != SpecialAction.None ? specialFont : normalFont;
                    g.DrawString(key.Label, font, textBrush, rect, sf);

                    if (key.Action == SpecialAction.None)
                    {
                        var detailRect = new RectangleF(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4);

                        if (key.ShiftedChar != '\0')
                            g.DrawString(key.ShiftedChar.ToString(), shiftFont, shiftBrush, detailRect, sfTopRight);
                    }
                }
            }
        }
    }

    private void DrawCursors(Graphics g)
    {
        lock (_lock)
        {
            if (_rightVisible)
                DrawCircle(g, (float)_rightCursorX, (float)_rightCursorY, 12,
                    Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            if (_leftVisible)
                DrawCircle(g, (float)_leftCursorX, (float)_leftCursorY, 12,
                    Color.White, Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }
    }

    private static void DrawCircle(Graphics g, float cx, float cy, float r, Color strokeColor, Color fillColor)
    {
        using var pen = new Pen(strokeColor, 2.5f);
        using var brush = new SolidBrush(fillColor);
        g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
    }

    public void SetRightCursor(double px, double py)
    { lock (_lock) { _rightCursorX = px; _rightCursorY = py; _rightVisible = true; } Invalidate(); }

    public void SetLeftCursor(double px, double py)
    { lock (_lock) { _leftCursorX = px; _leftCursorY = py; _leftVisible = true; } Invalidate(); }

    public void HideRightCursor()
    { lock (_lock) _rightVisible = false; Invalidate(); }

    public void HideLeftCursor()
    { lock (_lock) _leftVisible = false; Invalidate(); }

    public void HighlightKey(KeyDef? key)
    { lock (_lock) _highlightedKey = key; Invalidate(); }

    public void HighlightLeftKey(KeyDef? key)
    { lock (_lock) _highlightedLeftKey = key; Invalidate(); }

    public void SetModifierState(bool shift, bool sym)
    { lock (_lock) { _shiftHeld = shift; _symActive = sym; } Invalidate(); }

    public void FlashKey(KeyDef? key)
    {
        if (key is null) return;
        lock (_lock)
        {
            _flashingKey = key;
            _flashEnd = DateTime.UtcNow.AddMilliseconds(120);
        }
        Invalidate();
        _ = Task.Delay(150).ContinueWith(_ =>
        {
            lock (_lock) _flashingKey = null;
            try { BeginInvoke(Invalidate); } catch { }
        });
    }

    public void HideAll()
    {
        lock (_lock)
        {
            _rightVisible = false;
            _leftVisible = false;
            _highlightedKey = null;
            _highlightedLeftKey = null;
            _flashingKey = null;
        }
        Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _topMostTimer.Stop();
        _topMostTimer.Dispose();
        base.OnFormClosing(e);
    }

    public (double BoardX, double BoardY, double KeyW, double KeyH) GetMetrics()
        => (_boardX, _boardY, _keyW, _keyH);
}
