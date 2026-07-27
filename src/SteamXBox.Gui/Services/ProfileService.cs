using System.Collections.ObjectModel;
using System.IO;
using SteamXBox.Gui.Models;

namespace SteamXBox.Gui.Services;

public sealed class ProfileService
{
    public ObservableCollection<ProfileData> Profiles { get; } = [];

    public ProfileData? ActiveProfile { get; set; }

    public void LoadAll()
    {
        Profiles.Clear();
        foreach (var p in ProfileData.LoadAll())
            Profiles.Add(p);

        ActiveProfile ??= Profiles.FirstOrDefault();
    }

    public void Save(ProfileData profile)
    {
        profile.Save();
        var idx = Profiles.ToList().FindIndex(p => p.Name == profile.Name);
        if (idx >= 0)
            Profiles[idx] = profile;
        else
            Profiles.Add(profile);

        if (ActiveProfile?.Name == profile.Name)
            ActiveProfile = profile;
    }

    public void Delete(ProfileData profile)
    {
        profile.Delete();
        Profiles.Remove(profile);
        if (ActiveProfile?.Name == profile.Name)
            ActiveProfile = Profiles.FirstOrDefault();
    }

    public ProfileData CreateNew(string name)
    {
        var p = new ProfileData { Name = name };
        Save(p);
        return p;
    }
}
