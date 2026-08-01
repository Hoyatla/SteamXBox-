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
    private ShiftMode _shift;
    private bool _daisywheel;
    private int? _activePetal;
    private KeyDef? _flashingSlot;
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

            if (_daisywheel)
            {
                DrawDaisywheel(g);
            }
            else
            {
                DrawKeyboard(g);
                DrawCursors(g);
            }
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
        var palette = OverlayPalette.Current;
        using var keyBrush = new SolidBrush(palette.Colour(palette.KeyFill, 0x80202030));
        using var borderPen = new Pen(palette.Colour(palette.KeyBorder, 0x90808090), 1);
        using var highlightBrush = new SolidBrush(palette.Colour(palette.KeyHighlight, 0xC04080FF));
        using var flashBrush = new SolidBrush(palette.Colour(palette.KeyFlash, 0xE0FFFFFF));
        using var symBrush = new SolidBrush(palette.Colour(palette.SymbolText, 0xC0FFCC44));
        using var symHighlightBrush = new SolidBrush(palette.Colour(palette.SymbolHighlightText, 0xFF181818));
        using var normalFont = palette.CreateFont(16, FontStyle.Bold);
        using var specialFont = palette.CreateFont(11, FontStyle.Regular);
        using var shiftFont = palette.CreateFont(9, FontStyle.Regular);
        using var symFont = palette.CreateFont(16, FontStyle.Bold);
        using var textBrush = new SolidBrush(palette.Colour(palette.KeyText, 0xFFFFFFFF));
        using var shiftBrush = new SolidBrush(palette.Colour(palette.ShiftText, 0x90CCCCCC));
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
                else if (_shift != ShiftMode.Off && key.Action == SpecialAction.Shift)
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

                    // The shift key states its own mode, phone-style: one capital, or locked.
                    var label = key.Action == SpecialAction.Shift
                        ? _shift switch
                        {
                            ShiftMode.OneShot => "MAJ ↑",
                            ShiftMode.Locked => "MAJ 🔒",
                            _ => key.Label,
                        }
                        : key.Label;

                    g.DrawString(label, font, textBrush, rect, sf);

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

    /// <summary>
    /// Draws the eight petals as a ring centred low on the screen, each showing its four characters
    /// tagged with the button that picks them.
    /// </summary>
    private void DrawDaisywheel(Graphics g)
    {
        var palette = OverlayPalette.Current;

        // Slot colours follow the Xbox face buttons so the mapping is readable at a glance.
        Color[] slotColors =
        [
            palette.Colour(palette.SlotA, 0xFF5CC05C),
            palette.Colour(palette.SlotB, 0xFFE05C5C),
            palette.Colour(palette.SlotX, 0xFF5C9CE0),
            palette.Colour(palette.SlotY, 0xFFE0C85C),
        ];

        using var petalBrush = new SolidBrush(palette.Colour(palette.PetalFill, 0xB0181824));
        using var activePetalBrush = new SolidBrush(palette.Colour(palette.PetalActiveFill, 0xD8203860));
        using var borderPen = new Pen(palette.Colour(palette.PetalBorder, 0x70808090), 1.5f);
        using var activeBorderPen = new Pen(palette.Colour(palette.PetalActiveBorder, 0xFF4080FF), 2.5f);
        using var hubBrush = new SolidBrush(palette.Colour(palette.HubFill, 0xC0101018));
        using var slotFont = palette.CreateFont(20, FontStyle.Bold);
        using var specialFont = palette.CreateFont(11, FontStyle.Bold);
        using var tagFont = palette.CreateFont(9, FontStyle.Regular);
        using var hubFont = palette.CreateFont(13, FontStyle.Bold);
        using var flashBrush = new SolidBrush(palette.Colour(palette.PetalFlash, 0xF0FFFFFF));
        using var dimBrush = new SolidBrush(palette.Colour(palette.PetalDimText, 0xA0B0B0C0));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        float centerX = _screenW / 2f;
        float centerY = _screenH - 250f;
        float ringRadius = 165f;
        float petalRadius = 62f;

        lock (_lock)
        {
            bool symbols = _symActive;

            g.FillEllipse(hubBrush, centerX - 46, centerY - 46, 92, 92);
            g.DrawEllipse(borderPen, centerX - 46, centerY - 46, 92, 92);
            g.DrawString(
                _shift == ShiftMode.Locked ? "MAJ" : _shift == ShiftMode.OneShot ? "Maj" : (symbols ? "SYM" : "abc"),
                hubFont,
                _shift != ShiftMode.Off || symbols ? flashBrush : dimBrush,
                new RectangleF(centerX - 46, centerY - 46, 92, 92),
                sf);

            // Exit hint under the hub. B is a character key in this mode, so the way out is not the
            // one muscle memory expects, and nothing else on screen says so. Drawn between the hub
            // and the south petal, which starts at centerY + 103.
            g.DrawString(
                "Menu = quitter   ·   clic pad = MAJ",
                tagFont,
                dimBrush,
                new RectangleF(centerX - 150, centerY + 56, 300, 16),
                sf);

            for (int petal = 0; petal < DaisywheelLayout.Petals; petal++)
            {
                var slots = DaisywheelLayout.Petal(petal, symbols);
                if (slots is null) continue;

                // Petal 0 sits north and indices advance clockwise.
                double angle = (90.0 - petal * 45.0) * Math.PI / 180.0;
                float px = centerX + (float)(Math.Cos(angle) * ringRadius);
                float py = centerY - (float)(Math.Sin(angle) * ringRadius);

                bool active = _activePetal == petal;
                g.FillEllipse(active ? activePetalBrush : petalBrush,
                    px - petalRadius, py - petalRadius, petalRadius * 2, petalRadius * 2);
                g.DrawEllipse(active ? activeBorderPen : borderPen,
                    px - petalRadius, py - petalRadius, petalRadius * 2, petalRadius * 2);

                // The four slots are laid out as a small cross inside the petal, in ABXY order.
                (float dx, float dy)[] slotOffsets =
                [
                    (0f, 30f),   // A - bottom
                    (30f, 0f),   // B - right
                    (-30f, 0f),  // X - left
                    (0f, -30f),  // Y - top
                ];

                for (int slot = 0; slot < slots.Length; slot++)
                {
                    var key = slots[slot];
                    var (dx, dy) = slotOffsets[slot];
                    var cell = new RectangleF(px + dx - 26, py + dy - 15, 52, 30);

                    bool flashing = ReferenceEquals(key, _flashingSlot) && DateTime.UtcNow < _flashEnd;
                    using var brush = new SolidBrush(flashing ? Color.White : slotColors[slot]);

                    string label = key.Action == SpecialAction.None
                        ? (_shift != ShiftMode.Off ? key.ShiftedChar : key.NormalChar).ToString()
                        : key.Label;

                    g.DrawString(label, key.Action == SpecialAction.None ? slotFont : specialFont, brush, cell, sf);

                    if (active)
                    {
                        var tag = new RectangleF(px + dx - 26, py + dy + 11, 52, 12);
                        g.DrawString(DaisywheelLayout.SlotNames[slot], tagFont, dimBrush, tag, sf);
                    }
                }
            }
        }
    }

    public void SetTypingMode(bool daisywheel)
    { lock (_lock) _daisywheel = daisywheel; Invalidate(); }

    public void SetActivePetal(int? petal)
    { lock (_lock) _activePetal = petal; Invalidate(); }

    /// <summary>Flashes a daisywheel slot white to acknowledge the keypress.</summary>
    public void FlashSlot(KeyDef? key)
    {
        if (key is null) return;
        lock (_lock)
        {
            _flashingSlot = key;
            _flashEnd = DateTime.UtcNow.AddMilliseconds(120);
        }
        Invalidate();
        _ = Task.Delay(150).ContinueWith(_ =>
        {
            lock (_lock) _flashingSlot = null;
            try { BeginInvoke(Invalidate); } catch { }
        });
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

    public void SetModifierState(ShiftMode shift, bool sym)
    { lock (_lock) { _shift = shift; _symActive = sym; } Invalidate(); }

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
