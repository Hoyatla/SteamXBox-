using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Runtime;

public interface IVirtualXbox360Sink : IVirtualPadSink
{
    ValueTask SubmitAsync(Xbox360Report report, CancellationToken cancellationToken);
}
