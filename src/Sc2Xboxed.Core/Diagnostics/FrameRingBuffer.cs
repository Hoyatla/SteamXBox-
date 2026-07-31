using System.Globalization;
using Sc2Xboxed.Core.Input;

namespace Sc2Xboxed.Core.Diagnostics;

/// <summary>
/// Keeps the most recent controller frames in memory so they can be dumped when something
/// interesting happens.
/// </summary>
/// <remarks>
/// This is the answer to wanting per-frame detail without paying for it continuously: frames cost
/// nothing while nothing is wrong, and the context leading up to a mode switch, an ownership change
/// or an overlay toggle is still available afterwards.
/// </remarks>
public sealed class FrameRingBuffer
{
    private readonly string[] _lines;
    private readonly object _gate = new();
    private int _next;
    private int _count;

    public FrameRingBuffer(int capacity = 240)
    {
        _lines = new string[Math.Max(8, capacity)];
    }

    public int Capacity => _lines.Length;

    public void Add(SteamControllerState state)
    {
        var line = Format(state);

        lock (_gate)
        {
            _lines[_next] = line;
            _next = (_next + 1) % _lines.Length;
            if (_count < _lines.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>Returns the buffered frames oldest-first and clears the buffer.</summary>
    public List<string> Drain()
    {
        lock (_gate)
        {
            var result = new List<string>(_count);
            var start = (_next - _count + _lines.Length) % _lines.Length;

            for (int i = 0; i < _count; i++)
            {
                var line = _lines[(start + i) % _lines.Length];
                if (line is not null)
                {
                    result.Add(line);
                }
            }

            _next = 0;
            _count = 0;
            return result;
        }
    }

    private static string Format(SteamControllerState state)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "btn={0} ls=({1:0.000},{2:0.000}) rs=({3:0.000},{4:0.000}) lt={5:0.00} rt={6:0.00} " +
            "lp=({7:0.000},{8:0.000} t={9} c={10}) rp=({11:0.000},{12:0.000} t={13} c={14})",
            state.Buttons,
            state.LeftStick.X, state.LeftStick.Y,
            state.RightStick.X, state.RightStick.Y,
            state.LeftTrigger, state.RightTrigger,
            state.LeftPad.X, state.LeftPad.Y, state.LeftPad.IsTouched, state.LeftPad.IsPressed,
            state.RightPad.X, state.RightPad.Y, state.RightPad.IsTouched, state.RightPad.IsPressed);
    }
}
