using System.IO;
using System.Runtime.CompilerServices;
using HidSharp;
using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Hid;

namespace Sc2Xboxed.Osk;

public sealed class PadInputReader : IAsyncDisposable
{
    private readonly SteamHidDiscovery _discovery = new();
    private readonly TritonInputReportParser _parser = new();
    private HidStream? _stream;

    public bool IsOpen => _stream is not null;

    public async IAsyncEnumerable<SteamControllerState> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureOpen();

        var buffer = new byte[64];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _stream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (IOException)
            {
                yield break;
            }

            if (bytesRead <= 0)
                continue;

            var report = buffer.AsSpan(0, bytesRead);
            if (_parser.TryParse(report, TimeSpan.FromMilliseconds(Environment.TickCount64), out var state))
            {
                yield return state;
            }
        }
    }

    public void EnsureOpen()
    {
        if (_stream is not null)
            return;

        var device = _discovery.FindPreferredControllerDevice()
            ?? throw new InvalidOperationException("No Steam Controller HID device found.");

        if (!device.TryOpen(out _stream))
            throw new IOException($"Cannot open HID device.");

        _stream.ReadTimeout = 20;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
    }
}
