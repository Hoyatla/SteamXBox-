using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Services;
using SteamXBox.Gui.Views;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class MainWindow : Window
{
    private readonly UIElement[] _views;
    private readonly SettingsService _settings = new();
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
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var idx))
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
        }
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
