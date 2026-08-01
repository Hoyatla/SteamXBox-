using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;
using SteamXBox.Gui.Views;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class MainWindow : Window
{
    private readonly UIElement[] _views;
    // The shared instance, not a private one: a second copy here would rewrite the whole settings
    // file from its own snapshot every time a tab was resized, reverting the language, the last
    // active profile and the Windows startup flag saved by the other view models.
    private readonly SettingsService _settings = App.SettingsSvc;
    private int _currentTabIndex;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainVm;
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            Icon = new BitmapImage(iconUri);
        }
        catch { }

        ProfileView.DataContext = new ProfileViewModel();
        XboxView.DataContext = new XboxViewModel();
        SettingsView.DataContext = new SettingsViewModel();
        DebugView.DataContext = App.DebugVm;

        _views = [HomeView, ProfileView, XboxView, SettingsView, LogView, DebugView];

        try
        {
            _settings.Load();
            EnsureTabSizes();
            RestoreTabSize(_currentTabIndex);
        }
        catch { }
    }

    /// <summary>
    /// Tabs hold very different amounts of content, so each remembers the size the user gave it.
    /// Position is deliberately not restored: a saved position can land the window on a monitor that
    /// is no longer connected.
    /// </summary>
    private void EnsureTabSizes()
    {
        var sizes = _settings.Settings.TabSizes;
        if (sizes.Length >= _views.Length)
        {
            return;
        }

        var grown = new TabSize[_views.Length];
        for (var i = 0; i < grown.Length; i++)
        {
            grown[i] = i < sizes.Length && sizes[i] is not null ? sizes[i] : new TabSize();
        }

        _settings.Settings.TabSizes = grown;
    }

    private void SaveTabSize(int tabIndex)
    {
        // RestoreBounds rather than Width/Height: while maximized those report the maximized size,
        // which would be restored as a normal window filling the screen.
        if (tabIndex < 0 || tabIndex >= _settings.Settings.TabSizes.Length)
        {
            return;
        }

        var bounds = WindowState == WindowState.Normal
            ? new Size(Width, Height)
            : new Size(RestoreBounds.Width, RestoreBounds.Height);

        if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
        {
            return;
        }

        _settings.Settings.TabSizes[tabIndex] = new TabSize { Width = bounds.Width, Height = bounds.Height };
    }

    private void RestoreTabSize(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _settings.Settings.TabSizes.Length)
        {
            return;
        }

        var size = _settings.Settings.TabSizes[tabIndex];
        if (size is null || !size.IsUsable || WindowState == WindowState.Maximized)
        {
            return;
        }

        Width = size.Width;
        Height = size.Height;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        // FrameworkElement rather than Button: the navigation items are RadioButtons so that a theme
        // can style the active tab, and a skin is free to retemplate them into anything at all.
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var idx))
        {
            try
            {
                SaveTabSize(_currentTabIndex);
                RestoreTabSize(idx);
                _settings.Save();
            }
            catch { }

            _currentTabIndex = idx;

            for (var i = 0; i < _views.Length; i++)
                _views[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;

            AnimateIn(_views[idx]);
        }
    }

    /// <summary>
    /// Fades and slides the incoming view in, using timings the theme provides.
    /// </summary>
    /// <remarks>
    /// Driven from resources rather than hard-coded so a skin owns its own sense of movement: a
    /// short dry slide reads as sober, a longer one with overshoot reads as playful. A theme that
    /// defines nothing gets no animation at all, which is also how this behaves if the resources are
    /// missing — the view is simply shown, exactly as before.
    ///
    /// Only the incoming view is animated. Cross-fading would need both views visible at once, and
    /// they are laid out on top of each other in the same cell.
    /// </remarks>
    private void AnimateIn(UIElement view)
    {
        var duration = TryFindResource("TabTransitionDuration") as Duration?;
        if (duration is not { HasTimeSpan: true } span || span.TimeSpan <= TimeSpan.Zero)
        {
            view.Opacity = 1;
            return;
        }

        var offset = TryFindResource("TabTransitionOffset") as double? ?? 0.0;
        var ease = TryFindResource("TabTransitionEase") as IEasingFunction;

        view.BeginAnimation(OpacityProperty, null);
        view.Opacity = 0;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span) { EasingFunction = ease });

        if (offset == 0)
        {
            return;
        }

        // A fresh transform each time: reusing one that is still animating leaves the view offset.
        var transform = new TranslateTransform();
        view.RenderTransform = transform;
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(offset, 0, span) { EasingFunction = ease });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            SaveTabSize(_currentTabIndex);
            _settings.Save();
        }
        catch { }

        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    /// <summary>Community invite. Opens in the user's browser, never in an embedded view.</summary>
    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.gg/MmmvB5s3E",
                UseShellExecute = true,
            });
        }
        catch
        {
            // No default browser, or the shell refused: not worth interrupting the user over.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
