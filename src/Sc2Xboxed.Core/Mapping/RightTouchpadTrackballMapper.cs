using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

public sealed class RightTouchpadTrackballMapper
{
    private readonly RightTouchpadTrackballSettings _settings;
    private TouchpadSample _previous;
    private TimeSpan? _previousTimestamp;
    private double _velocityX;
    private double _velocityY;

    public RightTouchpadTrackballMapper(RightTouchpadTrackballSettings settings)
    {
        _settings = settings;
    }

    public MouseOutputFrame Update(TimeSpan timestamp, TouchpadSample sample)
    {
        sample = sample.Clamp();
        var deltaTime = GetDeltaTime(timestamp);

        if (sample.IsTouched)
        {
            return UpdateTouched(sample, deltaTime);
        }

        _previous = TouchpadSample.Released;
        return UpdateInertia(deltaTime);
    }

    public void Reset()
    {
        _previous = TouchpadSample.Released;
        _previousTimestamp = null;
        _velocityX = 0.0;
        _velocityY = 0.0;
    }

    private MouseOutputFrame UpdateTouched(TouchpadSample sample, double deltaTime)
    {
        if (!_previous.IsTouched)
        {
            _previous = sample;
            _velocityX = 0.0;
            _velocityY = 0.0;
            return MouseOutputFrame.Empty;
        }

        var deltaX = sample.X - _previous.X;
        var deltaY = sample.Y - _previous.Y;
        _previous = sample;

        if (Math.Abs(deltaX) < _settings.MotionDeadZone)
        {
            deltaX = 0.0;
        }

        if (Math.Abs(deltaY) < _settings.MotionDeadZone)
        {
            deltaY = 0.0;
        }

        if (deltaX == 0.0 && deltaY == 0.0)
        {
            _velocityX = 0.0;
            _velocityY = 0.0;
            return MouseOutputFrame.Empty;
        }

        if (_settings.InvertX)
        {
            deltaX = -deltaX;
        }

        if (_settings.InvertY)
        {
            deltaY = -deltaY;
        }

        var pixelsX = deltaX * _settings.PixelsPerPadUnit;
        var pixelsY = -deltaY * _settings.PixelsPerPadUnit;

        _velocityX = ClampSpeed(pixelsX / deltaTime);
        _velocityY = ClampSpeed(pixelsY / deltaTime);

        return new MouseOutputFrame(pixelsX, pixelsY, 0);
    }

    private MouseOutputFrame UpdateInertia(double deltaTime)
    {
        if (Math.Abs(_velocityX) < _settings.StopSpeedPixelsPerSecond &&
            Math.Abs(_velocityY) < _settings.StopSpeedPixelsPerSecond)
        {
            _velocityX = 0.0;
            _velocityY = 0.0;
            return MouseOutputFrame.Empty;
        }

        var decay = Math.Exp(-_settings.InertiaDecayPerSecond * deltaTime);
        _velocityX *= decay;
        _velocityY *= decay;

        return new MouseOutputFrame(_velocityX * deltaTime, _velocityY * deltaTime, 0);
    }

    private double GetDeltaTime(TimeSpan timestamp)
    {
        if (_previousTimestamp is not { } previousTimestamp)
        {
            _previousTimestamp = timestamp;
            return 1.0 / 120.0;
        }

        _previousTimestamp = timestamp;

        var seconds = (timestamp - previousTimestamp).TotalSeconds;
        return Math.Clamp(seconds, 1.0 / 1000.0, 0.05);
    }

    private double ClampSpeed(double velocity)
    {
        return Math.Clamp(
            velocity,
            -_settings.MaxSpeedPixelsPerSecond,
            _settings.MaxSpeedPixelsPerSecond);
    }
}
