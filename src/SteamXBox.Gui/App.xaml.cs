using System.Windows;
using SteamXBox.Gui.ViewModels;

namespace SteamXBox.Gui;

public partial class App : Application
{
    public static MainViewModel MainVm { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var profileService = new Services.ProfileService();
        profileService.LoadAll();

        MainVm = new MainViewModel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainVm?.Dispose();
        base.OnExit(e);
    }
}
