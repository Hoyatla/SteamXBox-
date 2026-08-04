using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Core.Input;
using SteamXBox.Gui.Services;

namespace SteamXBox.Gui.ViewModels;

/// <summary>One connected controller, as a numbered button.</summary>
/// <remarks>
/// The number is the position in the list, not the XInput slot. Slots have gaps — a pad on slot 2
/// with nothing on 0 or 1 is normal — and showing "Manette 3" for the only controller attached is
/// the kind of detail that makes an interface feel broken.
/// </remarks>
public partial class ControllerSlotItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _profileName = "";

    /// <summary>Whether this controller belongs to the family of the tab being looked at.</summary>
    /// <remarks>
    /// Emphasis, never a filter. Every connected controller is listed in every tab — a pad that
    /// disappears when the tab changes reads as a pad that disconnected, which is the one thing the
    /// list exists to rule out.
    /// </remarks>
    [ObservableProperty] private bool _isCurrentFamily = true;

    public required int Number { get; init; }

    public required ControllerIdentity Identity { get; init; }

    public string DisplayName => Identity.DisplayName;

    /// <summary>Whether this controller will still be recognised tomorrow.</summary>
    /// <remarks>
    /// Surfaced rather than hidden. An Xbox pad is identified by its slot and nothing else, so its
    /// profile cannot survive a reconnection — and a setting that silently fails to stick is worse
    /// than one that says it will not.
    /// </remarks>
    public bool IsDurable => ControllerIdentityFactory.IsStable(Identity.Id);
}

/// <summary>
/// The connected controllers, clickable, shared by the Profile and Xbox tabs.
/// </summary>
/// <remarks>
/// One instance for both tabs. Two would drift: assigning a profile in one and looking at the other
/// would show a stale list, and the tab that happened to refresh last would win.
/// </remarks>
public partial class ControllerStripViewModel : ObservableObject
{
    private readonly ControllerProfileStore _store;
    private ControllerProfileBook _book;

    public ControllerStripViewModel(string defaultProfile)
    {
        _store = new ControllerProfileStore();
        _book = _store.Load(defaultProfile);
        Refresh();
    }

    /// <summary>The one strip, shared by the Profile and Xbox tabs.</summary>
    /// <remarks>
    /// A single instance on purpose. Two would drift: assigning a profile in one tab and looking at
    /// the other would show a stale list, and whichever tab refreshed last would appear to be right.
    /// </remarks>
    public static ControllerStripViewModel Shared { get; } =
        new(App.SettingsSvc?.Settings.LastActiveProfile ?? "default");

    public ObservableCollection<ControllerSlotItem> Controllers { get; } = [];

    /// <summary>Which family of controllers the strip is showing, or null for all of them.</summary>
    /// <remarks>
    /// The navigation is grouped by family — Steam, PS5, Xbox — because the three have genuinely
    /// different capabilities and a profile written for trackpads means nothing on a pad that has
    /// none. Filtering here rather than building a strip per family keeps one source of truth for
    /// what is attached; the tabs choose a view of it, they do not each keep their own.
    /// </remarks>
    [ObservableProperty] private ControllerKind? _family;

    /// <summary>Shows only one family, or all of them when passed null.</summary>
    public void ShowFamily(ControllerKind? family)
    {
        Family = family;
        Refresh();
        FamilyChanged?.Invoke(family);
    }

    /// <summary>Raised when the tab switches to another family of controller.</summary>
    /// <remarks>
    /// The Xbox tab relabels its binding rows from this, so a row reads "Rond (B)" under the
    /// PlayStation tabs and "B" under the Xbox ones. An event rather than the tab polling the strip:
    /// the strip is the one that knows, and a poll would relabel a frame late — long enough to be
    /// seen.
    /// </remarks>
    public event Action<ControllerKind?>? FamilyChanged;

    [ObservableProperty] private ControllerSlotItem? _selected;

    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Re-reads what is attached, keeping the current selection if it is still there.</summary>
    /// <remarks>
    /// Rebuilt rather than diffed: the list is at most four entries and is only refreshed when the
    /// user is looking at it. The stable ordering that makes this safe is
    /// <see cref="ControllerRoster"/>'s doing — a list that reshuffles between two refreshes is one
    /// nobody can point at.
    /// </remarks>
    public void Refresh()
    {
        var previous = Selected?.Identity.Id;

        var roster = ControllerRoster.Build(
            SafeHidPaths(),
            Sc2Xboxed.Windows.XInputControllerSource.ConnectedSlots());

        Controllers.Clear();

        // Every connected controller, in every tab. Filtering the list down to the tab's family was
        // my own addition and it was wrong: the user asked to see what is connected, and a pad that
        // vanishes when you change tab reads as a pad that disconnected. The family now only decides
        // which entries are emphasised, never which ones exist.
        var number = 1;
        foreach (var identity in roster)
        {
            Controllers.Add(new ControllerSlotItem
            {
                Number = number++,
                Identity = identity,
                ProfileName = _book.ProfileFor(identity.Id),
                IsCurrentFamily = Family is null || identity.Kind == Family,
            });
        }

        Selected = Controllers.FirstOrDefault(c => c.Identity.Id == previous)
                   ?? Controllers.FirstOrDefault();

        foreach (var controller in Controllers)
        {
            controller.IsSelected = ReferenceEquals(controller, Selected);
        }

        StatusMessage = Controllers.Count switch
        {
            0 => "Aucune manette connectée.",
            1 => "1 manette connectée.",
            _ => $"{Controllers.Count} manettes connectées.",
        };
    }

    private static IEnumerable<string> SafeHidPaths()
    {
        try
        {
            return new Sc2Xboxed.Hid.SteamHidDiscovery()
                .ListValveDevices()
                .Select(d => d.DevicePath)
                .ToList();
        }
        catch (Exception ex)
        {
            // A HID enumeration that fails must not empty the Xbox pads out of the list too.
            UiLog.Failure("enumerating Valve HID devices", ex);
            return [];
        }
    }

    [RelayCommand]
    private void Select(ControllerSlotItem? item)
    {
        if (item is null)
        {
            return;
        }

        Selected = item;

        foreach (var controller in Controllers)
        {
            controller.IsSelected = ReferenceEquals(controller, item);
        }

        UiLog.Action("select controller", $"{item.DisplayName} [{item.Identity.Id}]");
    }

    /// <summary>Files the selected controller under a profile.</summary>
    public void AssignToSelected(string? profileName)
    {
        if (Selected is null)
        {
            return;
        }

        _book.Assign(Selected.Identity.Id, profileName);
        Selected.ProfileName = _book.ProfileFor(Selected.Identity.Id);
        _store.Save(_book);

        StatusMessage = Selected.IsDurable
            ? $"{Selected.DisplayName} → {Selected.ProfileName}"
            // Said plainly rather than discovered later: XInput offers a slot and nothing else, so
            // this assignment cannot be restored onto the right pad tomorrow.
            : $"{Selected.DisplayName} → {Selected.ProfileName} (cette session seulement)";

        UiLog.Action("assign profile", StatusMessage);
    }

    /// <summary>Drops assignments naming profiles that no longer exist.</summary>
    public void ForgetMissingProfiles(IEnumerable<string> existingProfiles)
    {
        _book.DropMissingProfiles(existingProfiles);
        _store.Save(_book);
        Refresh();
    }

    /// <summary>The profile a controller should run on.</summary>
    public string ProfileFor(string controllerId) => _book.ProfileFor(controllerId);
}
