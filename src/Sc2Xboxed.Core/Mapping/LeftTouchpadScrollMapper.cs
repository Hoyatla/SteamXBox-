using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

public sealed class LeftTouchpadScrollMapper
{
    private readonly LeftTouchpadScrollSettings _settings;
    private TouchpadSample _previous;
    private double _wheelRemainder;

    public LeftTouchpadScrollMapper(LeftTouchpadScrollSettings settings)
    {
        _settings = settings;
    }

    public MouseOutputFrame Update(TouchpadSample sample)
    {
        sample = sample.Clamp();

        if (!sample.IsTouched)
        {
            _previous = TouchpadSample.Released;
            return MouseOutputFrame.Empty;
        }

        if (!_previous.IsTouched)
        {
            _previous = sample;
            return MouseOutputFrame.Empty;
        }

        var deltaY = sample.Y - _previous.Y;
        _previous = sample;

        if (Math.Abs(deltaY) < _settings.MotionDeadZone)
        {
            return MouseOutputFrame.Empty;
        }

        if (_settings.InvertVertical)
        {
            deltaY = -deltaY;
        }

        _wheelRemainder += deltaY * _settings.WheelDeltaPerPadUnit;

        var wheelDelta = (int)Math.Truncate(_wheelRemainder);
        _wheelRemainder -= wheelDelta;

        return wheelDelta == 0
            ? MouseOutputFrame.Empty
            : new MouseOutputFrame(0.0, 0.0, wheelDelta);
    }

    public void Reset()
    {
        _previous = TouchpadSample.Released;
        _wheelRemainder = 0.0;
    }
}
