using System.Windows;
using SteamXBox.Gui.Services;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class App : Application
{
    public static ProfileService ProfileSvc { get; private set; } = null!;
    public static MainViewModel MainVm { get; private set; } = null!;
    public static DebugViewModel DebugVm { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before any window is built, so the first render is already in the right language.
        try
        {
            var settings = new SettingsService();
            settings.Load();
            Localization.Strings.Current.Apply(settings.Settings.Language);
        }
        catch
        {
            Localization.Strings.Current.Apply(Localization.AppLanguage.System);
        }

        ProfileSvc = new ProfileService();
        ProfileSvc.LoadAll();

        MainVm = new MainViewModel();
        DebugVm = new DebugViewModel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainVm?.Dispose();
        base.OnExit(e);
    }
}
