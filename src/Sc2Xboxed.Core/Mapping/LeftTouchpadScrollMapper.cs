using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

public sealed class LeftTouchpadScrollMapper
{
    private readonly LeftTouchpadScrollSettings _settings;
    private TouchpadSample _previous;
    private TimeSpan? _previousTimestamp;
    private double _wheelRemainder;
    private double _horizontalRemainder;
    private double _velocity;

    private readonly double[] _windowUnits;
    private readonly double[] _windowDt;
    private int _windowNext;
    private int _windowCount;

    /// <summary>Notches already emitted by the current throw, against the coast budget.</summary>
    private int _coastNotches;

    /// <summary>When the current press began, for the click settle window.</summary>
    private TimeSpan? _pressStartedAt;

    public LeftTouchpadScrollMapper(LeftTouchpadScrollSettings settings)
    {
        _settings = settings;

        var window = Math.Clamp(settings.VelocityWindowFrames, 1, 32);
        _windowUnits = new double[window];
        _windowDt = new double[window];
    }

    public MouseOutputFrame Update(TimeSpan timestamp, TouchpadSample sample)
    {
        sample = sample.Clamp();
        var deltaTime = GetDeltaTime(timestamp);

        if (!sample.IsTouched)
        {
            _previous = TouchpadSample.Released;
            return UpdateInertia(deltaTime);
        }

        if (!_previous.IsTouched)
        {
            _previous = sample;
            _velocity = 0.0;
            ClearWindow();
            return MouseOutputFrame.Empty;
        }

        // Whole-surface click zone: swallow the shift the press itself causes, then let scrolling
        // resume so a press-and-drag still works.
        if (sample.IsPressed)
        {
            _pressStartedAt ??= timestamp;

            if ((timestamp - _pressStartedAt.Value).TotalMilliseconds < _settings.ClickSettleMilliseconds)
            {
                _previous = sample;
                _velocity = 0.0;
                ClearWindow();
                return MouseOutputFrame.Empty;
            }
        }
        else
        {
            _pressStartedAt = null;
        }

        var deltaY = sample.Y - _previous.Y;
        var deltaX = sample.X - _previous.X;
        _previous = sample;

        var horizontal = _settings.HorizontalEnabled ? deltaX : 0.0;

        if (Math.Abs(deltaY) < _settings.MotionDeadZone &&
            Math.Abs(horizontal) < _settings.MotionDeadZone)
        {
            // Resting the finger cancels the throw, the same way it does on a phone.
            _velocity = 0.0;
            ClearWindow();
            return MouseOutputFrame.Empty;
        }

        if (_settings.InvertVertical)
        {
            deltaY = -deltaY;
        }

        if (_settings.InvertHorizontal)
        {
            horizontal = -horizontal;
        }

        var gain = AccelerationGain(Math.Sqrt(deltaY * deltaY + horizontal * horizontal), deltaTime);

        var units = deltaY * _settings.WheelDeltaPerPadUnit * gain;
        var horizontalUnits = horizontal * _settings.WheelDeltaPerPadUnit * gain;

        PushWindow(units, deltaTime);
        _velocity = WindowVelocity();
        _coastNotches = 0;

        var frame = Emit(units);
        var horizontalDelta = EmitHorizontal(horizontalUnits);

        return horizontalDelta == 0
            ? frame
            : new MouseOutputFrame(frame.DeltaX, frame.DeltaY, frame.WheelDelta, horizontalDelta);
    }

    /// <summary>Keeps scrolling after the finger lifts, decaying to a stop.</summary>
    private MouseOutputFrame UpdateInertia(double deltaTime)
    {
        var remaining = _settings.MaxCoastNotches - _coastNotches;

        if (Math.Abs(_velocity) < _settings.StopSpeedUnitsPerSecond || remaining <= 0)
        {
            _velocity = 0.0;
            _wheelRemainder = 0.0;
            return MouseOutputFrame.Empty;
        }

        var decay = Math.Exp(-_settings.InertiaDecayPerSecond * deltaTime);
        _velocity *= decay;

        var frame = Emit(_velocity * deltaTime);

        // Checking the budget before emitting is not enough: at a high sensitivity a single frame can
        // produce dozens of notches at once and sail past the cap. Trim the frame to what is left.
        if (Math.Abs(frame.WheelDelta) > remaining)
        {
            frame = new MouseOutputFrame(0.0, 0.0, Math.Sign(frame.WheelDelta) * remaining);
            _wheelRemainder = 0.0;
        }

        _coastNotches += Math.Abs(frame.WheelDelta);
        return frame;
    }

    private void PushWindow(double units, double deltaTime)
    {
        _windowUnits[_windowNext] = units;
        _windowDt[_windowNext] = deltaTime;
        _windowNext = (_windowNext + 1) % _windowUnits.Length;

        if (_windowCount < _windowUnits.Length)
        {
            _windowCount++;
        }
    }

    /// <summary>
    /// Speed-dependent gain, mirroring the trackball's curve so both pads behave consistently.
    /// Returns exactly 1 at an exponent of 1, so it is inert unless enabled.
    /// </summary>
    private double AccelerationGain(double magnitude, double deltaTime)
    {
        if (Math.Abs(_settings.AccelerationExponent - 1.0) < 0.0001)
        {
            return 1.0;
        }

        var speed = magnitude / Math.Max(0.0001, deltaTime);
        var normalized = speed / Math.Max(0.0001, _settings.AccelerationReferenceSpeed);
        if (normalized <= 0.0)
        {
            return _settings.MinAccelerationGain;
        }

        return Math.Clamp(
            Math.Pow(normalized, _settings.AccelerationExponent - 1.0),
            _settings.MinAccelerationGain,
            _settings.MaxAccelerationGain);
    }

    /// <summary>Accumulates fractional horizontal units and emits whole notches.</summary>
    private int EmitHorizontal(double units)
    {
        if (units == 0.0)
        {
            return 0;
        }

        _horizontalRemainder += units;
        var delta = (int)Math.Truncate(_horizontalRemainder);
        _horizontalRemainder -= delta;
        return delta;
    }

    /// <summary>Total scrolled units over total elapsed time across the window.</summary>
    private double WindowVelocity()
    {
        double sumUnits = 0, sumTime = 0, travel = 0;
        for (int i = 0; i < _windowCount; i++)
        {
            sumUnits += _windowUnits[i];
            sumTime += _windowDt[i];
            travel += Math.Abs(_windowUnits[i]);
        }

        if (sumTime <= 0.0)
        {
            return 0.0;
        }

        // Too short to be a flick: place the content, do not launch it.
        if (travel < _settings.MinThrowTravelUnits)
        {
            return 0.0;
        }

        // Reject a gesture that doubled back on itself. Lifting a finger drags it backwards slightly,
        // and those reversed frames are what made the page coast the wrong way after a scroll.
        var coherence = travel > 0.0 ? Math.Abs(sumUnits) / travel : 0.0;
        if (coherence < _settings.MinThrowDirectionCoherence)
        {
            return 0.0;
        }

        return Math.Clamp(
            sumUnits / sumTime,
            -_settings.MaxSpeedUnitsPerSecond,
            _settings.MaxSpeedUnitsPerSecond);
    }

    private void ClearWindow()
    {
        Array.Clear(_windowUnits);
        Array.Clear(_windowDt);
        _windowNext = 0;
        _windowCount = 0;
        _coastNotches = 0;
        _horizontalRemainder = 0.0;
    }

    /// <summary>
    /// Accumulates fractional wheel units and emits whole notches, so slow scrolling still moves
    /// instead of being truncated away frame after frame.
    /// </summary>
    private MouseOutputFrame Emit(double units)
    {
        _wheelRemainder += units;

        var wheelDelta = (int)Math.Truncate(_wheelRemainder);
        _wheelRemainder -= wheelDelta;

        return wheelDelta == 0
            ? MouseOutputFrame.Empty
            : new MouseOutputFrame(0.0, 0.0, wheelDelta);
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

    public void Reset()
    {
        _previous = TouchpadSample.Released;
        _previousTimestamp = null;
        _wheelRemainder = 0.0;
        _velocity = 0.0;
        ClearWindow();
    }
}
