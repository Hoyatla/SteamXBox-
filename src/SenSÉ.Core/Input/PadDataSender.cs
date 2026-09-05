using System.IO.Pipes;

namespace SenSÉ.Core.Input;

/// <summary>
/// What kind of controller drives the overlay keyboard.
/// </summary>
/// <remarks>
/// The overlay is one window but two keyboards: a Steam Controller types on its touchpads, a
/// pad that only has joysticks types on the sticks. The kind travels on every frame so the
/// overlay can answer the controller that is actually feeding it, instead of keeping both input
/// paths live and letting a resting thumb fight the other hand.
/// </remarks>
public enum OskControllerKind : byte
{
    /// <summary>A Valve controller: the touchpads type.</summary>
    Steam = 0,

    /// <summary>A pad with joysticks and no Steam touchpads: the sticks type.</summary>
    Sticks = 1,
}

public sealed class PadDataSender : IAsyncDisposable
{
    /// <summary>Name of the pipe this sender serves; one per keyboard.</summary>
    /// <remarks>
    /// Injected rather than fixed. A constant here meant one keyboard for the whole machine: a
    /// second controller asking for one would find the pipe already bound and silently get nothing.
    /// </remarks>
    private readonly string _pipeName;

    /// <param name="pipeName">Pipe to serve. Defaults to the single-keyboard name that shipped before.</param>
    public PadDataSender(string? pipeName = null)
        => _pipeName = string.IsNullOrWhiteSpace(pipeName) ? "SenSÉ_OskPad" : pipeName;
    private readonly List<NamedPipeServerStream> _clients = new();
    private readonly object _lock = new();
    private bool _isRunning;

    public bool HasClients { get { lock (_lock) return _clients.Count > 0; } }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _ = AcceptClientsAsync();
    }

    private async Task AcceptClientsAsync()
    {
        while (_isRunning)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync().ConfigureAwait(false);
                lock (_lock) { _clients.Add(server); }
                server = null;
            }
            catch
            {
                server?.Dispose();
                if (_isRunning) await Task.Delay(500).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Layout of the frames on this pipe.
    /// </summary>
    /// <remarks>
    /// Written first on every frame, and checked by the reader. The pipe carries fixed-size frames,
    /// so a sender and a reader that disagree on the size do not fail — they slide out of step and
    /// stay there, and the overlay types whatever the misread bytes happen to mean. A version byte
    /// turns that into a clean disconnection.
    ///
    /// Bumped when the sticks and triggers were added for controllers that have no trackpads, and
    /// again when the controller kind was added so the overlay types on pads or sticks depending
    /// on who is feeding it.
    /// </remarks>
    public const byte ProtocolVersion = 3;

    /// <summary>
    /// Fixed frame size: version, two touchpad samples, the button mask, then both sticks and both
    /// triggers, then the controller kind.
    /// </summary>
    /// <remarks>
    /// The overlay needs the buttons for daisywheel typing, where ABXY pick the character, and the
    /// sticks for a controller with no pads, where each stick aims at its own half of the keyboard.
    /// The kind byte is appended after the triggers so the stick values keep their known offsets.
    /// </remarks>
    public const int FrameSize = 1 + 44 + 48 + 1;

    public void SendPadState(
        TouchpadSample rightPad,
        TouchpadSample leftPad,
        SteamControllerButtons buttons,
        NormalizedStick leftStick,
        NormalizedStick rightStick,
        double leftTrigger,
        double rightTrigger,
        OskControllerKind kind)
    {
        var buffer = new byte[FrameSize];
        var span = buffer.AsSpan();
        int offset = 0;

        span[offset++] = ProtocolVersion;

        WriteDouble(span, ref offset, rightPad.X);
        WriteDouble(span, ref offset, rightPad.Y);
        span[offset++] = (byte)(rightPad.IsTouched ? 1 : 0);
        span[offset++] = (byte)(rightPad.IsPressed ? 1 : 0);

        WriteDouble(span, ref offset, leftPad.X);
        WriteDouble(span, ref offset, leftPad.Y);
        span[offset++] = (byte)(leftPad.IsTouched ? 1 : 0);
        span[offset++] = (byte)(leftPad.IsPressed ? 1 : 0);

        WriteUInt64(span, ref offset, (ulong)buttons);

        WriteDouble(span, ref offset, leftStick.X);
        WriteDouble(span, ref offset, leftStick.Y);
        WriteDouble(span, ref offset, rightStick.X);
        WriteDouble(span, ref offset, rightStick.Y);
        WriteDouble(span, ref offset, leftTrigger);
        WriteDouble(span, ref offset, rightTrigger);

        span[offset++] = (byte)kind;

        lock (_lock)
        {
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    _clients[i].Write(buffer, 0, offset);
                    _clients[i].Flush();
                }
                catch
                {
                    try { _clients[i].Dispose(); } catch { }
                    _clients.RemoveAt(i);
                }
            }
        }
    }

    private static void WriteDouble(Span<byte> span, ref int offset, double value)
    {
        BitConverter.TryWriteBytes(span.Slice(offset, 8), value);
        offset += 8;
    }

    private static void WriteUInt64(Span<byte> span, ref int offset, ulong value)
    {
        BitConverter.TryWriteBytes(span.Slice(offset, 8), value);
        offset += 8;
    }

    public async ValueTask DisposeAsync()
    {
        _isRunning = false;
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                try { client.Dispose(); } catch { }
            }
            _clients.Clear();
        }
    }
}
