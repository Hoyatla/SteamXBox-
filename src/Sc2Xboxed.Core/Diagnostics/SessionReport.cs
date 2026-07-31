using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Mapping;
using Sc2Xboxed.Core.Osk;

namespace Sc2Xboxed.Core.Diagnostics;

/// <summary>
/// Builds the startup identity and effective-settings dump.
/// </summary>
/// <remarks>
/// Two questions cost real debugging time before this existed: "is the running binary actually the
/// build I just made?" and "what values is the mapper actually using?". Both are answered here, at
/// the top of every log.
/// </remarks>
public static class SessionReport
{
    public static IEnumerable<string> Identity(string[] commandLineArgs)
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version?.ToString() ?? "unknown";
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        yield return $"version        : {version}{(informational is null ? "" : $" ({informational})")}";
        yield return $"executable     : {Environment.ProcessPath ?? AppContext.BaseDirectory}";
        yield return $"base directory : {AppContext.BaseDirectory}";
        yield return $"process id     : {Environment.ProcessId}";
        yield return $"arguments      : {string.Join(' ', commandLineArgs)}";
        yield return $"local time     : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} (UTC{DateTimeOffset.Now.Offset.Hours:+00;-00}:{Math.Abs(DateTimeOffset.Now.Offset.Minutes):00})";
        yield return $"os             : {RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}";
        yield return $"runtime        : {RuntimeInformation.FrameworkDescription}";
        yield return $"elevated       : {IsElevated()}";
    }

    /// <summary>
    /// Formats the resolved profile, marking each value as read from the file or defaulted. A key the
    /// editor writes but the loader ignores shows up here as "(default)" despite being present on disk.
    /// </summary>
    public static IEnumerable<string> Profile(string profileName, ProfileLoadResult result)
    {
        yield return $"profile name   : {(string.IsNullOrEmpty(profileName) ? "<none, built-in defaults>" : profileName)}";
        yield return $"profile file   : {result.FilePath}";
        yield return $"file found     : {result.FileFound}";

        if (!result.FileFound)
        {
            // Silent fallback here means every value the profile editor writes is ignored at runtime,
            // which looks exactly like "tuning does nothing".
            yield return "*** WARNING: profile file missing -- running on built-in defaults. ***";
            yield return "*** Everything configured in the profile editor is being ignored.    ***";
        }

        if (result.Error is { } error)
        {
            yield return $"*** PARSE ERROR: {error} -- falling back to built-in defaults ***";
        }

        foreach (var origin in result.Values)
        {
            yield return $"  {origin}";
        }
    }

    /// <summary>The values the mappers actually run with, after profile resolution.</summary>
    public static IEnumerable<string> EffectiveSettings(Sc2XboxedProfileSettings settings)
    {
        var trackball = settings.RightPadTrackball;
        var scroll = settings.LeftPadScroll;

        yield return "routing";
        yield return $"  rightPadMode          : {settings.RightPadMode}";
        yield return $"  leftPadMode           : {settings.LeftPadMode}";
        yield return $"  leftStickMode         : {settings.LeftStickMode}";
        yield return $"  clickSettleMs         : {F(trackball.ClickSettleMilliseconds)} (whole pad clicks; drag resumes after)";
        yield return $"  finePrecision         : gain {F(trackball.MinAccelerationGain)} over the first {F(trackball.FinePrecisionTravel)} pad units";
        yield return $"  minThrowTravelPixels  : {F(trackball.MinThrowTravelPixels)}";
        yield return $"  touchActivationTravel : {F(trackball.TouchActivationTravel)}";

        yield return "right pad (trackball -> mouse)";
        yield return $"  pixelsPerPadUnit      : {F(trackball.PixelsPerPadUnit)}";
        yield return $"  motionDeadZone        : {F(trackball.MotionDeadZone)} (2D magnitude)";
        yield return $"  invertX / invertY     : {trackball.InvertX} / {trackball.InvertY}";
        yield return $"  inertiaDecayPerSecond : {F(trackball.InertiaDecayPerSecond)}";
        yield return $"  velocityWindowFrames  : {trackball.VelocityWindowFrames}";
        yield return $"  quietFramesToCancel   : {trackball.QuietFramesToCancelThrow}";
        yield return $"  accelerationExponent  : {F(trackball.AccelerationExponent)}{(Math.Abs(trackball.AccelerationExponent - 1.0) < 0.0001 ? " (linear, off)" : "")}";
        yield return $"  edgeSpeedPixelsPerSec : {F(trackball.EdgeSpeedPixelsPerSecond)}{(trackball.EdgeSpeedPixelsPerSecond <= 0 ? " (off)" : $" past radius {F(trackball.EdgeThreshold)}")}";
        yield return $"  stop / max px per sec : {F(trackball.StopSpeedPixelsPerSecond)} / {F(trackball.MaxSpeedPixelsPerSecond)}";

        yield return "left pad (scroll -> wheel)";
        yield return $"  wheelDeltaPerPadUnit  : {F(scroll.WheelDeltaPerPadUnit)}{ScrollSanity(scroll.WheelDeltaPerPadUnit)}";
        yield return $"  motionDeadZone        : {F(scroll.MotionDeadZone)}";
        yield return $"  invertVertical        : {scroll.InvertVertical}";
        yield return $"  inertiaDecayPerSecond : {F(scroll.InertiaDecayPerSecond)}";
        yield return $"  velocityWindowFrames  : {scroll.VelocityWindowFrames}";
        yield return $"  accelerationExponent  : {F(scroll.AccelerationExponent)}{(Math.Abs(scroll.AccelerationExponent - 1.0) < 0.0001 ? " (linear, off)" : "")}";
        yield return $"  horizontalEnabled     : {scroll.HorizontalEnabled}";
        yield return $"  stop / max u per sec  : {F(scroll.StopSpeedUnitsPerSecond)} / {F(scroll.MaxSpeedUnitsPerSecond)}";
        yield return $"  maxCoastNotches       : {scroll.MaxCoastNotches} (hard cap per throw)";

        yield return "sticks";
        yield return $"  stickDeadZone         : {F(settings.StickDeadZone)} (arrow keys in Profile mode)";
        yield return $"  gamepadStickDeadZone  : {F(settings.GamepadStickDeadZone)} (virtual Xbox pad)";
    }

    public static IEnumerable<string> OverlayKeyboard(OskSettings osk)
    {
        yield return $"typingMode            : {osk.TypingMode}";
        yield return $"hoverHaptics          : {osk.HoverHaptics}";
        yield return $"hapticIntensity       : {osk.HapticIntensity} -> hover {osk.HoverPulseUs}us";
        yield return $"clickForce left/right : {osk.LeftClickForce} / {osk.RightClickForce} -> {osk.LeftClickPulseUs}us / {osk.RightClickPulseUs}us";
        yield return $"validateOnRelease     : {osk.ValidateOnRelease}";
        yield return $"cursorSmoothing       : {F(osk.CursorSmoothing)} (fixed)";
    }

    /// <summary>
    /// Flags a scroll sensitivity that will produce absurd wheel travel. A full pad swipe spans 2.0
    /// units, so anything much above ~50 means hundreds of notches per gesture.
    /// </summary>
    private static string ScrollSanity(double wheelDeltaPerPadUnit)
    {
        var notchesPerSwipe = wheelDeltaPerPadUnit * 2.0;
        if (notchesPerSwipe > 200)
        {
            return $"  <-- SUSPICIOUS: ~{F(notchesPerSwipe)} notches per full swipe";
        }

        return $"  (~{F(notchesPerSwipe)} notches per full swipe)";
    }

    private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool IsElevated()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
