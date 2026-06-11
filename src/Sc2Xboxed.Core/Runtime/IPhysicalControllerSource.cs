using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Runtime;

public interface IPhysicalControllerSource : IAsyncDisposable
{
    IAsyncEnumerable<SteamControllerState> ReadFramesAsync(CancellationToken cancellationToken);
}
