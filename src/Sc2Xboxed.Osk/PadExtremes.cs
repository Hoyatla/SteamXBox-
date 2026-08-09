using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Osk;

/// <summary>
/// Records how far each trackpad actually reaches, and says so once a second.
/// </summary>
/// <remarks>
/// The keyboard's zones are laid out on the assumption that a pad spans -1 to +1 in both axes: the
/// left edge of the pad is the first column of its half, the right edge the last. If the hardware
/// only ever reports, say, -0.85 to +0.85, the outer columns sit past the rim and the thumb has to
/// leave the surface to reach them — which is exactly the discomfort being chased, and it is not
/// something the mapping code can detect on its own.
///
/// <para>
/// So the question is measured rather than assumed. Sweep a thumb from one edge of a pad to the
/// other and the line below says what the extremes were. Anything short of ±1.00 is the answer.
/// </para>
///
/// <para>
/// Once a second, and only when a new extreme was reached, so a resting thumb writes nothing. The
/// per-frame logging this project already paid for in stutter is not repeated here.
/// </para>
/// </remarks>
internal static class PadExtremes
{
    private static double _leftMinX = double.MaxValue, _leftMaxX = double.MinValue;
    private static double _leftMinY = double.MaxValue, _leftMaxY = double.MinValue;
    private static double _rightMinX = double.MaxValue, _rightMaxX = double.MinValue;
    private static double _rightMinY = double.MaxValue, _rightMaxY = double.MinValue;

    private static bool _dirty;
    private static DateTime _lastReport = DateTime.MinValue;

    /// <summary>How long between two lines, at most.</summary>
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(1);

    public static void Observe(TouchpadSample left, TouchpadSample right)
    {
        // Only while a finger is down. An untouched pad reports a resting value that would drag the
        // extremes towards zero and hide the very thing being measured.
        if (left.IsTouched || left.IsPressed)
        {
            Widen(left.X, ref _leftMinX, ref _leftMaxX);
            Widen(left.Y, ref _leftMinY, ref _leftMaxY);
        }

        if (right.IsTouched || right.IsPressed)
        {
            Widen(right.X, ref _rightMinX, ref _rightMaxX);
            Widen(right.Y, ref _rightMinY, ref _rightMaxY);
        }

        if (!_dirty || DateTime.UtcNow - _lastReport < ReportEvery)
        {
            return;
        }

        _dirty = false;
        _lastReport = DateTime.UtcNow;

        Program.Log(
            $"pad extremes  left X[{Show(_leftMinX)}..{Show(_leftMaxX)}] Y[{Show(_leftMinY)}..{Show(_leftMaxY)}]"
            + $"  right X[{Show(_rightMinX)}..{Show(_rightMaxX)}] Y[{Show(_rightMinY)}..{Show(_rightMaxY)}]");
    }

    private static void Widen(double value, ref double min, ref double max)
    {
        if (value < min)
        {
            min = value;
            _dirty = true;
        }

        if (value > max)
        {
            max = value;
            _dirty = true;
        }
    }

    /// <summary>An unseen extreme reads as a dash rather than as a huge number.</summary>
    private static string Show(double value)
        => value is double.MaxValue or double.MinValue ? "  -  " : value.ToString("+0.00;-0.00");
}
