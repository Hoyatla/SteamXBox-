using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Wire format for haptic requests sent from the overlay keyboard process to the core
/// process. The core owns the only HID stream that writes haptics, so satellite
/// processes ask for feedback instead of opening the device themselves.
/// </summary>
public static class HapticRequestWire
{
    public const string PipeName = "SteamXBox_OskHaptic";

    /// <summary>
    /// Fixed-size frame: actuator, type, gain, frequency, duration, LFO frequency, LFO depth,
    /// pulse width.
    /// </summary>
    public const int MessageSize = 12;

    public static void Write(Span<byte> destination, HapticCommand command)
    {
        if (destination.Length < MessageSize)
        {
            throw new ArgumentException($"Buffer must be at least {MessageSize} bytes.", nameof(destination));
        }

        destination[0] = (byte)command.Actuator;
        destination[1] = (byte)command.Type;
        destination[2] = unchecked((byte)(sbyte)Math.Clamp(command.GainDb, sbyte.MinValue, sbyte.MaxValue));
        WriteUInt16(destination, 3, command.Frequency);
        WriteUInt16(destination, 5, command.DurationMs);
        WriteUInt16(destination, 7, command.LfoFreq);
        destination[9] = command.LfoDepth;
        WriteUInt16(destination, 10, command.PulseWidthUs);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out HapticCommand command)
    {
        command = default;

        if (source.Length < MessageSize)
        {
            return false;
        }

        var actuator = (HapticActuator)source[0];
        var type = (HapticType)source[1];

        // Reject unknown enum values rather than forwarding them to the report builder,
        // which throws on an out-of-range actuator.
        if (!Enum.IsDefined(actuator) || !Enum.IsDefined(type))
        {
            return false;
        }

        command = new HapticCommand(
            actuator,
            type,
            unchecked((sbyte)source[2]),
            ReadUInt16(source, 3),
            ReadUInt16(source, 5),
            ReadUInt16(source, 7),
            source[9],
            ReadUInt16(source, 10));

        return true;
    }

    private static void WriteUInt16(Span<byte> destination, int offset, ushort value)
    {
        destination[offset] = (byte)(value & 0xFF);
        destination[offset + 1] = (byte)(value >> 8);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset)
    {
        return (ushort)(source[offset] | (source[offset + 1] << 8));
    }
}
