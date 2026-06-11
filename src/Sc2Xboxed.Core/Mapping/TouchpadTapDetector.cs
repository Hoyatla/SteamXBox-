using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Mapping;

public sealed class TouchpadTapDetector
{
    private readonly TouchpadTapSettings _settings;
    private bool _isTracking;
    private bool _cancelled;
    private TimeSpan _startTime;
    private double _startX;
    private double _startY;
    private double _lastX;
    private double _lastY;

    public TouchpadTapDetector(TouchpadTapSettings settings)
    {
        _settings = settings;
    }

    public TouchpadTap Update(TimeSpan timestamp, TouchpadSample sample)
    {
        sample = sample.Clamp();

        if (!sample.IsTouched)
        {
            return Release(timestamp);
        }

        if (!_isTracking)
        {
            Start(timestamp, sample);
            return TouchpadTap.None;
        }

        _lastX = sample.X;
        _lastY = sample.Y;

        var travel = Distance(_startX, _startY, sample.X, sample.Y);
        if (travel > _settings.MaxTravel || sample.Pressure < _settings.MinPressure)
        {
            _cancelled = true;
        }

        return TouchpadTap.None;
    }

    public void Reset()
    {
        _isTracking = false;
        _cancelled = false;
        _startTime = TimeSpan.Zero;
        _startX = 0.0;
        _startY = 0.0;
        _lastX = 0.0;
        _lastY = 0.0;
    }

    private void Start(TimeSpan timestamp, TouchpadSample sample)
    {
        _isTracking = true;
        _cancelled = sample.Pressure < _settings.MinPressure;
        _startTime = timestamp;
        _startX = sample.X;
        _startY = sample.Y;
        _lastX = sample.X;
        _lastY = sample.Y;
    }

    private TouchpadTap Release(TimeSpan timestamp)
    {
        if (!_isTracking)
        {
            return TouchpadTap.None;
        }

        var duration = timestamp - _startTime;
        var wasTapped = !_cancelled && duration <= _settings.MaxTapDuration;
        var tap = wasTapped
            ? new TouchpadTap(true, _lastX, _lastY, timestamp)
            : TouchpadTap.None;

        Reset();
        return tap;
    }

    private static double Distance(double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
