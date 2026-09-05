using HidSharp;

namespace SenSÉ.Hid;

public sealed class SteamControllerLizardModeHeartbeat : IAsyncDisposable
{
    private readonly HidStream _stream;
    private readonly object _streamGate;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public SteamControllerLizardModeHeartbeat(HidStream stream, object streamGate)
    {
        _stream = stream;
        _streamGate = streamGate;
        _worker = Task.Run(RunAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800), _cancellation.Token)
                .ConfigureAwait(false);

            // Everything the disable changed, said again — see SteamControllerLizardMode.Beat. The
            // beat used to repeat one of the two commands, so a session that died left the
            // controller half restored: buttons back, trackpads still dead.
            SteamControllerLizardMode.Beat(_stream, _streamGate);
        }
    }
}
