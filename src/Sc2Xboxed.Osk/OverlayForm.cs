using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.Osk;

public sealed class OverlayForm : Form
{
    private double _boardY;
    private double _boardX;
    private double _keyW;
    private double _keyH;
    private int _screenW;
    private int _screenH;

    // Origin of the virtual desktop. It goes negative as soon as a monitor sits to the left of or
    // above the primary one, which is why every draw converts screen coordinates to client ones.
    private int _originX;
    private int _originY;

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

    /// <summary>
    /// Where to write placement decisions. Set by the host so they land in the overlay's log.
    /// </summary>
    /// <remarks>
    /// The log recorded the layout, the scale and the floating flag, and then never said where the
    /// board went or why. That is the one thing a placement bug is about, and its absence turned a
    /// perfectly ordinary "it ran in fixed mode" into a report that nothing had been done — with no
    /// way, from the log alone, to tell that apart from the placement rules failing.
    /// </remarks>
    /// <remarks>
    /// Hidden from designer serialisation: this is a runtime hook, not a form property, and the
    /// WinForms analyser rightly refuses a public delegate on a Control without saying so.
    /// </remarks>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<string>? Log { get; set; }

    public double BoardX => _boardX;
    public double BoardY => _boardY;
    public double KeyW => _keyW;
    public double KeyH => _keyH;

    /// <summary>
    /// Standard key size, in pixels. Keys are wider than they are tall, like a real keyboard: the
    /// thumb travels further horizontally than vertically, and square keys wasted vertical space.
    /// The board is sized from these rather than from the screen width, so it stays the same
    /// physical size on a 1080p laptop and on an ultrawide.
    /// </summary>
    public const int StandardKeyWidth = 92;
    public const int StandardKeyHeight = 69;

    /// <summary>User scale, as a percentage of the standard size.</summary>
    /// <remarks>
    /// Hidden from designer serialisation: this form is built in code, and WinForms otherwise refuses
    /// a public property on a Form that does not declare how it serialises.
    /// </remarks>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ScalePercent { get; set; } = 100;

    /// <summary>
    /// True: the board follows the text field and dodges the pointer. False: it is pinned to the
    /// bottom of the screen and never moves.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Floating { get; set; } = true;

    public int BoardWidth => (int)Math.Round(StandardKeyWidth * KeyboardLayout.MaxCols * ScalePercent / 100.0);
    public int BoardHeight => (int)Math.Round(StandardKeyHeight * KeyboardLayout.Rows * ScalePercent / 100.0);

    /// <summary>
    /// Sizes the window to the whole virtual desktop rather than the primary monitor.
    /// </summary>
    /// <remarks>
    /// The overlay used to be created as a rectangle from (0,0) to the primary screen's size. On a
    /// multi-monitor desktop, a keyboard placed next to a field on any other monitor was computed
    /// correctly and then drawn outside the window, so nothing appeared at all.
    /// </remarks>
    private void CoverAllScreens()
    {
        var virtualScreen = SystemInformation.VirtualScreen;
        _originX = virtualScreen.X;
        _originY = virtualScreen.Y;
        _screenW = Math.Max(1, virtualScreen.Width);
        _screenH = Math.Max(1, virtualScreen.Height);
    }

    /// <summary>Screen coordinate to client coordinate. Everything drawn goes through these.</summary>
    private double ToClientX(double screenX) => screenX - _originX;

    private double ToClientY(double screenY) => screenY - _originY;

    /// <summary>
    /// Moves the board next to whatever is being typed into. Called each time the overlay is shown.
    /// </summary>
    /// <remarks>
    /// Recomputed on every show rather than once at construction: the field moves between one use
    /// and the next, and a keyboard pinned where the last field happened to be is no better than a
    /// keyboard pinned to the bottom of the screen.
    /// </remarks>
    public void UpdatePlacement()
    {
        // Monitors can be plugged in, unplugged or rearranged while the overlay sits resident, so the
        // virtual desktop is re-measured on every show rather than only at construction.
        CoverAllScreens();
        if (IsHandleCreated)
        {
            Bounds = new Rectangle(_originX, _originY, _screenW, _screenH);
        }

        if (!Floating)
        {
            // Fixed mode: bottom centre of the screen the caret is on, and nothing else moves it.
            //
            // The screen is chosen by the caret rather than by the foreground window. They are
            // usually the same, but not always — a dialog can own the foreground while the field
            // being typed into sits on the other monitor — and a keyboard pinned to the wrong screen
            // is useless to the hand that asked for it.
            var fixedPlacement = FixedPlacement();

            lock (_lock)
            {
                _boardX = fixedPlacement.Bounds.X;
                _boardY = fixedPlacement.Bounds.Y;
                _keyW = fixedPlacement.Bounds.Width / (double)KeyboardLayout.MaxCols;
                _keyH = fixedPlacement.Bounds.Height / (double)KeyboardLayout.Rows;
            }

            LastPlacement = fixedPlacement.Kind;
            LogPlacement("show/fixed", fixedPlacement);
            Invalidate();
            return;
        }

        var placement = FloatingPlacement();

        lock (_lock)
        {
            _boardX = placement.Bounds.X;
            _boardY = placement.Bounds.Y;
            _keyW = placement.Bounds.Width / (double)KeyboardLayout.MaxCols;
            _keyH = placement.Bounds.Height / (double)KeyboardLayout.Rows;
        }

        LastPlacement = placement.Kind;
        LogPlacement("show/floating", placement);
        Invalidate();
    }

    /// <summary>
    /// Where the pinned board belongs: the bottom centre of the screen the caret is on.
    /// </summary>
    private OverlayPlacementResult FixedPlacement()
    {
        // The screen holding the text field, and failing that the screen holding the mouse pointer.
        //
        // Not the foreground window, which was the previous fallback and is the wrong question: a
        // dialog can own the foreground while the field being typed into sits on the other monitor,
        // and a full-screen application can own it while the user is working elsewhere. The pointer
        // is where the user is, which is the next best thing to knowing where the caret is.
        var caret = CaretLocator.FindActiveFieldDetailed();
        var area = caret.Rect.IsEmpty
            ? CaretLocator.PointerWorkArea()
            : CaretLocator.WorkAreaFor(caret.Rect);

        _lastWorkArea = area;
        return OverlayPlacement.PlaceFixed(BoardWidth, BoardHeight, area);
    }

    /// <summary>
    /// The work area measured at the last placement, for anything that must not ask again.
    /// </summary>
    /// <remarks>
    /// Painting in particular. Measuring is a question put to the application being typed into, and
    /// a repaint happens far too often to be allowed to ask one.
    /// </remarks>
    private ScreenRect _lastWorkArea;

    /// <summary>Where the floating board belongs, given where the caret is right now.</summary>
    private OverlayPlacementResult FloatingPlacement()
    {
        var field = CaretLocator.FindActiveFieldDetailed();

        // The monitor of the foreground window, not the primary one. With no caret to locate, the
        // keyboard used to go home to monitor one while the user was typing on monitor two.
        var work = field.Rect.IsEmpty
            ? CaretLocator.ForegroundWorkArea()
            : CaretLocator.WorkAreaFor(field.Rect);

        _lastWorkArea = work;
        return OverlayPlacement.Place(BoardWidth, BoardHeight, field.Rect, field.IsCaret, work);
    }

    private void LogPlacement(string why, OverlayPlacementResult placement)
        => Log?.Invoke(
            $"Placement {why}: {placement.Kind} at "
            + $"({placement.Bounds.X},{placement.Bounds.Y}) {placement.Bounds.Width}x{placement.Bounds.Height} "
            + $"floating={Floating}");

    /// <summary>Where the board ended up last time, for the log.</summary>
    public PlacementKind LastPlacement { get; private set; } = PlacementKind.ScreenBottom;

    /// <summary>
    /// Moves the board out from under the mouse pointer when it comes near.
    /// </summary>
    /// <remarks>
    /// The pointer and the keyboard are driven by the same hands — the right pad moves the cursor
    /// while the board sits on the text field — so they collide constantly. Rather than let the
    /// keyboard block the pointer, the board steps aside.
    ///
    /// The daisywheel is excluded: it is centred on the screen by design and has no position to
    /// negotiate.
    /// </remarks>
    private void DodgePointer()
    {
        // A pinned keyboard does not move, pointer or no pointer: predictability is the whole point
        // of choosing fixed mode.
        if (_daisywheel || !Floating)
        {
            return;
        }

        ScreenRect board;
        lock (_lock)
        {
            board = new ScreenRect((int)_boardX, (int)_boardY, BoardWidth, BoardHeight);
        }

        var cursor = Cursor.Position;
        var work = CaretLocator.WorkAreaFor(board);

        if (OverlayAvoidance.Dodge(board, cursor.X, cursor.Y, work) is not { } moved)
        {
            return;
        }

        lock (_lock)
        {
            _boardX = moved.X;
            _boardY = moved.Y;
        }

        Invalidate();
    }

    public OverlayForm()
    {
        CoverAllScreens();
        // Fixed key size: the board is sized from its own content instead of stretching across the
        // whole screen. That is what makes it float rather than sit as a full-width band.
        _keyW = StandardKeyWidth;
        _keyH = StandardKeyHeight;
        // Screen coordinates, like everywhere else the board origin is read. Written as client
        // coordinates here it disagreed with the rest by the virtual desktop's origin.
        _boardX = _originX + ((_screenW - BoardWidth) / 2.0);
        _boardY = _originY + _screenH - BoardHeight - 40;

        Text = "SteamXBox Keyboard";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(_originX, _originY, _screenW, _screenH);
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        DoubleBuffered = true;

        // 120 ms rather than a second: this both re-asserts topmost and runs the pointer dodge, and a
        // keyboard that took a second to notice the cursor arriving would be worse than one that
        // never moved. It is two cheap calls, not a repaint.
        _topMostTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (!IsHandleCreated || !Visible)
            {
                return;
            }

            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            DodgePointer();
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
                // Converted to client coordinates, like every other thing drawn here. The board
                // origin is a screen position — DodgePointer compares it with Cursor.Position, and
                // Program builds the stick cursors from it — but the form covers the whole virtual
                // desktop, whose top-left is not the screen origin. Drawing the keys at the raw
                // screen value was invisible on a single monitor at (0,0) and slid the keyboard by
                // the virtual desktop's origin as soon as a second screen sat left of the first,
                // which put it across the join between the two.
                double x = ToClientX(_boardX) + key.Col * _keyW;
                double y = ToClientY(_boardY) + key.Row * _keyH;
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

        // Centre sur l'ecran qui porte le champ actif, converti en coordonnees client.
        //
        // La zone est celle mesuree au dernier placement, pas une nouvelle interrogation. Appeler
        // FindActiveField ici revenait a poser une question UI Automation a l'application visee a
        // chaque repaint -- c'est-a-dire a chaque survol de touche -- et c'est elle qui repond, sur
        // son propre thread d'interface. Un rendu ne doit rien demander a personne.
        var wheelArea = _lastWorkArea;
        float centerX = (float)ToClientX(wheelArea.X + (wheelArea.Width / 2.0));
        float centerY = (float)ToClientY(wheelArea.Bottom - 250.0);
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
            // Cursor positions arrive in screen coordinates, like the board they follow.
            if (_rightVisible)
                DrawCircle(g, (float)ToClientX(_rightCursorX), (float)ToClientY(_rightCursorY), 12,
                    Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            if (_leftVisible)
                DrawCircle(g, (float)ToClientX(_leftCursorX), (float)ToClientY(_leftCursorY), 12,
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
