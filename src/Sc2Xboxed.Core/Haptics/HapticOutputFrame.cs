namespace Sc2Xboxed.Core.Haptics;

public sealed class HapticOutputFrame
{
    public static HapticOutputFrame Empty { get; } = new(Array.Empty<HapticCommand>());

    public HapticOutputFrame(IReadOnlyList<HapticCommand> commands)
    {
        Commands = commands;
    }

    public IReadOnlyList<HapticCommand> Commands { get; }
}
