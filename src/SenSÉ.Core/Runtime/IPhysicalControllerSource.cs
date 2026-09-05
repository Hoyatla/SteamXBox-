using SenSÉ.Core.Input;

namespace SenSÉ.Core.Runtime;

public interface IPhysicalControllerSource : IAsyncDisposable
{
    IAsyncEnumerable<ControllerState> ReadFramesAsync(CancellationToken cancellationToken);
}
