namespace Sc2Xboxed.Windows;

public sealed record HidHideSetupResult(string ApplicationPath, IReadOnlyList<string> HiddenDevices);
