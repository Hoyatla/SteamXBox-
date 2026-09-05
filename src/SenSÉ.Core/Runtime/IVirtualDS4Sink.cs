using SenSÉ.Core.Output;

namespace SenSÉ.Core.Runtime;

/// <summary>
/// The virtual DualShock 4 pad a DualSense is represented by.
/// </summary>
/// <remarks>
/// <see cref="IVirtualXbox360Sink"/> and this interface share nothing but the connection lifecycle,
/// so the family-aware <see cref="VirtualPadSet"/> can hold one pad per controller without the two
/// families' reports leaking into each other.
/// </remarks>
public interface IVirtualDS4Sink : IVirtualPadSink
{
    ValueTask SubmitAsync(DS4Report report, CancellationToken cancellationToken);
}
