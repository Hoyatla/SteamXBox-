using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SteamXBox.Gui.Views;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class MainWindow : Window
{
    private readonly UIElement[] _views;

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
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var idx))
        {
            for (var i = 0; i < _views.Length; i++)
                _views[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
