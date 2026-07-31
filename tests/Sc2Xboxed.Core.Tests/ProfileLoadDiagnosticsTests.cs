using Sc2Xboxed.Core.Diagnostics;
using Sc2Xboxed.Core.Mapping;

namespace Sc2Xboxed.Core.Tests;

public sealed class ProfileLoadDiagnosticsTests
{
    [Fact]
    public void AMissingProfileIsReportedLoudlyInsteadOfSilentlyDefaulting()
    {
        // A name that cannot exist on disk, so this exercises the fallback path.
        var result = ProfileMapper.LoadDetailed($"__no_such_profile_{Guid.NewGuid():N}");

        Assert.False(result.FileFound);
        Assert.Empty(result.Values);

        var report = string.Join("\n", SessionReport.Profile("missing", result));

        // The silent fallback is what made a mis-named profile look like "tuning does nothing".
        Assert.Contains("WARNING", report);
        Assert.Contains("built-in defaults", report);
    }

    [Fact]
    public void MissingProfileStillYieldsUsableDefaults()
    {
        var result = ProfileMapper.LoadDetailed($"__no_such_profile_{Guid.NewGuid():N}");

        Assert.Equal(Sc2XboxedProfileSettings.Default, result.Settings);
    }

    [Fact]
    public void EffectiveSettingsFlagAnAbsurdScrollSensitivity()
    {
        var settings = Sc2XboxedProfileSettings.Default with
        {
            LeftPadScroll = LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 600.0 },
        };

        var report = string.Join("\n", SessionReport.EffectiveSettings(settings));

        // 600 units per pad unit is ~1200 notches for a single full swipe.
        Assert.Contains("SUSPICIOUS", report);
    }

    [Fact]
    public void EffectiveSettingsAcceptATunedScrollSensitivity()
    {
        var settings = Sc2XboxedProfileSettings.Default with
        {
            LeftPadScroll = LeftTouchpadScrollSettings.Default with { WheelDeltaPerPadUnit = 10.0 },
        };

        var report = string.Join("\n", SessionReport.EffectiveSettings(settings));

        Assert.DoesNotContain("SUSPICIOUS", report);
        Assert.Contains("notches per full swipe", report);
    }

    [Fact]
    public void EffectiveSettingsRecordBothInvertFlags()
    {
        var report = string.Join("\n", SessionReport.EffectiveSettings(Sc2XboxedProfileSettings.Default));

        // Direction is the setting most likely to be misdiagnosed, so it must always be visible.
        Assert.Contains("invertX / invertY", report);
        Assert.Contains("invertVertical", report);
    }

    [Fact]
    public void IdentityIncludesVersionAndExecutablePath()
    {
        var report = string.Join("\n", SessionReport.Identity(["xbox-run", "--profile", "test"]));

        Assert.Contains("version", report);
        Assert.Contains("executable", report);
        Assert.Contains("--profile test", report);
    }
}
