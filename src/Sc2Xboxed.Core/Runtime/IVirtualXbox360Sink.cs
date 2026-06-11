using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Runtime;

public interface IVirtualXbox360Sink : IAsyncDisposable
{
    ValueTask ConnectAsync(CancellationToken cancellationToken);

    ValueTask SubmitAsync(Xbox360Report report, CancellationToken cancellationToken);
}
