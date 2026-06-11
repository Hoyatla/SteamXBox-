namespace Sc2Xboxed.Windows;

public sealed class TrackpadMousePolicy
{
    private readonly WindowsForegroundProcessProvider _foregroundProcessProvider;
    private readonly HashSet<string> _disabledProcessNames;

    public TrackpadMousePolicy(
        TrackpadMouseMode mode,
        IReadOnlyCollection<string> disabledProcessNames,
        WindowsForegroundProcessProvider foregroundProcessProvider)
    {
        Mode = mode;
        _foregroundProcessProvider = foregroundProcessProvider;
        _disabledProcessNames = disabledProcessNames
            .Select(NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public TrackpadMouseMode Mode { get; }

    public bool IsMouseOutputEnabled()
    {
        return Mode switch
        {
            TrackpadMouseMode.Always => !IsForegroundDisabled(),
            TrackpadMouseMode.DisableForForegroundProcesses => !IsForegroundDisabled(),
            TrackpadMouseMode.Never => false,
            _ => false
        };
    }

    private bool IsForegroundDisabled()
    {
        if (_disabledProcessNames.Count == 0)
        {
            return false;
        }

        var foreground = _foregroundProcessProvider.GetForegroundProcess();
        if (foreground is null)
        {
            return false;
        }

        return _disabledProcessNames.Contains(NormalizeProcessName(foreground.ProcessName)) ||
            (!string.IsNullOrWhiteSpace(foreground.FileName) &&
             _disabledProcessNames.Contains(NormalizeProcessName(Path.GetFileName(foreground.FileName))));
    }

    private static string NormalizeProcessName(string processName)
    {
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
    }
}
