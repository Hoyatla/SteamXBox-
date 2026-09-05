using SenSÉ.Core.Haptics;

namespace SenSÉ.Core.Runtime;

public interface IHapticSink : IAsyncDisposable
{
    ValueTask SubmitAsync(HapticOutputFrame frame, CancellationToken cancellationToken);
}
