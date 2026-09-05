using System.IO;
using System.IO.Pipes;
using SenSÉ.Core.Input;

namespace SenSÉ.Osk;

/// <summary>One decoded frame from the pad pipe, with the kind of controller it came from.</summary>
public readonly record struct OskPadFrame(OskControllerKind Kind, ControllerState State);

public sealed class PadInputReader : IAsyncDisposable
{
    private readonly string _pipeName;

    /// <param name="pipeName">Pipe to read. Must match what the bridge serves for this keyboard.</param>
    public PadInputReader(string? pipeName = null)
        => _pipeName = string.IsNullOrWhiteSpace(pipeName) ? "SenSÉ_OskPad" : pipeName;
    private NamedPipeClientStream? _pipe;

    public bool IsOpen => _pipe is not null && _pipe.IsConnected;

    public async IAsyncEnumerable<OskPadFrame> ReadFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[PadDataSender.FrameSize];

        // The whole loop is the reconnection: the bridge closes and reopens this controller's
        // instance on the same pipe name (a Bluetooth pad that drops and returns, a controller
        // re-enumerated by a rescan). A keyboard that gave up on its first disconnect would stay
        // deaf for the rest of the session — on screen, and dead to the sticks aiming at it.
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In);
                await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                pipe?.Dispose();
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            _pipe = pipe;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Byte-mode pipes can split a frame; a partial read must not desynchronize the stream.
                        await pipe.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { yield break; }
                    catch (EndOfStreamException) { break; }
                    catch (IOException) { break; }

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

                    var kind = (OskControllerKind)buffer[offset++];

                    var right = new TouchpadSample(rightTouched, rightX, rightY, 0.0, rightPressed);
                    var left = new TouchpadSample(leftTouched, leftX, leftY, 0.0, leftPressed);

                    yield return new OskPadFrame(kind, new ControllerState(
                        TimeSpan.FromMilliseconds(Environment.TickCount64),
                        buttons,
                        leftStick,
                        rightStick,
                        leftTrigger,
                        rightTrigger,
                        left,
                        right));
                }
            }
            finally
            {
                _pipe = null;
                pipe.Dispose();
            }
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
