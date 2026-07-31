using System.Globalization;

namespace Sc2Xboxed.Core.Diagnostics;

/// <summary>
/// Accumulates what the pipeline actually did, summarised once per second.
/// </summary>
/// <remarks>
/// Replaces per-frame logging as the default way to see behaviour. One line per second answers
/// "did the pads emit anything, how much, and did haptics get through" without megabytes of noise.
/// </remarks>
public sealed class RuntimeCounters
{
    private int _frames;
    private int _mouseEvents;
    private double _mousePixelsX;
    private double _mousePixelsY;
    private int _wheelNotches;
    private int _hapticsSubmitted;
    private int _hapticsDropped;
    private int _overlayHapticRequests;
    private int _padClicks;
    private int _rightPadTouchFrames;
    private int _leftPadTouchFrames;

    public void Frame(bool rightTouched, bool leftTouched)
    {
        _frames++;
        if (rightTouched) _rightPadTouchFrames++;
        if (leftTouched) _leftPadTouchFrames++;
    }

    public void MouseMotion(double pixelsX, double pixelsY)
    {
        _mouseEvents++;
        _mousePixelsX += pixelsX;
        _mousePixelsY += pixelsY;
    }

    public void Wheel(int notches) => _wheelNotches += Math.Abs(notches);

    public void PadClick() => _padClicks++;

    public void HapticSubmitted() => _hapticsSubmitted++;

    public void HapticDropped() => _hapticsDropped++;

    public void OverlayHapticRequest() => _overlayHapticRequests++;

    public bool HasActivity =>
        _mouseEvents > 0 || _wheelNotches > 0 || _padClicks > 0 ||
        _hapticsSubmitted > 0 || _hapticsDropped > 0 || _overlayHapticRequests > 0;

    /// <summary>Formats the interval summary and resets the counters.</summary>
    public string DrainToLine(TimeSpan elapsed, string mode, string owner)
    {
        var seconds = Math.Max(0.001, elapsed.TotalSeconds);
        var fps = _frames / seconds;

        var line = string.Format(
            CultureInfo.InvariantCulture,
            "mode={0} owner={1} fps={2:0} frames={3} | pad touch r/l={4}/{5} clicks={6} " +
            "| mouse events={7} dx={8:0.#}px dy={9:0.#}px | wheel={10} notches " +
            "| haptics sent={11} dropped={12} overlayReq={13}",
            mode, owner, fps, _frames,
            _rightPadTouchFrames, _leftPadTouchFrames, _padClicks,
            _mouseEvents, _mousePixelsX, _mousePixelsY, _wheelNotches,
            _hapticsSubmitted, _hapticsDropped, _overlayHapticRequests);

        Reset();
        return line;
    }

    private void Reset()
    {
        _frames = 0;
        _mouseEvents = 0;
        _mousePixelsX = 0;
        _mousePixelsY = 0;
        _wheelNotches = 0;
        _hapticsSubmitted = 0;
        _hapticsDropped = 0;
        _overlayHapticRequests = 0;
        _padClicks = 0;
        _rightPadTouchFrames = 0;
        _leftPadTouchFrames = 0;
    }
}
