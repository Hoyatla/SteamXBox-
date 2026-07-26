using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Mapping;

public sealed class SmoothedTouchpadInput
{
	private TouchpadSample _smoothed;
	private bool _lastRawTouched;
	private bool _lastRawPressed;
	private int _touchedFrames;
	private int _pressedFrames;
	private int _releasedFrames;

	private const double PositionAlpha = 0.5;
	private const int DebounceFrames = 2;

	public TouchpadSample Update(TouchpadSample raw)
	{
		double sx = _smoothed.X;
		double sy = _smoothed.Y;

		if (raw.IsTouched)
		{
			sx = PositionAlpha * raw.X + (1.0 - PositionAlpha) * _smoothed.X;
			sy = PositionAlpha * raw.Y + (1.0 - PositionAlpha) * _smoothed.Y;
		}

		if (raw.IsTouched == _lastRawTouched)
			_touchedFrames++;
		else
			_touchedFrames = 0;

		if (raw.IsPressed == _lastRawPressed)
			_pressedFrames++;
		else
			_pressedFrames = 0;

		bool touched;
		if (_touchedFrames >= DebounceFrames)
			touched = raw.IsTouched;
		else
			touched = _smoothed.IsTouched;

		bool pressed;
		if (_pressedFrames >= DebounceFrames)
			pressed = raw.IsPressed;
		else
			pressed = _smoothed.IsPressed;

		if (_smoothed.IsTouched && !touched)
		{
			_releasedFrames++;
			if (_releasedFrames < DebounceFrames)
				touched = true;
		}
		else
		{
			_releasedFrames = 0;
		}

		_lastRawTouched = raw.IsTouched;
		_lastRawPressed = raw.IsPressed;
		_smoothed = new TouchpadSample(touched, sx, sy, raw.Pressure, pressed);
		return _smoothed;
	}

	public void Reset()
	{
		_smoothed = TouchpadSample.Released;
		_lastRawTouched = false;
		_lastRawPressed = false;
		_touchedFrames = 0;
		_pressedFrames = 0;
		_releasedFrames = 0;
	}
}
