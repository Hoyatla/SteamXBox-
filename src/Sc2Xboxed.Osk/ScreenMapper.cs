using System.Runtime.InteropServices;
using System.Diagnostics;
using Sc2Xboxed.Hid;

namespace Sc2Xboxed.Osk;

public sealed class ScreenMapper
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private double _rightX;
    private double _rightY;
    private double _leftX;
    private double _leftY;

    public double CursorRadius { get; set; } = 10.0;

    public (double Left, double Top, double Width, double Height) OskBounds { get; private set; }
    public bool IsOskFound { get; private set; }

    public (double X, double Y) RightCursor { get; private set; }
    public (double X, double Y) LeftCursor { get; private set; }

    private DateTime _lastOskCheck = DateTime.MinValue;
    private IntPtr _oskWindow = IntPtr.Zero;

    public void UpdateOskBounds()
    {
        if ((DateTime.UtcNow - _lastOskCheck).TotalMilliseconds < 500)
            return;
        _lastOskCheck = DateTime.UtcNow;

        var osk = Process.GetProcessesByName("osk").FirstOrDefault();
        if (osk is null || osk.MainWindowHandle == IntPtr.Zero)
        {
            IsOskFound = false;
            _oskWindow = IntPtr.Zero;
            return;
        }

        _oskWindow = osk.MainWindowHandle;
        if (GetWindowRect(_oskWindow, out var rect))
        {
            int vx = GetSystemMetrics(SmXvirtualscreen);
            int vy = GetSystemMetrics(SmYvirtualscreen);
            int vw = GetSystemMetrics(SmCxvirtualscreen);
            int vh = GetSystemMetrics(SmCyvirtualscreen);

            OskBounds = (rect.Left - vx, rect.Top - vy, rect.Right - rect.Left, rect.Bottom - rect.Top);
            IsOskFound = true;
        }
    }

    public void Reset()
    {
        if (IsOskFound)
        {
            _rightX = OskBounds.Width / 2.0;
            _rightY = OskBounds.Height / 2.0;
            _leftX = OskBounds.Width / 2.0;
            _leftY = OskBounds.Height / 2.0;
        }
    }

    public void UpdateRightPad(double padX, double padY)
    {
        if (!IsOskFound) return;

        double halfW = OskBounds.Width / 2.0;
        double rightOffset = halfW;

        _rightX = (padX + 1.0) / 2.0 * halfW + rightOffset;
        _rightY = (1.0 - padY) / 2.0 * OskBounds.Height;

        _rightX = Math.Clamp(_rightX, 0, OskBounds.Width);
        _rightY = Math.Clamp(_rightY, 0, OskBounds.Height);

        RightCursor = (OskBounds.Left + _rightX, OskBounds.Top + _rightY);
    }

    public void UpdateLeftPad(double padX, double padY)
    {
        if (!IsOskFound) return;

        double halfW = OskBounds.Width / 2.0;

        _leftX = (padX + 1.0) / 2.0 * halfW;
        _leftY = (1.0 - padY) / 2.0 * OskBounds.Height;

        _leftX = Math.Clamp(_leftX, 0, OskBounds.Width);
        _leftY = Math.Clamp(_leftY, 0, OskBounds.Height);

        LeftCursor = (OskBounds.Left + _leftX, OskBounds.Top + _leftY);
    }
}
