using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Reads an Xbox controller through XInput.
/// </summary>
/// <remarks>
/// XInput rather than HID, even over Bluetooth. Windows presents a paired Xbox Wireless Controller
/// on an XInput slot exactly as it does a wired one, so there is nothing Bluetooth-specific to
/// handle — and reading it the same way the game does means what SteamXBox sees is what the game
/// sees.
///
/// It polls. XInput has no event or overlapped read: <c>XInputGetState</c> returns the current
/// state and nothing else. The interval is the frame budget for the whole pipeline, so it is set
/// once here rather than left to the caller to guess.
/// </remarks>
public sealed class XInputControllerSource : IPhysicalControllerSource
{
    /// <summary>XInput exposes four slots, assigned by Windows in connection order.</summary>
    public const int SlotCount = 4;

    private const uint ErrorSuccess = 0;

    private readonly int _requestedSlot;
    private readonly TimeSpan _interval;

    /// <summary>Slot currently being read, or -1 when no controller has been found yet.</summary>
    public int Slot { get; private set; } = -1;

    /// <param name="slot">Slot to read, or -1 to take the first one that answers.</param>
    /// <param name="pollIntervalMs">
    /// Milliseconds between reads. Eight is about 125 Hz, close to what the Steam Controller
    /// pipeline already runs at, so the pointer feels the same whichever controller is in hand.
    /// </param>
    public XInputControllerSource(int slot = -1, int pollIntervalMs = 8)
    {
        _requestedSlot = slot;
        _interval = TimeSpan.FromMilliseconds(Math.Max(1, pollIntervalMs));
    }

    /// <summary>Whether any XInput controller is connected right now.</summary>
    public static bool AnyConnected() => ConnectedSlots().Count > 0;

    /// <summary>
    /// Every XInput slot that answers right now.
    /// </summary>
    /// <remarks>
    /// The count matters as much as the fact that one answered. Several controllers are meant to be
    /// several inputs, so a machine with four pads attached and one answering slot is a different
    /// problem from one with four answering slots — and from the outside the two look identical.
    /// </remarks>
    public static IReadOnlyList<int> ConnectedSlots()
    {
        var slots = new List<int>();

        for (var i = 0; i < SlotCount; i++)
        {
            if (Connected((uint)i))
            {
                slots.Add(i);
            }
        }

        return slots;
    }

    public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (Slot < 0)
            {
                Slot = FindSlot(_requestedSlot);
                if (Slot < 0)
                {
                    // Nothing connected. Backing off rather than spinning: a disconnected slot is
                    // the normal state while the controller is asleep, and polling it at 125 Hz for
                    // hours would burn a core for nothing.
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            if (XInputGetState((uint)Slot, out var state) != ErrorSuccess)
            {
                // Gone. Forgetting the slot rather than ending the stream: the controller may come
                // back on a different slot after sleeping, and the caller should not have to
                // rebuild the source for that.
                Slot = -1;
                continue;
            }

            yield return XInputStateMapper.Map(
                new XInputFrame(
                    state.Gamepad.Buttons,
                    state.Gamepad.LeftTrigger,
                    state.Gamepad.RightTrigger,
                    state.Gamepad.ThumbLX,
                    state.Gamepad.ThumbLY,
                    state.Gamepad.ThumbRX,
                    state.Gamepad.ThumbRY),
                DateTimeOffset.UtcNow - start);

            await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int FindSlot(int requested)
    {
        if (requested >= 0 && requested < SlotCount)
        {
            return Connected((uint)requested) ? requested : -1;
        }

        for (var i = 0; i < SlotCount; i++)
        {
            if (Connected((uint)i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Connected(uint slot)
    {
        try
        {
            return XInputGetState(slot, out _) == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            // xinput1_4.dll ships with Windows 8 and later. Absent means a system this build does
            // not target, not a fault to throw over.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
}
