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

    // Rolling window of recent motion, used to measure the throw velocity.
    private readonly double[] _windowDx;
    private readonly double[] _windowDy;
    private readonly double[] _windowDt;
    private int _windowNext;
    private int _windowCount;

    /// <summary>Consecutive frames whose motion stayed under the dead zone.</summary>
    private int _quietFrames;

    /// <summary>Travel accumulated since the current contact began, for intent detection.</summary>
    private double _contactTravel;
    private bool _contactActivated;

    /// <summary>Pixels covered by the frames currently in the velocity window.</summary>
    private double _windowTravel;

    /// <summary>When the current press began, for the click settle window.</summary>
    private TimeSpan? _pressStartedAt;

    public RightTouchpadTrackballMapper(RightTouchpadTrackballSettings settings)
    {
        _settings = settings;

        var window = Math.Clamp(settings.VelocityWindowFrames, 1, 32);
        _windowDx = new double[window];
        _windowDy = new double[window];
        _windowDt = new double[window];
    }

    public MouseOutputFrame Update(TimeSpan timestamp, TouchpadSample sample)
    {
        sample = sample.Clamp();
        var deltaTime = GetDeltaTime(timestamp);

        if (sample.IsTouched)
        {
            // Whole-surface click zone, but only the onset is frozen: the press settles without
            // dragging the pointer, then movement resumes so click-and-drag still works.
            if (sample.IsPressed)
            {
                _pressStartedAt ??= timestamp;

                if ((timestamp - _pressStartedAt.Value).TotalMilliseconds < _settings.ClickSettleMilliseconds)
                {
                    UpdateTouched(sample, deltaTime);
                    _velocityX = 0.0;
                    _velocityY = 0.0;
                    ClearWindow();
                    return MouseOutputFrame.Empty;
                }
            }
            else
            {
                _pressStartedAt = null;
            }

            var frame = UpdateTouched(sample, deltaTime);
            var edge = EdgeContinuation(sample, deltaTime);
            return edge.HasMouseMotion ? frame.Add(edge) : frame;
        }

        _previous = TouchpadSample.Released;
        return UpdateInertia(deltaTime);
    }

    /// <summary>
    /// Extra movement while the finger rests near the pad edge, in the direction it is pushing.
    /// Independent of the delta path, so it works even when the finger is perfectly still.
    /// </summary>
    private MouseOutputFrame EdgeContinuation(TouchpadSample sample, double deltaTime)
    {
        if (_settings.EdgeSpeedPixelsPerSecond <= 0.0)
        {
            return MouseOutputFrame.Empty;
        }

        var radius = Math.Sqrt(sample.X * sample.X + sample.Y * sample.Y);
        if (radius < _settings.EdgeThreshold)
        {
            return MouseOutputFrame.Empty;
        }

        // Ramp in over the remaining travel so crossing the threshold is not a step change.
        var span = Math.Max(0.001, 1.0 - _settings.EdgeThreshold);
        var ramp = Math.Clamp((radius - _settings.EdgeThreshold) / span, 0.0, 1.0);
        var speed = _settings.EdgeSpeedPixelsPerSecond * ramp * deltaTime;

        var dirX = sample.X / Math.Max(0.0001, radius);
        var dirY = sample.Y / Math.Max(0.0001, radius);

        if (_settings.InvertX) dirX = -dirX;
        if (_settings.InvertY) dirY = -dirY;

        // Same Y convention as the delta path: pad Y grows downwards, and the extra negation plus the
        // InvertY flag is what makes finger-up move the cursor up.
        return new MouseOutputFrame(dirX * speed, -dirY * speed, 0);
    }

    public void Reset()
    {
        _previous = TouchpadSample.Released;
        _previousTimestamp = null;
        _velocityX = 0.0;
        _velocityY = 0.0;
        ClearWindow();
    }

    private MouseOutputFrame UpdateTouched(TouchpadSample sample, double deltaTime)
    {
        if (!_previous.IsTouched)
        {
            _previous = sample;
            _velocityX = 0.0;
            _velocityY = 0.0;
            _contactTravel = 0.0;
            _contactActivated = false;
            ClearWindow();
            return MouseOutputFrame.Empty;
        }

        var deltaX = sample.X - _previous.X;
        var deltaY = sample.Y - _previous.Y;
        _previous = sample;

        var magnitude = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        // Travel since this contact began drives both the brush filter and the precision ramp.
        _contactTravel += magnitude;

        // Involuntary contact rejection: a fresh touch must prove intent before it moves anything.
        // A brush or a resting finger registers as a touch and used to nudge the pointer.
        if (!_contactActivated)
        {
            if (_contactTravel < _settings.TouchActivationTravel)
            {
                return MouseOutputFrame.Empty;
            }

            _contactActivated = true;
        }

        // Gate on the 2D magnitude. An independent per-axis dead zone dropped each axis out
        // separately, which turned slow diagonal drags into stair steps.
        if (magnitude < _settings.MotionDeadZone)
        {
            // A brief pause mid-gesture must not cancel a flick, but a finger genuinely resting on the
            // pad must, otherwise lifting off would throw the cursor using a stale velocity.
            _quietFrames++;
            if (_quietFrames >= _settings.QuietFramesToCancelThrow)
            {
                _velocityX = 0.0;
                _velocityY = 0.0;
                ClearWindow();
            }

            return MouseOutputFrame.Empty;
        }

        _quietFrames = 0;

        if (_settings.InvertX)
        {
            deltaX = -deltaX;
        }

        if (_settings.InvertY)
        {
            deltaY = -deltaY;
        }

        // Distance-based precision wins over the speed curve near the start of a gesture: a small
        // correction stays fine-grained no matter how briskly it was made.
        var gain = ApplyPrecisionRamp(AccelerationGain(magnitude, deltaTime));

        var pixelsX = deltaX * _settings.PixelsPerPadUnit * gain;
        var pixelsY = -deltaY * _settings.PixelsPerPadUnit * gain;

        PushWindow(pixelsX, pixelsY, deltaTime);
        (_velocityX, _velocityY) = WindowVelocity();

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

    /// <summary>
    /// Speed-dependent gain. With <see cref="RightTouchpadTrackballSettings.AccelerationExponent"/>
    /// at 1 this returns exactly 1, so the curve is inert until deliberately enabled.
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

        var gain = Math.Pow(normalized, _settings.AccelerationExponent - 1.0);
        return Math.Clamp(gain, _settings.MinAccelerationGain, _settings.MaxAccelerationGain);
    }

    /// <summary>
    /// Blends the gain from <see cref="RightTouchpadTrackballSettings.MinAccelerationGain"/> up to the
    /// speed-derived gain as the gesture covers ground.
    /// </summary>
    /// <remarks>
    /// At the very start of a contact the gain is the fine value outright, whatever the speed curve
    /// says. That is the whole point: a two-millimetre correction has to stay a two-millimetre
    /// correction even when it is made quickly.
    /// </remarks>
    private double ApplyPrecisionRamp(double speedGain)
    {
        if (_settings.FinePrecisionTravel <= 0.0)
        {
            return speedGain;
        }

        var ratio = Math.Clamp(_contactTravel / _settings.FinePrecisionTravel, 0.0, 1.0);
        return _settings.MinAccelerationGain + (speedGain - _settings.MinAccelerationGain) * ratio;
    }

    private void PushWindow(double pixelsX, double pixelsY, double deltaTime)
    {
        _windowDx[_windowNext] = pixelsX;
        _windowDy[_windowNext] = pixelsY;
        _windowDt[_windowNext] = deltaTime;
        _windowNext = (_windowNext + 1) % _windowDx.Length;

        if (_windowCount < _windowDx.Length)
        {
            _windowCount++;
        }
    }

    /// <summary>
    /// Total displacement over total elapsed time across the window.
    /// </summary>
    /// <remarks>
    /// Measuring the gesture rather than smoothing it: a single frame's delta is too noisy at HID
    /// report rate, but an exponential average lags so far behind a short flick that the throw comes
    /// out far weaker than the movement that produced it.
    /// </remarks>
    private (double X, double Y) WindowVelocity()
    {
        double sumX = 0, sumY = 0, sumT = 0, travel = 0;
        for (int i = 0; i < _windowCount; i++)
        {
            sumX += _windowDx[i];
            sumY += _windowDy[i];
            sumT += _windowDt[i];
            travel += Math.Sqrt(_windowDx[i] * _windowDx[i] + _windowDy[i] * _windowDy[i]);
        }

        _windowTravel = travel;

        if (sumT <= 0.0)
        {
            return (0.0, 0.0);
        }

        // A short gesture must never throw, however briskly it was made. Velocity is distance over
        // time, so a two-pixel nudge completed in one frame reads as fast and used to launch a long
        // glide right when the user was placing the cursor precisely.
        if (travel < _settings.MinThrowTravelPixels)
        {
            return (0.0, 0.0);
        }

        return (ClampSpeed(sumX / sumT), ClampSpeed(sumY / sumT));
    }

    private void ClearWindow()
    {
        Array.Clear(_windowDx);
        Array.Clear(_windowDy);
        Array.Clear(_windowDt);
        _windowNext = 0;
        _windowCount = 0;
        _quietFrames = 0;
        _windowTravel = 0.0;
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
