using System.IO.Pipes;

namespace Sc2Xboxed.Core.Input;

public sealed class PadDataSender : IAsyncDisposable
{
    private const string PipeName = "SteamXBox_OskPad";
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
                    PipeName,
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
    /// Bumped when the sticks and triggers were added for controllers that have no trackpads.
    /// </remarks>
    public const byte ProtocolVersion = 2;

    /// <summary>
    /// Fixed frame size: version, two touchpad samples, the button mask, then both sticks and both
    /// triggers.
    /// </summary>
    /// <remarks>
    /// The overlay needs the buttons for daisywheel typing, where ABXY pick the character, and the
    /// sticks for a controller with no pads, where each stick aims at its own half of the keyboard.
    /// </remarks>
    public const int FrameSize = 1 + 44 + 48;

    public void SendPadState(
        TouchpadSample rightPad,
        TouchpadSample leftPad,
        SteamControllerButtons buttons,
        NormalizedStick leftStick,
        NormalizedStick rightStick,
        double leftTrigger,
        double rightTrigger)
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
