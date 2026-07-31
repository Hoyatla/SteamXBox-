using System.IO;
using System.IO.Pipes;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Osk;

public sealed class PadInputReader : IAsyncDisposable
{
    private const string PipeName = "SteamXBox_OskPad";
    private NamedPipeClientStream? _pipe;

    public bool IsOpen => _pipe is not null && _pipe.IsConnected;

    public async IAsyncEnumerable<SteamControllerState> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In);
                await _pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch
            {
                _pipe?.Dispose();
                _pipe = null;
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        var buffer = new byte[PadDataSender.FrameSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Byte-mode pipes can split a frame; a partial read must not desynchronize the stream.
                await _pipe!.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { yield break; }
            catch (EndOfStreamException) { yield break; }
            catch (IOException) { yield break; }

            int offset = 0;
            var rightX = ReadDouble(buffer, ref offset);
            var rightY = ReadDouble(buffer, ref offset);
            bool rightTouched = buffer[offset++] != 0;
            bool rightPressed = buffer[offset++] != 0;

            var leftX = ReadDouble(buffer, ref offset);
            var leftY = ReadDouble(buffer, ref offset);
            bool leftTouched = buffer[offset++] != 0;
            bool leftPressed = buffer[offset++] != 0;

            var buttons = (SteamControllerButtons)ReadUInt64(buffer, ref offset);

            var right = new TouchpadSample(rightTouched, rightX, rightY, 0.0, rightPressed);
            var left = new TouchpadSample(leftTouched, leftX, leftY, 0.0, leftPressed);

            yield return new SteamControllerState(
                TimeSpan.FromMilliseconds(Environment.TickCount64),
                buttons,
                NormalizedStick.Center,
                NormalizedStick.Center,
                0.0,
                0.0,
                left,
                right);
        }
    }

    private static double ReadDouble(byte[] buffer, ref int offset)
    {
        double value = BitConverter.ToDouble(buffer, offset);
        offset += 8;
        return value;
    }

    private static ulong ReadUInt64(byte[] buffer, ref int offset)
    {
        ulong value = BitConverter.ToUInt64(buffer, offset);
        offset += 8;
        return value;
    }

    public ValueTask DisposeAsync()
    {
        _pipe?.Dispose();
        _pipe = null;
        return ValueTask.CompletedTask;
    }
}
