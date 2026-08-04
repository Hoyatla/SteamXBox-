using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Runtime;

public interface IPhysicalControllerSource : IAsyncDisposable
{
    IAsyncEnumerable<ControllerState> ReadFramesAsync(CancellationToken cancellationToken);
}
