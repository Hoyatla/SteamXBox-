using System.IO;
using System.Windows;
using System.Windows.Markup;
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

        TryApplyExternalSkin();

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

        ShowMainWindow();
    }

    /// <summary>
    /// Builds and shows the main window, dropping the external skin and retrying once if it throws.
    /// </summary>
    /// <remarks>
    /// Loading a skin cannot validate it. A dictionary parses happily while still holding a resource
    /// of the wrong type — a Color where a Brush is expected, say — and that only throws when a
    /// window actually resolves it. Under StartupUri that exception was unhandled and the process
    /// died with no window and no message, which is precisely how a bad skin presented itself.
    /// A skin is cosmetic; it must never be able to stop the application from running.
    /// </remarks>
    private void ShowMainWindow()
    {
        try
        {
            new MainWindow().Show();
            return;
        }
        catch (Exception exception)
        {
            RemoveExternalSkin();
            SkinFailure = exception.Message;
        }

        // Second attempt on the built-in theme. If this throws too, the fault is not the skin and
        // the exception must surface rather than be swallowed.
        new MainWindow().Show();
    }

    /// <summary>Why the external skin was rejected, or null when none was.</summary>
    public static string? SkinFailure { get; private set; }

    private static void RemoveExternalSkin()
    {
        if (_externalSkin is not null)
        {
            Current.Resources.MergedDictionaries.Remove(_externalSkin);
            _externalSkin = null;
        }
    }

    private static ResourceDictionary? _externalSkin;

    /// <summary>Name of the optional skin dropped next to the executable.</summary>
    private const string SkinFileName = "skin.xaml";

    /// <summary>
    /// Merges <c>skin.xaml</c> from the application directory when it exists, on top of the built-in
    /// theme.
    /// </summary>
    /// <remarks>
    /// Every view refers to the theme only through named resource keys, so a dictionary that
    /// redefines those keys restyles the whole interface without touching a single view. Merged last
    /// means it wins the lookup, and this runs before any window is built so the first render is
    /// already skinned. A broken or missing skin is not an error: the built-in theme stays.
    /// </remarks>
    private static void TryApplyExternalSkin()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SkinFileName);
            if (!File.Exists(path))
            {
                return;
            }

            using var stream = File.OpenRead(path);
            if (XamlReader.Load(stream) is ResourceDictionary skin)
            {
                Current.Resources.MergedDictionaries.Add(skin);
                _externalSkin = skin;
            }
        }
        catch
        {
            // A malformed skin must never stop the application from starting.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MainVm?.Dispose();
        base.OnExit(e);
    }
}
