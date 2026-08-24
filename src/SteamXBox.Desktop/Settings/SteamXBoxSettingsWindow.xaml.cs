using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SteamXBox.Desktop.Settings;

/// <summary>
/// SteamXBox's own settings: preferences, logs and diagnostics.
/// </summary>
/// <remarks>
/// These three screens were tabs of the controller configuration window, sitting next to Profiles
/// and Xbox as though they were about the controller. They are not — they are about SteamXBox
/// itself, which is why they belong to the environment.
/// </remarks>
public partial class SteamXBoxSettingsWindow : Window
{
    private readonly UIElement[] _screens;
    private readonly DebugViewModel _debug = new();

    public SteamXBoxSettingsWindow()
    {
        InitializeComponent();

        // La taille et la place que l'utilisateur lui a donnees, d'une session a l'autre.
        SuiviFenetre.Suivre(this, "reglages");

        SettingsView.DataContext = new SettingsViewModel();
        DebugView.DataContext = _debug;
        _screens = [SettingsView, LogView, DebugView, ToolsView];

        // The diagnostics screen used to be fed by the configuration window's device polling. In
        // this process it has to watch for itself, and it should stop when the window closes rather
        // than keep a timer and a HID poll alive for a window nobody is looking at.
        _debug.StartWatching();
        Closed += (_, _) => _debug.StopWatching();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out var index))
        {
            return;
        }

        for (var i = 0; i < _screens.Length; i++)
        {
            _screens[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
