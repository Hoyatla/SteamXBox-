using System.IO.Pipes;

namespace Sc2Xboxed.Core.Input;

public sealed class PadDataSender : IAsyncDisposable
{
    private const string PipeName = "SteamXBox_OskPad";
    private readonly List<NamedPipeServerStream> _clients = new();
    private bool _isRunning;

    public bool HasClients => _clients.Count > 0;

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
            try
            {
                var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync().ConfigureAwait(false);
                _clients.Add(server);
            }
            catch { break; }
        }
    }

    public void SendPadState(TouchpadSample rightPad, TouchpadSample leftPad)
    {
        if (_clients.Count == 0) return;

        var buffer = new byte[36];
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

    private static void WriteDouble(Span<byte> span, ref int offset, double value)
    {
        BitConverter.TryWriteBytes(span.Slice(offset, 8), value);
        offset += 8;
    }

    public async ValueTask DisposeAsync()
    {
        _isRunning = false;
        foreach (var client in _clients)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        _clients.Clear();
    }
}
