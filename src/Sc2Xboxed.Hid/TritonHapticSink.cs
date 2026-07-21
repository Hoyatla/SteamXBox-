using HidSharp;
using Sc2Xboxed.Core.Haptics;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Hid;

public sealed class TritonHapticSink : IHapticSink
{
    private readonly SteamHidDiscovery _discovery;
    private readonly TritonHapticReportBuilder _reportBuilder;
    private HidStream? _stream;
    private int _outputReportLength = 65;

    public TritonHapticSink()
        : this(new SteamHidDiscovery(), new TritonHapticReportBuilder())
    {
    }

    public TritonHapticSink(SteamHidDiscovery discovery, TritonHapticReportBuilder reportBuilder)
    {
        _discovery = discovery;
        _reportBuilder = reportBuilder;
    }

    public ValueTask SubmitAsync(HapticOutputFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        foreach (var command in frame.Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stream!.Write(_reportBuilder.Build(command, _outputReportLength));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _stream = null;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (_stream is not null)
        {
            return;
        }

        var device = _discovery.FindPreferredControllerDevice()
            ?? throw new InvalidOperationException("No known Valve Steam Controller HID interface was found.");

        if (!device.TryOpen(out _stream))
        {
            throw new IOException($"Unable to open HID device {device.DevicePath}.");
        }

        _outputReportLength = Math.Max(7, device.GetMaxOutputReportLength());
        _stream.WriteTimeout = 250;
    }
}
