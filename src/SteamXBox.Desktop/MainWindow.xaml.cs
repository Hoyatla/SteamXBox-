using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SteamXBox.Desktop.ControlCentre;
using Sc2Xboxed.Core.Diagnostics;
using SteamXBox.Shell.Localization;

namespace SteamXBox.Desktop;

/// <summary>
/// The SteamXBox Desktop window, holding the control centre.
/// </summary>
/// <remarks>
/// Fullscreen windowed: the environment fills the screen but stays an ordinary window, free to go
/// behind the others. It never activates itself (<c>ShowActivated="False"</c>), so typing keeps
/// going to whatever was in front. Every tile is focusable so the arrow keys — and the gamepad
/// mapped to them — walk the grid once the window has the focus; the mouse still works, it is
/// simply not what the layout is designed around.
/// </remarks>
public partial class MainWindow : Window
{
    public static readonly DependencyProperty SelectedHintProperty =
        DependencyProperty.Register(nameof(SelectedHint), typeof(string), typeof(MainWindow),
            new PropertyMetadata(string.Empty));

    /// <summary>Description of the tile under the pointer or the focus, shown under the title.</summary>
    public string SelectedHint
    {
        get => (string)GetValue(SelectedHintProperty);
        set => SetValue(SelectedHintProperty, value);
    }

    public IReadOnlyList<QuickAction> Actions { get; } = QuickActions.All;

    public string VersionText { get; } = SteamXBox.Shell.AppVersionInfo.ProductAndVersion + " Desktop";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reposition();

        // Focus the first tile so a gamepad or the arrow keys have somewhere to start once the
        // window has the focus. Guarded on the window being active: at startup it is deliberately
        // not activated, and this must never steal the foreground from the user.
        if (IsActive)
        {
            Tiles.ApplyTemplate();
            Tiles.UpdateLayout();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    /// <summary>
    /// Spreads the environment over the work area, once the window is already shown.
    /// </summary>
    /// <remarks>
    /// The work area, not <c>WindowState.Maximized</c>. A borderless window
    /// (<c>WindowStyle="None"</c>) maximises over the whole monitor, taskbar included — Windows
    /// treats it as a fullscreen application and keeps the taskbar behind it. Sized to the work
    /// area instead, the window stops where the taskbar begins, so the taskbar stays in front and
    /// clickable, and clicking another window there is what puts this one behind.
    /// </remarks>
    private void Reposition()
    {
        var work = SystemParameters.WorkArea;

        WindowState = WindowState.Normal;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
    }

    // ---- Letting the clicks through ----

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    /// <summary>
    /// Tells Windows that the empty part of the window is not there, as far as the mouse goes.
    /// </summary>
    /// <remarks>
    /// <c>Background="{x:Null}"</c> alone is not enough, and believing it was cost several rounds of
    /// this. A null background stops WPF's <i>internal</i> hit test — no element in the visual tree
    /// is hit — but the window is still an HWND: Windows delivers the click to it, WPF answers
    /// <c>HTCLIENT</c>, and the click dies there. Nothing reaches the window underneath, so no other
    /// window can be brought forward and this one can never go behind.
    ///
    /// <para>
    /// <c>HTTRANSPARENT</c> is the answer that means "keep looking below me". Windows then repeats
    /// the hit test on the next window down and the click lands where the user aimed it.
    /// </para>
    ///
    /// <para>
    /// Per point rather than per window: <c>WS_EX_TRANSPARENT</c> would do the same thing for the
    /// whole surface, which is what the on-screen keyboard uses, but here the tiles have to stay
    /// clickable. The card's own rectangle answers the question — inside it the window keeps the
    /// click, outside it the click goes through.
    /// </para>
    ///
    /// <para>
    /// A cached rectangle, and that part is not an optimisation. <c>WM_NCHITTEST</c> does not arrive
    /// on click: Windows sends it on every mouse move, dozens of times a second. The first version
    /// of this hook called <c>PointFromScreen</c> and <see cref="UIElement.InputHitTest"/> on each
    /// one, walking the visual tree across a work-area-sized window — the pointer lagged, and device
    /// changes lagged with it, because <c>WM_DEVICECHANGE</c> is broadcast to every top-level window
    /// and one slow window delays the broadcast for the whole machine. Comparing four numbers costs
    /// nothing and answers the same question.
    /// </para>
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(PassClicksThroughTheEmptyArea);
        }

        // Setting a flag is free; recomputing happens at most once per hit test that follows a
        // layout pass, rather than on every mouse move.
        LayoutUpdated += (_, _) => _cardBoundsStale = true;
        LocationChanged += (_, _) => _cardBoundsStale = true;
    }

    private Rect _cardBounds = Rect.Empty;
    private bool _cardBoundsStale = true;

    private IntPtr PassClicksThroughTheEmptyArea(
        IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST)
        {
            return IntPtr.Zero;
        }

        // Screen coordinates, packed as two signed 16-bit halves. Signed matters: a monitor left of
        // the primary one has negative x, and reading it unsigned lands the point on the far right.
        var packed = lParam.ToInt32();
        var screenPoint = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));

        if (_cardBoundsStale)
        {
            _cardBounds = CardScreenBounds();
            _cardBoundsStale = false;
        }

        if (_cardBounds.IsEmpty || _cardBounds.Contains(screenPoint))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(HTTRANSPARENT);
    }

    /// <summary>The card's rectangle in physical screen pixels, or empty if it cannot be measured.</summary>
    /// <remarks>
    /// <c>PointToScreen</c> returns device pixels, which is the same space <c>WM_NCHITTEST</c> packs
    /// its point in, so the two compare directly and the DPI never enters into it. Empty is the safe
    /// answer while the visual has no presentation source: the window keeps the click rather than
    /// dropping it somewhere unpredictable.
    /// </remarks>
    private Rect CardScreenBounds()
    {
        try
        {
            if (Card.ActualWidth <= 0 || Card.ActualHeight <= 0)
            {
                return Rect.Empty;
            }

            return new Rect(
                Card.PointToScreen(new Point(0, 0)),
                Card.PointToScreen(new Point(Card.ActualWidth, Card.ActualHeight)));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    // ---- Dragging ----

    private Point _dragOrigin;
    private bool _dragging;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _dragging = true;
        Mouse.Capture((IInputElement)sender);
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        Left += current.X - _dragOrigin.X;
        Top += current.Y - _dragOrigin.Y;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Mouse.Capture(null);
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickAction action })
        {
            return;
        }

        // Every tile runs inside this guard. Without it an exception here reaches the dispatcher,
        // the environment window goes down, and OnExit takes the controller bridge and the overlay
        // keyboard with it: one broken tile shutting down all of SteamXBox. A tile that fails
        // should fail alone and say so.
        UiLog.Action(action.Label);

        try
        {
            // Actions that need the environment window run it themselves and manage their own
            // hide-and-restore, so the overlay is never left off screen waiting for a signal.
            if (action.WithEnvironment is not null)
            {
                SelectedHint = action.WithEnvironment(this);
                UiLog.Action(action.Label, $"completed with the environment: {SelectedHint}");
                return;
            }

            // The remaining keystroke action still needs this window out of the way: it goes to
            // whatever is in front, and the clipboard history has to paste into the application the
            // user was actually in. Hidden rather than closed — the window keeps its position and
            // comes back where it was.
            if (action.YieldsForeground)
            {
                Hide();
                UiLog.Window(nameof(MainWindow), "hidden", $"{action.Label} needs the foreground");
                ShowSignal.Watch(this);
            }

            action.Invoke();
            UiLog.Action(action.Label, "completed");
        }
        catch (Exception exception)
        {
            UiLog.Failure($"tile '{action.Label}'", exception);

            // Whatever went wrong, the overlay must end up back on screen: a hidden environment
            // with no way back is indistinguishable from a crash.
            if (!IsVisible)
            {
                OverlayShower.Show(this);
                UiLog.Window(nameof(MainWindow), "restored", "after a tile failed while hidden");
            }

            SelectedHint = $"{action.Label} : {exception.GetType().Name} — {exception.Message}";
        }
    }

    private void Tile_Focused(object sender, KeyboardFocusChangedEventArgs e) => ShowHint(sender);

    private void Tile_Hovered(object sender, MouseEventArgs e) => ShowHint(sender);

    private void ShowHint(object sender)
    {
        if (sender is FrameworkElement { Tag: QuickAction action })
        {
            SelectedHint = Strings.Current[action.Hint];
        }
    }

    /// <summary>
    /// Quits when the overlay itself is closed by anything other than its own button.
    /// </summary>
    /// <remarks>
    /// Alt+F4 and "close window" from the taskbar bypass the ✕. Under
    /// <see cref="System.Windows.ShutdownMode.OnExplicitShutdown"/> nothing else would end the
    /// process, leaving it running with no window at all.
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        UiLog.Window(nameof(MainWindow), "closed", "quitting SteamXBox");
        base.OnClosed(e);
        App.Quit();
    }

    /// <summary>
    /// The only deliberate way out.
    /// </summary>
    /// <remarks>
    /// Quits explicitly rather than closing the window and relying on a shutdown mode to notice.
    /// The process now ends only here, so nothing that merely closes a window — a calculator, a
    /// settings screen, a capture that failed — can take SteamXBox down with it.
    /// </remarks>
    private void Close_Click(object sender, RoutedEventArgs e) => App.Quit();
}
