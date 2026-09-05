using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenSÉ.Plugins;
using SenSÉ.Shell.Localization;
using SenSÉ.Shell.Theming;
using SenSÉ.Shell.Theming.Windows;

namespace SenSÉ.Desktop.Settings;

/// <summary>
/// The Windows-theming half of the settings screen.
/// </summary>
/// <remarks>
/// A separate part of <see cref="SettingsViewModel"/> because it is a different kind of setting:
/// everything else on that screen changes SenSÉ, this changes the machine. Keeping it in its
/// own file makes that boundary visible in the source, not only in the interface.
///
/// The theme and the cursors live together here on purpose. They are two facets of one choice — how
/// the environment and the machine look — so they are picked the same way and applied by the same
/// button, rather than one taking effect on selection and the other needing a click.
/// </remarks>
public partial class SettingsViewModel
{
    private readonly WindowsStateBackup _windowsBackup = new();

    /// <summary>Cursor packs found in the plugin library.</summary>
    public List<PluginManifest> CursorPacks { get; } = [];

    [ObservableProperty] private PluginManifest? _selectedCursorPack;
    [ObservableProperty] private string _cursorAttribution = "";
    [ObservableProperty] private string _windowsThemeStatus = "";
    [ObservableProperty] private bool _hasWindowsBackup;

    /// <summary>Where the plugin library lives, next to the executable.</summary>
    private static string PluginRoot => Path.Combine(AppContext.BaseDirectory, "Plugins");

    /// <summary>Fills the cursor list. Called from the constructor.</summary>
    private void LoadWindowsPlugins()
    {
        HasWindowsBackup = _windowsBackup.Exists;
        _themeAtOpen = _settingsService.Settings.Theme ?? "";
        WindowsThemeStatus = ThemeLoader.LastOutcome;

        var scan = PluginCatalog.Scan(PluginRoot);
        CursorPacks.AddRange(scan.Loaded.Where(p => p.Kind == PluginCategory.ThemeWindows));

        // Rejections are shown rather than swallowed: a plugin refused because it does not declare
        // how to undo itself is exactly what its author needs to be told.
        if (scan.Rejected.Count > 0)
        {
            var first = scan.Rejected[0];
            WindowsThemeStatus = Strings.Current.Format(
                "Plugin refusé — {0} : {1}", Path.GetFileName(first.Directory), first.Reason);
        }
    }

    partial void OnSelectedCursorPackChanged(PluginManifest? value)
        => CursorAttribution = value is null
            ? ""
            : string.Join("  ·  ", new[] { value.Author, value.Licence, value.Source }.Where(s => s.Length > 0));

    /// <summary>Theme in force when the screen opened, to tell whether it actually changed.</summary>
    private string _themeAtOpen = "";

    /// <summary>
    /// Applies the chosen cursors, then the chosen theme.
    /// </summary>
    /// <remarks>
    /// The cursors need an explicit action because they write into Windows, and browsing a list
    /// must never modify the machine.
    ///
    /// The theme is applied last because it restarts the environment: merging a new dictionary into
    /// a running application repaints only the parts that resolve through <c>DynamicResource</c>,
    /// which is most of a window left in the old colours. Nothing after that call would run, so the
    /// cursors go first.
    /// </remarks>
    [RelayCommand]
    private void ApplyWindowsTheme()
    {
        var themeChanged = (SelectedTheme?.Id ?? "") != _themeAtOpen;

        try
        {
            var manifest = SelectedCursorPack;
            if (manifest is null)
            {
                // Not an error: changing the theme alone is a legitimate use of this button.
                if (!themeChanged)
                {
                    WindowsThemeStatus = Strings.Current["Aucun paquet de curseurs sélectionné."];
                    return;
                }

                App.RestartForTheme();
                return;
            }

            if (manifest.EntryPath.Length == 0 || !File.Exists(manifest.EntryPath))
            {
                WindowsThemeStatus = Strings.Current["Point d'entrée du plugin introuvable."];
                return;
            }

            var scheme = CursorSchemeParser.Load(manifest.EntryPath);

            // A .crs carries no scheme name; without this the mouse control panel would keep
            // showing the previous scheme's name beside the new cursors.
            if (scheme.Name.Length == 0)
            {
                scheme = scheme with { Name = manifest.Name };
            }

            var folder = Path.GetDirectoryName(manifest.EntryPath)!;
            var (applied, missing) = CursorInstaller.Apply(scheme, folder, _windowsBackup);

            HasWindowsBackup = _windowsBackup.Exists;

            WindowsThemeStatus = missing.Count == 0
                ? Strings.Current.Format("{0} : {1} curseurs appliqués.", manifest.Name, applied)
                : Strings.Current.Format(
                    "{0} : {1} curseurs appliqués, {2} fichiers absents du paquet.",
                    manifest.Name, applied, missing.Count);
        }
        catch (Exception exception)
        {
            WindowsThemeStatus = Strings.Current.Format("Échec : {0}", exception.Message);
            return;
        }

        // Le thème en dernier : il redémarre l'environnement, donc rien de ce qui suit ne
        // s'exécuterait.
        if (themeChanged)
        {
            App.RestartForTheme();
        }
    }

    [RelayCommand]
    private void RestoreWindowsTheme()
    {
        try
        {
            WindowsThemeStatus = CursorInstaller.Restore(_windowsBackup)
                ? Strings.Current["Réglages Windows d'origine restaurés."]
                : Strings.Current["Aucune sauvegarde à restaurer."];

            HasWindowsBackup = _windowsBackup.Exists;
        }
        catch (Exception exception)
        {
            WindowsThemeStatus = Strings.Current.Format("Échec : {0}", exception.Message);
        }
    }
}
