namespace Sc2Xboxed.Core.Osk;

/// <summary>
/// Smooths an overlay cursor position, filtering hard when the finger is nearly still and barely at
/// all when it moves.
/// </summary>
/// <remarks>
/// The previous filter was a fixed-coefficient exponential average, which cannot win: a coefficient
/// low enough to kill the tremble on a resting finger also makes a real movement lag behind it, and
/// one high enough to feel immediate passes the tremble straight through. The capacitive pad reports
/// a contact patch whose centroid wanders by a pixel or two while the finger is still, so a resting
/// cursor never stops moving.
///
/// This is the one-euro filter: the cutoff frequency rises with speed, so the smoothing adapts. A
/// resting finger is cut hard and the cursor sits still; a deliberate movement is passed almost
/// untouched and stays responsive.
///
/// A dead band sits in front of it. Below <see cref="DeadBandPixels"/> of travel the output does not
/// move at all, which stops the last fraction of a pixel of dithering — the part that reads as
/// vibration rather than as motion.
/// </remarks>
public sealed class CursorFilter
{
    /// <summary>Travel below which the cursor is held completely still, in pixels.</summary>
    private const double DeadBandPixels = 1.4;

    /// <summary>Cutoff for a motionless finger, in Hz. Lower filters harder.</summary>
    private const double MinCutoff = 1.0;

    /// <summary>How fast the cutoff opens up with speed. Higher reacts sooner.</summary>
    private const double Beta = 0.012;

    /// <summary>Cutoff of the filter applied to the speed estimate itself, in Hz.</summary>
    private const double DerivativeCutoff = 1.0;

    /// <summary>Assumed frame interval when the real one is unusable, in seconds.</summary>
    private const double FallbackDeltaSeconds = 1.0 / 125.0;

    private readonly double _smoothingScale;

    private bool _has;
    private int _lastTick;
    private double _x, _y;
    private double _dx, _dy;
    private double _rawX, _rawY;

    /// <param name="smoothing">
    /// The user's cursor smoothing setting, 0 to 1. It scales how hard a resting finger is filtered;
    /// it does not change the behaviour under real movement, which stays responsive either way.
    /// </param>
    public CursorFilter(double smoothing)
    {
        // A high setting means "smooth a lot", which means a lower cutoff.
        var clamped = Math.Clamp(smoothing, 0.05, 1.0);
        _smoothingScale = 1.0 / clamped;
    }

    public double X => _x;
    public double Y => _y;

    /// <summary>Forgets the current position, so the next sample is taken as-is.</summary>
    public void Reset() => _has = false;

    public void Update(double rawX, double rawY)
    {
        int tick = Environment.TickCount;

        if (!_has)
        {
            _x = _rawX = rawX;
            _y = _rawY = rawY;
            _dx = _dy = 0;
            _lastTick = tick;
            _has = true;
            return;
        }

        // TickCount wraps every 49 days; the subtraction handles that, the clamp handles a stalled
        // or duplicated frame.
        var elapsed = (tick - _lastTick) / 1000.0;
        if (elapsed <= 0 || elapsed > 0.25)
        {
            elapsed = FallbackDeltaSeconds;
        }
        _lastTick = tick;

        // Dead band on the raw input: below this the finger is resting, not moving.
        var travel = Math.Sqrt(((rawX - _rawX) * (rawX - _rawX)) + ((rawY - _rawY) * (rawY - _rawY)));
        if (travel < DeadBandPixels)
        {
            rawX = _rawX;
            rawY = _rawY;
        }
        else
        {
            _rawX = rawX;
            _rawY = rawY;
        }

        // Speed, itself filtered: an unfiltered derivative is dominated by the very noise we are
        // trying to remove, and would open the cutoff on a resting finger.
        var dAlpha = Alpha(DerivativeCutoff, elapsed);
        _dx += dAlpha * (((rawX - _x) / elapsed) - _dx);
        _dy += dAlpha * (((rawY - _y) / elapsed) - _dy);
        var speed = Math.Sqrt((_dx * _dx) + (_dy * _dy));

        var cutoff = (MinCutoff / _smoothingScale) + (Beta * speed);
        var alpha = Alpha(cutoff, elapsed);

        _x += alpha * (rawX - _x);
        _y += alpha * (rawY - _y);
    }

    private static double Alpha(double cutoff, double elapsed)
    {
        var tau = 1.0 / (2.0 * Math.PI * cutoff);
        return 1.0 / (1.0 + (tau / elapsed));
    }
}
