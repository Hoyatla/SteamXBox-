using Sc2Xboxed.Core.Osk;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The overlay cursor has to sit still under a resting finger and still keep up with a real
/// movement. A fixed-coefficient average cannot do both, which is why these two properties are
/// asserted together: making one pass by weakening the filter breaks the other.
/// </summary>
public class CursorFilterTests
{
    /// <summary>Feeds samples spaced far enough apart that the filter uses a real elapsed time.</summary>
    private static void Feed(CursorFilter filter, IEnumerable<(double X, double Y)> samples)
    {
        foreach (var (x, y) in samples)
        {
            filter.Update(x, y);
            Thread.Sleep(8);
        }
    }

    [Fact]
    public void FirstSampleIsTakenAsIs()
    {
        var filter = new CursorFilter(0.5);
        filter.Update(120, 80);

        Assert.Equal(120, filter.X, 3);
        Assert.Equal(80, filter.Y, 3);
    }

    /// <summary>
    /// A finger held still still moves the reported contact centroid by a pixel or so. That must not
    /// reach the cursor at all.
    /// </summary>
    [Fact]
    public void RestingFingerDoesNotMoveTheCursor()
    {
        var filter = new CursorFilter(0.5);
        filter.Update(200, 100);

        var jitter = new[]
        {
            (200.6, 100.4), (199.5, 99.7), (200.8, 100.9), (199.4, 99.3),
            (200.5, 100.6), (199.6, 99.5), (200.7, 100.2), (199.3, 99.8),
        };
        Feed(filter, jitter);

        Assert.True(Math.Abs(filter.X - 200) < 0.5, $"X drifted to {filter.X}");
        Assert.True(Math.Abs(filter.Y - 100) < 0.5, $"Y drifted to {filter.Y}");
    }

    /// <summary>
    /// A deliberate sweep must arrive. Filtering hard enough to kill jitter is worthless if the
    /// cursor then trails the finger across the keyboard.
    /// </summary>
    [Fact]
    public void DeliberateMovementIsFollowed()
    {
        var filter = new CursorFilter(0.5);
        filter.Update(0, 0);

        var sweep = Enumerable.Range(1, 25).Select(i => (X: i * 20.0, Y: 0.0));
        Feed(filter, sweep);

        // Within a fifth of the travelled distance of the target: responsive, not merely eventual.
        Assert.True(filter.X > 400, $"cursor only reached {filter.X} of 500");
    }

    [Fact]
    public void ResetTakesTheNextSampleAsIs()
    {
        var filter = new CursorFilter(0.5);
        filter.Update(10, 10);
        Feed(filter, [(12.0, 12.0), (14.0, 14.0)]);

        filter.Reset();
        filter.Update(900, 500);

        Assert.Equal(900, filter.X, 3);
        Assert.Equal(500, filter.Y, 3);
    }

    /// <summary>
    /// The setting scales how hard a resting finger is filtered. It must stay bounded: a zero or
    /// negative value used to be possible through the settings file and would divide by zero.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    [InlineData(50.0)]
    public void ExtremeSmoothingSettingsStayFinite(double smoothing)
    {
        var filter = new CursorFilter(smoothing);
        filter.Update(50, 50);
        Feed(filter, [(60.0, 60.0), (70.0, 70.0), (80.0, 80.0)]);

        Assert.True(double.IsFinite(filter.X));
        Assert.True(double.IsFinite(filter.Y));
    }
}
