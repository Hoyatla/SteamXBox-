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
    /// Fixed frame size: two touchpad samples plus the button mask. The overlay needs the buttons
    /// for daisywheel typing, where ABXY pick the character.
    /// </summary>
    public const int FrameSize = 44;

    public void SendPadState(TouchpadSample rightPad, TouchpadSample leftPad, SteamControllerButtons buttons)
    {
        var buffer = new byte[FrameSize];
        var span = buffer.AsSpan();
        int offset = 0;

        WriteDouble(span, ref offset, rightPad.X);
        WriteDouble(span, ref offset, rightPad.Y);
        span[offset++] = (byte)(rightPad.IsTouched ? 1 : 0);
        span[offset++] = (byte)(rightPad.IsPressed ? 1 : 0);

        WriteDouble(span, ref offset, leftPad.X);
        WriteDouble(span, ref offset, leftPad.Y);
        span[offset++] = (byte)(leftPad.IsTouched ? 1 : 0);
        span[offset++] = (byte)(leftPad.IsPressed ? 1 : 0);

        WriteUInt64(span, ref offset, (ulong)buttons);

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
