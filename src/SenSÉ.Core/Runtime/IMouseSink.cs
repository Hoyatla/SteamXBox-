using SenSÉ.Core.Output;

namespace SenSÉ.Core.Runtime;

public interface IMouseSink
{
    ValueTask SubmitAsync(MouseOutputFrame frame, CancellationToken cancellationToken);
}
