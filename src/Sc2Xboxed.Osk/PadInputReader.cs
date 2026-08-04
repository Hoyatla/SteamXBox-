using System.IO;
using System.IO.Pipes;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Osk;

public sealed class PadInputReader : IAsyncDisposable
{
    private const string PipeName = "SteamXBox_OskPad";
    private NamedPipeClientStream? _pipe;

    public bool IsOpen => _pipe is not null && _pipe.IsConnected;

    public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
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

            // A sender and a reader that disagree on the frame size do not fail, they slide out of
            // step and stay there — and the overlay would type whatever the misread bytes mean.
            // Disconnecting is the honest outcome: it is visible, and it stops on its own.
            if (buffer[offset++] != PadDataSender.ProtocolVersion)
            {
                yield break;
            }

            var rightX = ReadDouble(buffer, ref offset);
            var rightY = ReadDouble(buffer, ref offset);
            bool rightTouched = buffer[offset++] != 0;
            bool rightPressed = buffer[offset++] != 0;

            var leftX = ReadDouble(buffer, ref offset);
            var leftY = ReadDouble(buffer, ref offset);
            bool leftTouched = buffer[offset++] != 0;
            bool leftPressed = buffer[offset++] != 0;

            var buttons = (SteamControllerButtons)ReadUInt64(buffer, ref offset);

            // Carried for controllers with no trackpads: each stick aims at its own half of the
            // keyboard, and the triggers commit the key that half has selected.
            var leftStick = new NormalizedStick(ReadDouble(buffer, ref offset), ReadDouble(buffer, ref offset));
            var rightStick = new NormalizedStick(ReadDouble(buffer, ref offset), ReadDouble(buffer, ref offset));
            var leftTrigger = ReadDouble(buffer, ref offset);
            var rightTrigger = ReadDouble(buffer, ref offset);

            var right = new TouchpadSample(rightTouched, rightX, rightY, 0.0, rightPressed);
            var left = new TouchpadSample(leftTouched, leftX, leftY, 0.0, leftPressed);

            yield return new ControllerState(
                TimeSpan.FromMilliseconds(Environment.TickCount64),
                buttons,
                leftStick,
                rightStick,
                leftTrigger,
                rightTrigger,
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
