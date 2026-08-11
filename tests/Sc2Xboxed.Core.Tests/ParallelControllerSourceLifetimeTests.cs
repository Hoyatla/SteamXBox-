using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Runtime;
using Sc2Xboxed.Windows;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// That letting go of a controller reader actually stops it.
/// </summary>
/// <remarks>
/// <b>The bug these were written for.</b> Everything inside the reader ran on the token its caller
/// passed, which is the session's. So a reader the caller had finished with kept its arrivals
/// watcher alive until the whole session ended — rescanning every three seconds, opening devices,
/// and announcing controllers beside its own replacement.
///
/// <para>
/// The loop that waits for a pad to come back builds a new reader each time round and released none
/// of them. Two naps meant three watchers on three independent beats, which is why the log showed
/// the same controller arriving twice eight tenths of a second apart — from a loop that waits three
/// seconds and therefore cannot do that on its own.
/// </para>
///
/// <para>
/// It is tested here rather than watched for in a log because it is invisible until it is measured:
/// nothing crashes, nothing is lost, the machine simply does the same work several times over and
/// says so in a file nobody reads to the end.
/// </para>
/// </remarks>
public class ParallelControllerSourceLifetimeTests
{
    /// <summary>A controller that never sends anything and never ends by itself.</summary>
    private sealed class SilentSource : IPhysicalControllerSource
    {
        public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A controller that sends one frame and then goes quiet for ever.
    /// </summary>
    /// <remarks>
    /// Needed so that an enumeration can be entered and left. With a source that never sends
    /// anything, <c>await foreach</c> waits on the first item and the <c>break</c> below is never
    /// reached — which is how the first version of the ordering test hung instead of failing.
    /// </remarks>
    private sealed class OneFrameSource : IPhysicalControllerSource
    {
        public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ControllerState();

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (ControllerIdentity, IPhysicalControllerSource) Pad(string id)
        => (new ControllerIdentity(ControllerKind.XInput, id, id, Slot: 0), new SilentSource());

    private static (ControllerIdentity, IPhysicalControllerSource) Talking(string id)
        => (new ControllerIdentity(ControllerKind.XInput, id, id, Slot: 0), new OneFrameSource());

    // The heart of it: after DisposeAsync, the rescan must stop being called.
    [Fact]
    public async Task DisposingStopsTheArrivalsWatcher()
    {
        var rescans = 0;

        var source = new ParallelControllerSource(
            [Pad("one")],
            rescan: () =>
            {
                Interlocked.Increment(ref rescans);
                return [Pad("one")];
            },
            rescanInterval: TimeSpan.FromMilliseconds(40));

        using var session = new CancellationTokenSource();

        // Drained on a task of its own: the stream never ends by itself, which is the point.
        var reading = Task.Run(async () =>
        {
            await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
            }
        });

        await Task.Delay(300);
        var whileAlive = Volatile.Read(ref rescans);

        await source.DisposeAsync();

        // Long enough for several more beats, had anything still been beating.
        await Task.Delay(300);
        var afterDisposal = Volatile.Read(ref rescans);

        session.Cancel();

        Assert.True(whileAlive > 0, "the watcher never ran, so this proves nothing");
        Assert.True(
            afterDisposal - whileAlive <= 1,
            $"the watcher kept rescanning after disposal: {whileAlive} before, {afterDisposal} after");

        await Task.WhenAny(reading, Task.Delay(2000));
    }

    /// <summary>
    /// The order the product actually uses: stop reading first, dispose second.
    /// </summary>
    /// <remarks>
    /// <b>The test above passes without catching this.</b> It disposes while the enumeration is
    /// still running, so the link between the caller's life and this source's is still alive to
    /// carry the cancellation. The product does the opposite: it breaks out of the frame loop — the
    /// iterator ends, and with it anything the iterator owned — and only then releases the source.
    ///
    /// <para>
    /// A linked cancellation source that has been disposed forwards nothing. So the first fix put
    /// the link in a <c>using</c> inside the enumerator, the enumerator disposed it on the way out,
    /// and cancelling afterwards reached nobody. Measured in the product: SteamXBox handed the
    /// controller to Steam and released it from HidHide, and six tenths of a second later the
    /// orphaned watcher hid it again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StoppingTheEnumerationThenDisposingAlsoStopsTheWatcher()
    {
        var rescans = 0;

        var source = new ParallelControllerSource(
            [Talking("one")],
            rescan: () =>
            {
                Interlocked.Increment(ref rescans);
                return [Pad("one")];
            },
            rescanInterval: TimeSpan.FromMilliseconds(40));

        using var session = new CancellationTokenSource();

        // Entered and left, exactly as the frame loop leaves it: one frame in, then break.
        await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
        {
            break;
        }

        await Task.Delay(300);
        var whileAlive = Volatile.Read(ref rescans);

        await source.DisposeAsync();

        await Task.Delay(300);
        var afterDisposal = Volatile.Read(ref rescans);

        Assert.True(whileAlive > 0, "the watcher never ran, so this proves nothing");
        Assert.True(
            afterDisposal - whileAlive <= 1,
            $"the watcher kept rescanning after the enumeration ended and the source was disposed: "
            + $"{whileAlive} before, {afterDisposal} after");
    }

    // The caller's token must still work on its own: disposal is an addition, not a replacement.
    [Fact]
    public async Task CancellingTheCallerStopsTheWatcherToo()
    {
        var rescans = 0;

        await using var source = new ParallelControllerSource(
            [Pad("one")],
            rescan: () =>
            {
                Interlocked.Increment(ref rescans);
                return [Pad("one")];
            },
            rescanInterval: TimeSpan.FromMilliseconds(40));

        using var session = new CancellationTokenSource();

        var reading = Task.Run(async () =>
        {
            await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
            }
        });

        await Task.Delay(300);
        var whileAlive = Volatile.Read(ref rescans);

        session.Cancel();
        await Task.Delay(300);

        Assert.True(whileAlive > 0, "the watcher never ran, so this proves nothing");
        Assert.True(
            Volatile.Read(ref rescans) - whileAlive <= 1,
            "the watcher kept rescanning after the caller cancelled");

        await Task.WhenAny(reading, Task.Delay(2000));
    }
}
