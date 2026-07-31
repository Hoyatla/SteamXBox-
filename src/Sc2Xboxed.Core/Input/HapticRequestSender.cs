using System.IO.Pipes;
using System.Threading.Channels;
using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Satellite-side client for <see cref="HapticRequestWire"/>. Submissions are queued and
/// flushed by a background worker: haptics are cosmetic, so a stalled or absent core
/// process must never block the caller.
/// </summary>
public sealed class HapticRequestSender : IAsyncDisposable
{
    private const int QueueCapacity = 8;
    private const int ReconnectDelayMs = 1000;

    private readonly Channel<HapticCommand> _queue;
    private readonly Action<string>? _log;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _worker;

    public HapticRequestSender(Action<string>? log = null)
    {
        _log = log;
        _queue = Channel.CreateBounded<HapticCommand>(new BoundedChannelOptions(QueueCapacity)
        {
            // A backlog of stale ticks is worse than a dropped one: keep the newest.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public void Start()
    {
        _worker ??= Task.Run(() => PumpAsync(_cancellation.Token));
    }

    /// <summary>Queues a request. Returns immediately; never throws.</summary>
    public void Submit(HapticCommand command)
    {
        _queue.Writer.TryWrite(command);
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[HapticRequestWire.MessageSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", HapticRequestWire.PipeName, PipeDirection.Out);
                await pipe.ConnectAsync(ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                _log?.Invoke("Haptic request pipe connected.");

                // Drop anything queued while disconnected: those ticks are already stale.
                while (_queue.Reader.TryRead(out _)) { }

                while (!cancellationToken.IsCancellationRequested)
                {
                    var command = await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    HapticRequestWire.Write(buffer, command);
                    await pipe.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Haptic request pipe lost: {ex.GetType().Name}: {ex.Message}");

                try
                {
                    await Task.Delay(ReconnectDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                try { pipe?.Dispose(); } catch { }
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
