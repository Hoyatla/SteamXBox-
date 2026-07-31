using System.Windows;
using SteamXBox.Gui.Services;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class App : Application
{
    public static ProfileService ProfileSvc { get; private set; } = null!;

    /// <summary>
    /// The one settings instance for the whole application.
    /// </summary>
    /// <remarks>
    /// Each view model used to build its own, and every <c>Save()</c> rewrites the entire file from
    /// that instance's in-memory copy. Whichever tab saved last silently reverted the others: picking
    /// a profile then touching anything in Settings put <c>lastActiveProfile</c> back to whatever it
    /// was at launch, and ticking "start with Windows" was undone the same way.
    /// </remarks>
    public static SettingsService SettingsSvc { get; private set; } = null!;

    public static MainViewModel MainVm { get; private set; } = null!;
    public static DebugViewModel DebugVm { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SettingsSvc = new SettingsService();

        // Before any window is built, so the first render is already in the right language.
        try
        {
            SettingsSvc.Load();
            Localization.Strings.Current.Apply(SettingsSvc.Settings.Language);
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
