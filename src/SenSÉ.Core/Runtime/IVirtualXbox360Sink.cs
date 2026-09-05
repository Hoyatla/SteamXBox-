using SenSÉ.Core.Output;

namespace SenSÉ.Core.Runtime;

public interface IVirtualXbox360Sink : IVirtualPadSink
{
    ValueTask SubmitAsync(Xbox360Report report, CancellationToken cancellationToken);
}
