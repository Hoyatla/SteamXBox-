using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Runtime;

public interface IMouseSink
{
    ValueTask SubmitAsync(MouseOutputFrame frame, CancellationToken cancellationToken);
}
