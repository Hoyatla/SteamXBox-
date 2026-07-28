using System.Collections.ObjectModel;
using System.IO;
using SteamXBox.Gui.Models;

namespace SteamXBox.Gui.Services;

public sealed class ProfileService
{
    public ObservableCollection<ProfileData> Profiles { get; } = [];

    public ProfileData? ActiveProfile { get; set; }

    public event Action<ProfileData>? ProfileSaved;

    public void LoadAll()
    {
        Profiles.Clear();

        var list = ProfileData.LoadAll();

        var defaultProfile = list.FirstOrDefault(p => p.Name == "Default");
        if (defaultProfile is null)
        {
            defaultProfile = new ProfileData { Name = "Default" };
            list.Insert(0, defaultProfile);
        }

        var ordered = list
            .OrderBy(p => p.Name == "Default" ? 0 : 1)
            .ThenBy(p => p.Name)
            .ToList();

        foreach (var p in ordered)
            Profiles.Add(p);

        ActiveProfile ??= Profiles.FirstOrDefault();
    }

    public void Save(ProfileData profile)
    {
        profile.Save();

        var existing = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing is not null)
        {
            var idx = Profiles.IndexOf(existing);
            Profiles[idx] = profile;
        }
        else
        {
            var insertAt = Profiles.Count(p => p.Name != "Default");
            Profiles.Insert(insertAt + 1, profile);
        }

        if (ActiveProfile?.Name == profile.Name)
            ActiveProfile = profile;

        ProfileSaved?.Invoke(profile);
    }

    public void Delete(ProfileData profile)
    {
        if (profile.Name == "Default") return;
        profile.Delete();
        Profiles.Remove(profile);
        if (ActiveProfile?.Name == profile.Name)
            ActiveProfile = Profiles.FirstOrDefault();
    }

    public ProfileData CreateNew(string name)
    {
        return CreateNew(name, new ProfileData());
    }

    public ProfileData CreateNew(string name, ProfileData source)
    {
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create profile named 'Default'.");
        var p = source.Clone();
        p.Name = name;
        Save(p);
        return p;
    }
}
