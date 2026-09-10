using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SenSÉ.EditeurTexte.UI;

public enum ThemeApp { Light, Dark, SystemDefault }

/// <summary>
/// Gestionnaire de themes clair/sombre. La preference est persistee dans
/// <c>%LOCALAPPDATA%\SenSÉ\Éditeur\settings.json</c>. L'application d'un theme
/// change les couleurs de fond de la Window, du menu, de la toolbar, de la status bar,
/// et de chaque RichTextBox des onglets actifs. Phase I.2 : implementation minimale
/// qui ne redefinit pas tout le ResourceDictionary global - les couleurs systeme
/// des controles natifs WPF (TabControl headers, ComboBox popup) restent celles de Windows.
/// </summary>
public static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ", "Éditeur", "settings.json");

    // Couleurs des themes (cohérentes avec Themes/Light.xaml et Themes/Dark.xaml).
    private static readonly Brush LightWindow   = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush LightEditor   = new SolidColorBrush(Colors.White);
    private static readonly Brush LightForeground = new SolidColorBrush(Colors.Black);
    private static readonly Brush LightStatus   = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private static readonly Brush DarkWindow    = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush DarkEditor    = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
    private static readonly Brush DarkForeground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly Brush DarkStatus    = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));

    static ThemeManager()
    {
        // Freeze pour performance (acces depuis le thread UI).
        LightWindow.Freeze(); LightEditor.Freeze(); LightForeground.Freeze(); LightStatus.Freeze();
        DarkWindow.Freeze();  DarkEditor.Freeze();  DarkForeground.Freeze();  DarkStatus.Freeze();
    }

    public static ThemeApp Charger()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemeApp.SystemDefault;
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var s = t.GetString();
                if (string.Equals(s, "Light", StringComparison.OrdinalIgnoreCase)) return ThemeApp.Light;
                if (string.Equals(s, "Dark", StringComparison.OrdinalIgnoreCase)) return ThemeApp.Dark;
            }
        }
        catch { /* best effort */ }
        return ThemeApp.SystemDefault;
    }

    public static void Sauvegarder(ThemeApp theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var obj = new { theme = theme.ToString() };
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }

    public static void Appliquer(Window window, ThemeApp theme)
    {
        Brush windowBrush, editorBrush, foregroundBrush, statusBrush;
        switch (theme)
        {
            case ThemeApp.Dark:
                windowBrush = DarkWindow; editorBrush = DarkEditor; foregroundBrush = DarkForeground; statusBrush = DarkStatus;
                break;
            case ThemeApp.Light:
                windowBrush = LightWindow; editorBrush = LightEditor; foregroundBrush = LightForeground; statusBrush = LightStatus;
                break;
            default:
                // SystemDefault : on laisse les couleurs systeme. Ne touche a rien.
                return;
        }

        window.Background = windowBrush;
        window.Foreground = foregroundBrush;

        // Status bar.
        var statusBar = FindVisualChild<StatusBar>(window);
        if (statusBar is not null)
        {
            statusBar.Background = statusBrush;
            statusBar.Foreground = foregroundBrush;
        }

        // Menu.
        var menu = FindVisualChild<Menu>(window);
        if (menu is not null) menu.Foreground = foregroundBrush;

        // Tous les RichTextBox des onglets actifs (on parcourt les descendants visuels).
        foreach (var rtb in FindVisualChildren<System.Windows.Controls.RichTextBox>(window))
        {
            rtb.Background = editorBrush;
            rtb.Foreground = foregroundBrush;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }
}
