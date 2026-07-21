using System.Runtime.InteropServices;
using Sc2Xboxed.Core.Output;
using Sc2Xboxed.Core.Runtime;

namespace Sc2Xboxed.Windows;

public sealed class WindowsMouseSink : IMouseSink
{
    private double _remainderX;
    private double _remainderY;

    public ValueTask SubmitAsync(MouseOutputFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = new List<Input>(2);

        _remainderX += frame.DeltaX;
        _remainderY += frame.DeltaY;

        var moveX = (int)Math.Truncate(_remainderX);
        var moveY = (int)Math.Truncate(_remainderY);
        _remainderX -= moveX;
        _remainderY -= moveY;

        if (moveX != 0 || moveY != 0)
        {
            inputs.Add(Input.MouseMove(moveX, moveY));
        }

        if (frame.WheelDelta != 0)
        {
            inputs.Add(Input.MouseWheel(frame.WheelDelta));
        }

        if (inputs.Count > 0)
        {
            var sent = NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
            if (sent != inputs.Count)
            {
                throw new InvalidOperationException($"SendInput sent {sent}/{inputs.Count} mouse event(s).");
            }
        }

        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Input
    {
        private const uint InputMouse = 0;
        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventWheel = 0x0800;

        private readonly uint _type;
        private readonly MouseInput _mouseInput;

        private Input(uint type, MouseInput mouseInput)
        {
            _type = type;
            _mouseInput = mouseInput;
        }

        public static Input MouseMove(int dx, int dy)
        {
            return new Input(InputMouse, new MouseInput(dx, dy, 0, MouseEventMove, 0, IntPtr.Zero));
        }

        public static Input MouseWheel(int wheelDelta)
        {
            return new Input(InputMouse, new MouseInput(0, 0, (uint)wheelDelta, MouseEventWheel, 0, IntPtr.Zero));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseInput
    {
        private readonly int _dx;
        private readonly int _dy;
        private readonly uint _mouseData;
        private readonly uint _dwFlags;
        private readonly uint _time;
        private readonly IntPtr _dwExtraInfo;

        public MouseInput(int dx, int dy, uint mouseData, uint dwFlags, uint time, IntPtr dwExtraInfo)
        {
            _dx = dx;
            _dy = dy;
            _mouseData = mouseData;
            _dwFlags = dwFlags;
            _time = time;
            _dwExtraInfo = dwExtraInfo;
        }
    }

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    }
}
