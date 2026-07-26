using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Hid;

namespace Sc2Xboxed.Osk;

public sealed class HapticFeedback : IAsyncDisposable
{
    private readonly TritonHapticSink _sink = new();

    public async ValueTask PulseRightAsync()
    {
        try
        {
            await _sink.SubmitAsync(
                new HapticOutputFrame(new[] { HapticCommand.Tick(HapticActuator.RightTrackpad) }),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    public async ValueTask PulseLeftAsync()
    {
        try
        {
            await _sink.SubmitAsync(
                new HapticOutputFrame(new[] { HapticCommand.Tick(HapticActuator.LeftTrackpad) }),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync().ConfigureAwait(false);
    }
}
