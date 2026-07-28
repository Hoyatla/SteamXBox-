using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SteamXBox.Gui.Models;
using SteamXBox.Gui.Views;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class MainWindow : Window
{
    private readonly UIElement[] _views;
    private int _currentTabIndex;
    private readonly SteamXBox.Gui.Services.SettingsService _settings = new();

    public MainWindow()
    {
        InitializeComponent();
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

        _settings.Load();
        RestoreWindowState(_settings.Settings.TabWindowStates[0]);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var idx))
        {
            SaveCurrentWindowState(_currentTabIndex);
            _currentTabIndex = idx;
            RestoreWindowState(_settings.Settings.TabWindowStates[idx]);

            for (var i = 0; i < _views.Length; i++)
                _views[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;

            _settings.Save();
        }
    }

    private void SaveCurrentWindowState(int tabIndex)
    {
        var s = _settings.Settings.TabWindowStates[tabIndex] ??= new TabWindowState();
        s.Width = RestoreBounds.Width;
        s.Height = RestoreBounds.Height;
        s.Left = RestoreBounds.Left;
        s.Top = RestoreBounds.Top;
        s.WindowState = WindowState.ToString();
    }

    private void RestoreWindowState(TabWindowState? s)
    {
        if (s is null) return;
        WindowState = Enum.TryParse<WindowState>(s.WindowState, out var ws) ? ws : WindowState.Normal;
        if (s.Width > 0 && s.Height > 0)
        {
            Width = s.Width;
            Height = s.Height;
        }
        if (!double.IsNaN(s.Left) && !double.IsNaN(s.Top))
        {
            Left = s.Left;
            Top = s.Top;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentWindowState(_currentTabIndex);
        _settings.Save();
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
