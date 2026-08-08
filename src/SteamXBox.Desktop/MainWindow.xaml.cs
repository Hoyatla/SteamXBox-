using System.Windows;
using System.Windows.Input;
using SteamXBox.Desktop.ControlCentre;
using Sc2Xboxed.Core.Diagnostics;
using SteamXBox.Shell.Localization;

namespace SteamXBox.Desktop;

/// <summary>
/// The SteamXBox Desktop window, holding the control centre.
/// </summary>
/// <remarks>
/// A floating panel, not a full screen: SteamXBox runs alongside the desktop, so the physical
/// keyboard and mouse keep working everywhere else. The window never activates itself at startup
/// (<c>ShowActivated="False"</c>), so typing keeps going to whatever was in front. Every tile is
/// focusable so the arrow keys — and the gamepad mapped to them — walk the grid once the panel has
/// the focus; the mouse still works, it is simply not what the layout is designed around.
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
        // Bottom-right corner, above the taskbar, so the panel sits out of the way of whatever the
        // user is typing into. Positioned once at startup; the title bar then moves it anywhere.
        Reposition();

        // Focus the first tile so a gamepad or the arrow keys have somewhere to start once the
        // panel has the focus. Guarded on the window being active: at startup the window is
        // deliberately not activated, and this must never steal the foreground from the user.
        if (IsActive)
        {
            Tiles.ApplyTemplate();
            Tiles.UpdateLayout();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    /// <summary>
    /// Spreads the environment over the screen, once it is already shown.
    /// </summary>
    /// <remarks>
    /// After <c>Show()</c>, not in the XAML. WPF refuses outright to display a window that declares
    /// both <c>ShowActivated="False"</c> and <c>WindowState="Maximized"</c> — it throws at
    /// <c>Show()</c> and the environment never appears at all. Both are wanted here: the overlay
    /// covers the desktop, and it must never steal the foreground the moment it starts. Setting the
    /// state after the window exists satisfies the pair.
    /// </remarks>
    private void Reposition()
    {
        WindowState = WindowState.Maximized;
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
