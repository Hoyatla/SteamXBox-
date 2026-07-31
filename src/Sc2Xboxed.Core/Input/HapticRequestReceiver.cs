using System.IO.Pipes;
using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Core-side server for <see cref="HapticRequestWire"/>. Accepts one satellite process at a
/// time and forwards each request to the core's single haptic sink.
/// </summary>
public sealed class HapticRequestReceiver : IAsyncDisposable
{
    private readonly Func<HapticCommand, CancellationToken, ValueTask> _onRequest;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _worker;

    public HapticRequestReceiver(
        Func<HapticCommand, CancellationToken, ValueTask> onRequest,
        Action<string>? log = null)
    {
        _onRequest = onRequest;
        _log = log;
    }

    public void Start()
    {
        _worker ??= Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    HapticRequestWire.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _log?.Invoke("Haptic request client connected.");

                await ReadRequestsAsync(server, cancellationToken).ConfigureAwait(false);
                _log?.Invoke("Haptic request client disconnected.");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Haptic request pipe error: {ex.GetType().Name}: {ex.Message}");

                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ReadRequestsAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        var buffer = new byte[HapticRequestWire.MessageSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Byte-mode pipes can split a frame, so read the whole message or give up.
                await server.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            if (!HapticRequestWire.TryRead(buffer, out var command))
            {
                _log?.Invoke("Discarded malformed haptic request.");
                continue;
            }

            try
            {
                await _onRequest(command, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Haptic request dispatch failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);

        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _cancellation.Dispose();
    }
}
