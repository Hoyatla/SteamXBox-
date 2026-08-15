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

    /// <summary>
    /// What the chip says, renaming allowed: the name the user gave this pad, or its real one when
    /// it has none. Observable so a rename shows up on the chip the moment it is saved.
    /// </summary>
    [ObservableProperty] private string _displayName = "";
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
        _names = _store.LoadNames();
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

    // The names the user gave their controllers, keyed by family. Cosmetic: a pad that asks to be
    // called "Papa" keeps answering to "Papa" in the strip, and the logs stay honest about which one
    // is being described. Keyed by family for the same reason the profiles are: a pad reconnects
    // under a different Bluetooth address from one day to the next, and the family is the one thing
    // that never changes. Two pads of the same family share the name — they share their settings too.
    private readonly Dictionary<string, string> _names;

    /// <summary>Re-reads what is attached, keeping the current selection if it is still there.</summary>
    /// <remarks>
    /// Rebuilt rather than diffed: the list is at most four entries and is only refreshed when the
    /// user is looking at it. The stable ordering that makes this safe is
    /// <see cref="ControllerRoster"/>'s doing — a list that reshuffles between two refreshes is one
    /// nobody can point at.
    /// </remarks>
    public void Refresh()
    {
        ControllerIdentityFactory.DurableKeyResolver ??= path => Sc2Xboxed.Windows.DeviceTree.DurableKeyFor(path);

        var previous = Selected?.Identity.Id;

        // The same resolver the runtime uses, or the two would file the same pad under two different
        // identities — the GUI saving a profile the Core never looks up.
        var roster = ControllerRoster.Build(
            SafeHidPaths(),

            // Physical pads only. Every attached controller now gets its virtual Xbox pad as soon as
            // it connects, rather than on the first frame sent in Xbox mode — that is what lets a
            // game started later enumerate it. The side effect is that each controller occupies a
            // second XInput slot from the moment it is plugged in, and this list would show it as
            // another controller: one pad in the user's hands, two chips in the strip, and no way to
            // tell which is which.
            //
            // The user sees the slot of the controller they are holding. The pad SteamXBox creates
            // for it is machinery, not a device anyone owns.
            //
            // Only a slot positively identified as emulated is hidden. A slot that cannot be resolved
            // stays in the list: hiding a controller the user is holding is far worse than showing
            // one entry too many, and the resolution is ambiguous whenever two identical pads are
            // attached.
            Sc2Xboxed.Windows.XInputControllerSource.ConnectedSlots()
                .Where(slot => !Sc2Xboxed.Windows.XInputDurableIdentity.IsEmulatedSlot(slot)),

            SafeDualSensePaths(),
            slot => Sc2Xboxed.Windows.XInputDurableIdentity.For(slot));

        Controllers.Clear();

        // Every connected controller, in every tab. Filtering the list down to the tab's family was
        // my own addition and it was wrong: the user asked to see what is connected, and a pad that
        // vanishes when you change tab reads as a pad that disconnected. The family now only decides
        // which entries are emphasised, never which ones exist.
        // The number is the position in the list. Settings are shared by the whole family, so the
        // number only has to point at the pad while it is attached — it stops meaning anything the
        // moment the pad is unplugged, which is exactly when a remembered number would be relied on.
        // The profile name comes from the family, not the pad: two identical DualSenses — even one
        // that connects under a different Bluetooth address tomorrow — read the same family's
        // settings, which is the whole point of filing by family.
        var number = 1;

        foreach (var identity in roster)
        {
            Controllers.Add(new ControllerSlotItem
            {
                Number = number++,
                Identity = identity,
                DisplayName = _names.TryGetValue(identity.FamilyId, out var name) ? name : identity.DisplayName,
                ProfileName = _book.ProfileFor(identity.FamilyId),
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

    private static IEnumerable<string> SafeDualSensePaths()
    {
        try
        {
            return Sc2Xboxed.Hid.DualSenseControllerSource.Discover()
                .Select(d => d.DevicePath)
                .ToList();
        }
        catch (Exception ex)
        {
            // A failed enumeration of one family must not empty the other two out of the list.
            UiLog.Failure("enumerating DualSense devices", ex);
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

    /// <summary>Files the selected controller's family under a profile, true when it was assigned.</summary>
    /// <remarks>
    /// The assignment belongs to the family, not to the pad. Two identical pads — even one that
    /// connects under a different Bluetooth address tomorrow — share their family's settings, so
    /// assigning here covers them both, and nothing needs to be identified by pressing it.
    /// </remarks>
    public bool AssignToSelected(string? profileName)
    {
        if (Selected is null)
        {
            return false;
        }

        var familyId = Selected.Identity.FamilyId;
        if (string.IsNullOrEmpty(familyId))
        {
            StatusMessage = "Cette manette n'a pas de famille de réglages.";
            return false;
        }

        _book.Assign(familyId, profileName);
        Selected.ProfileName = _book.ProfileFor(familyId);
        _store.Save(_book);

        StatusMessage = $"{Selected.DisplayName} → {Selected.ProfileName}";
        UiLog.Action("assign profile", $"{familyId} → {Selected.ProfileName}");
        return true;
    }

    /// <summary>Drops assignments naming profiles that no longer exist.</summary>
    public void ForgetMissingProfiles(IEnumerable<string> existingProfiles)
    {
        _book.DropMissingProfiles(existingProfiles);
        _store.Save(_book);
        Refresh();
    }

    /// <summary>The profile a controller family should run on.</summary>
    public string ProfileFor(string familyId) => _book.ProfileFor(familyId);

    /// <summary>Asks the user what to call a controller, then remembers it.</summary>
    /// <remarks>
    /// The dialog is a view concern, but it is driven from here so the strip's own code-behind stays
    /// a thin shell: persistence, the chip label and the log entry all live in one place, the same
    /// way the strip already owns the profile assignments.
    /// </remarks>
    public void Rename(ControllerSlotItem item)
    {
        var current = _names.TryGetValue(item.Identity.Id, out var saved) ? saved : item.Identity.DisplayName;
        var dialog = new SteamXBox.Gui.Views.RenameDialog(current)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.ControllerName?.Trim() ?? "";
        if (name.Length == 0)
        {
            return;
        }

        _names[item.Identity.FamilyId] = name;
        _store.SaveNames(_names);
        item.DisplayName = name;

        MigrateProfileForRename(item.Identity.FamilyId, current, name);

        StatusMessage = $"Manette renommée : {name}";
        UiLog.Action("rename controller", $"{item.Identity.Id} → {name}");
    }

    /// <summary>
    /// Renaming a chip must not strand its family's profile under the old name. Saving always writes
    /// the profile file under the family's profile name, so without this a rename left the old file
    /// behind and the next save created a second one — two profiles for one family, the stale one
    /// still launching under <c>lastActiveProfile</c> while the editor worked on the new. Renamed
    /// here so the file, the assignment and the active profile all move together.
    /// </summary>
    private void MigrateProfileForRename(string familyId, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) ||
            oldName.Equals(newName, StringComparison.OrdinalIgnoreCase) ||
            oldName.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // No assignment of its own: the family runs the launch profile, and renaming a chip must not
        // rename another family's settings — or the shared fallback. Nothing to migrate.
        if (!_book.HasOwnProfile(familyId))
        {
            return;
        }

        var service = App.ProfileSvc;
        if (service is null)
        {
            return;
        }

        var assigned = _book.ProfileFor(familyId);
        var target = service.Profiles.FirstOrDefault(p =>
            p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase) ||
            (!assigned.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrWhiteSpace(assigned) &&
             p.Name.Equals(assigned, StringComparison.OrdinalIgnoreCase)));

        var migrated = false;
        if (target is not null && !target.Name.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            var renamed = target.Clone();
            renamed.Name = newName;
            service.Save(renamed);
            service.Delete(target);
            migrated = true;
        }

        // The assignment follows the rename whenever it pointed at the old name — or at a profile we
        // just moved — so the next save reuses the same file instead of creating a third one.
        if (migrated || assigned.Equals(oldName, StringComparison.OrdinalIgnoreCase))
        {
            _book.Assign(familyId, newName);
            _store.Save(_book);
        }

        // The runtime launches the profile named in the settings. It must follow too, or the bridge
        // keeps running the stale file under the old name after the rename.
        var settingsService = App.SettingsSvc;
        if (settingsService is not null &&
            settingsService.Settings.LastActiveProfile.Equals(oldName, StringComparison.OrdinalIgnoreCase))
        {
            settingsService.Settings.LastActiveProfile = newName;
            settingsService.Save();
        }

        if (App.MainVm.SelectedProfile?.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase) == true)
        {
            App.MainVm.SelectedProfile =
                service.Profiles.FirstOrDefault(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                ?? service.ActiveProfile;
        }
    }

    /// <summary>Forgets the name the user gave a controller, showing its real one again.</summary>
    public void ResetName(ControllerSlotItem item)
    {
        if (_names.Remove(item.Identity.FamilyId))
        {
            _store.SaveNames(_names);
        }

        item.DisplayName = item.Identity.DisplayName;
        StatusMessage = $"Nom réinitialisé : {item.DisplayName}";
        UiLog.Action("reset controller name", item.Identity.Id);
    }
}
