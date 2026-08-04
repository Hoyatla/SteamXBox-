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
/// Built to be driven without a pointer. Every tile is focusable, the arrow keys walk the grid and
/// Enter activates — the vocabulary a gamepad produces once its stick and face buttons are mapped
/// to those keys, so the same window serves the controller and the keyboard without a second code
/// path. The mouse still works; it is simply not what the layout is designed around.
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
        // Focus the first tile so a gamepad or the arrow keys have somewhere to start. Without this
        // the window opens with focus on nothing and the first press appears to do nothing at all.
        Tiles.ApplyTemplate();
        Tiles.UpdateLayout();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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
            // user was actually in. Hidden rather than minimised — the overlay refuses to minimise,
            // and a hidden window also releases the foreground.
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
                Show();
                WindowState = WindowState.Maximized;
                Activate();
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
    /// Keeps the overlay covering the desktop.
    /// </summary>
    /// <remarks>
    /// The title bar buttons are gone, but that is not enough: Win+D, Win+M, clicking the taskbar
    /// button and "show the desktop" all minimise a window without asking it. An overlay that
    /// vanishes on any of those is not an overlay, so the state is put straight back.
    /// </remarks>
    /// <summary>
    /// Quits when the overlay itself is closed by anything other than its own button.
    /// </summary>
    /// <remarks>
    /// Alt+F4 and "close window" from the taskbar bypass the ✕. Under
    /// <see cref="System.Windows.ShutdownMode.OnExplicitShutdown"/> nothing else would end the
    /// process, leaving it running with no window at all — which is the opposite failure to the one
    /// just fixed, and just as confusing.
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        UiLog.Window(nameof(MainWindow), "closed", "quitting SteamXBox");
        base.OnClosed(e);
        App.Quit();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Recorded because it is invisible by design: the overlay flicks back to maximised, so
            // whatever asked it to minimise leaves no other trace of having tried.
            UiLog.Window(nameof(MainWindow), "minimise refused", "overlay must keep covering the desktop");
            WindowState = WindowState.Maximized;
        }

        base.OnStateChanged(e);
    }

    /// <summary>
    /// The title bar is inert.
    /// </summary>
    /// <remarks>
    /// No dragging and no double-click to restore: a window that fills the screen has nowhere to be
    /// dragged to, and restoring it down is exactly what it must not do. Kept as a handler rather
    /// than removed from the XAML so the bar stays a real title bar if the overlay ever becomes
    /// resizable again.
    /// </remarks>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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
