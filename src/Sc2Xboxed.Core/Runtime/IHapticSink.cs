using Sc2Xboxed.Core.Haptics;

namespace Sc2Xboxed.Core.Runtime;

public interface IHapticSink : IAsyncDisposable
{
    ValueTask SubmitAsync(HapticOutputFrame frame, CancellationToken cancellationToken);
}
