using System.Globalization;

namespace Sc2Xboxed.Core.Diagnostics;

/// <summary>
/// Accumulates what the pipeline actually did, summarised once per second.
/// </summary>
/// <remarks>
/// Replaces per-frame logging as the default way to see behaviour. One line per second answers
/// "did the pads emit anything, how much, and did haptics get through" without megabytes of noise.
///
/// <para>
/// <b>Three additions on 12 August</b>, each for a question this line could not answer during a
/// night of diagnosis:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>A touched pad that emits nothing is now an event.</b> It used not to count as activity at
///     all, so the one second that mattered could go unlogged — the instrument was silent about
///     exactly the fault it was watching for.
///   </item>
///   <item>
///     <b>Frames are counted per source.</b> <c>fps=198</c> where 66 was expected says something is
///     wrong but not what; <c>sources=a:66 b:66 c:66</c> says three things are feeding one mapper.
///   </item>
///   <item>
///     <b>Suppression is recorded with its reason.</b> "No pointer output" and "pointer output
///     deliberately skipped because the overlay owns the pad" look identical from outside and need
///     completely different fixes.
///   </item>
/// </list>
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
    private int _padIgnoredFrames;
    private string? _padIgnoredReason;

    /// <summary>Frames delivered by each source, keyed by a short readable id.</summary>
    /// <remarks>
    /// One controller should be one source. More than one entry means the same pad is being read
    /// twice over, and every edge the mapper detects is then computed across interleaved streams.
    /// </remarks>
    private readonly Dictionary<string, int> _framesBySource = [];

    public void Frame(bool rightTouched, bool leftTouched, string? source = null)
    {
        _frames++;
        if (rightTouched) _rightPadTouchFrames++;
        if (leftTouched) _leftPadTouchFrames++;

        if (source is { Length: > 0 })
        {
            _framesBySource[source] = _framesBySource.GetValueOrDefault(source) + 1;
        }
    }

    /// <summary>
    /// A frame where the pad was touched and the pointer path was deliberately not run.
    /// </summary>
    /// <param name="reason">Short, and the same text every time, so it groups in the line.</param>
    public void PadIgnored(string reason)
    {
        _padIgnoredFrames++;
        _padIgnoredReason = reason;
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

    /// <summary>
    /// How far the fingers actually travelled on the pads this interval, in pad units.
    /// </summary>
    /// <remarks>
    /// Added 12 August, after the first version of <see cref="PadIsDeaf"/> called a resting thumb a
    /// fault. "Touched" and "moving" are not the same thing: a finger laid still on the pad is
    /// touched on every frame and must produce nothing, which is correct behaviour and was being
    /// reported twenty-one times in one session as an unexplained defect.
    ///
    /// <para>
    /// Travel is what separates the two, and it is the only reading that can. Emitted pixels cannot:
    /// they are zero in both cases, which is the whole difficulty.
    /// </para>
    /// </remarks>
    private double _padTravel;
    private readonly Dictionary<string, (double X, double Y)> _padWas = [];

    /// <summary>
    /// Records where a pad is now, so the distance covered can be accumulated.
    /// </summary>
    /// <param name="source">Which controller, since these counters are shared by all of them.</param>
    /// <param name="touched">A released pad contributes nothing and forgets its last position.</param>
    public void PadAt(string? source, bool touched, double x, double y)
    {
        if (source is not { Length: > 0 })
        {
            return;
        }

        if (!touched)
        {
            // Forgotten on release: keeping it would count the jump between where the finger left
            // and where the next one lands as travel, which is not a movement anyone made.
            _padWas.Remove(source);
            return;
        }

        if (_padWas.TryGetValue(source, out var was))
        {
            _padTravel += Math.Abs(x - was.X) + Math.Abs(y - was.Y);
        }

        _padWas[source] = (x, y);
    }

    /// <summary>Enough movement that a pointer standing still cannot be explained by a still finger.</summary>
    /// <remarks>
    /// Pad coordinates are normalised, so this is a small fraction of the pad's width — far more
    /// than sensor jitter, far less than a deliberate stroke.
    /// </remarks>
    private const double MeaningfulTravel = 0.02;

    /// <summary>A finger that moved on the pad and moved no pointer.</summary>
    public bool PadIsDeaf => _padTravel > MeaningfulTravel && _mouseEvents == 0;

    public bool HasActivity =>
        _mouseEvents > 0 || _wheelNotches > 0 || _padClicks > 0 ||
        _hapticsSubmitted > 0 || _hapticsDropped > 0 || _overlayHapticRequests > 0 ||
        // A touched pad counts even when it produced nothing. That silence is the interesting case:
        // without this the second in which the pointer died was the second that went unrecorded.
        _rightPadTouchFrames > 0 || _leftPadTouchFrames > 0 || _padIgnoredFrames > 0 ||
        _padTravel > 0 || _framesBySource.Count > 1;

    /// <summary>Formats the interval summary and resets the counters.</summary>
    public string DrainToLine(TimeSpan elapsed, string mode, string owner)
    {
        var seconds = Math.Max(0.001, elapsed.TotalSeconds);
        var fps = _frames / seconds;

        var line = string.Format(
            CultureInfo.InvariantCulture,
            "mode={0} owner={1} fps={2:0} frames={3} | pad touch r/l={4}/{5} travel={14:0.###} clicks={6} " +
            "| mouse events={7} dx={8:0.#}px dy={9:0.#}px | wheel={10} notches " +
            "| haptics sent={11} dropped={12} overlayReq={13}",
            mode, owner, fps, _frames,
            _rightPadTouchFrames, _leftPadTouchFrames, _padClicks,
            _mouseEvents, _mousePixelsX, _mousePixelsY, _wheelNotches,
            _hapticsSubmitted, _hapticsDropped, _overlayHapticRequests,
            _padTravel);

        // Named "SOURCES MULTIPLES" when first written, on the assumption that more than one source
        // meant one controller being read twice. It does not: these counters are shared by every
        // controller in the session, so two pads plugged in is two sources and perfectly ordinary.
        // The alarm fired 292 times in one session of normal use on 12 August. A count is what this
        // can honestly report — the rate beside each name is what says something, and reading it is
        // the human's job, not the label's.
        if (_framesBySource.Count > 1)
        {
            var breakdown = string.Join(" ", _framesBySource
                .OrderByDescending(entry => entry.Value)
                .Select(entry => $"{entry.Key}:{entry.Value}"));

            line += $" | sources={_framesBySource.Count} {breakdown}";
        }

        if (_padIgnoredFrames > 0)
        {
            line += $" | pad ignoré={_padIgnoredFrames} ({_padIgnoredReason})";
        }

        // Injections Windows would not take. Counted where they happen and read here, because until
        // this existed a refused injection was indistinguishable from a delivered one: both raised
        // "mouse events" by the same amount and the pointer stood still either way.
        var (refused, lastError) = Mapping.InputHelper.DrainRefused();

        if (refused > 0)
        {
            line += $" | INJECTION REFUSEE ×{refused} (erreur {lastError})";
        }

        if (PadIsDeaf)
        {
            line += _padIgnoredFrames > 0 ? " | PAD SOURD (voir la raison)" : " | PAD SOURD sans raison connue";
        }

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
        _padIgnoredFrames = 0;
        _padIgnoredReason = null;
        _padTravel = 0;
        _framesBySource.Clear();

        // _padWas is deliberately kept: it is where the fingers are, not what happened this second.
        // Clearing it would lose the first delta of every interval and undercount every stroke that
        // crosses a second boundary.
    }
}
