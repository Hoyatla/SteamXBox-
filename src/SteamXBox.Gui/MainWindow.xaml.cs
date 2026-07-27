using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SteamXBox.Gui.Views;

namespace SteamXBox.Gui;

public partial class MainWindow : Window
{
    private readonly UIElement[] _views;

    public MainWindow()
    {
        InitializeComponent();
        _views = [HomeView, ProfileView, SettingsView, LogView];
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
}
