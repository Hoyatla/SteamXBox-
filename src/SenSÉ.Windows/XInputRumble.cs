using System.Runtime.InteropServices;

namespace SenSÉ.Windows;

/// <summary>
/// Sends vibration to a physical XInput pad by its slot.
/// </summary>
/// <remarks>
/// The slot comes from the controller's <see cref="SenSÉ.Core.Input.ControllerIdentity"/>, which
/// <see cref="XInputControllerSource"/> pinned to the slot it reads — so the pad a game vibrates and
/// the pad that actually shakes are the same device. The virtual pad SenSÉ serves the game never
/// shakes on its own; the feedback is translated here, on the physical controller that owns it.
///
/// <para>
/// Writing vibration is safe beside the reads the source does: <c>XInputSetState</c> and
/// <c>XInputGetState</c> are independent calls, and Windows serialises them itself.
/// </para>
///
/// <para>
/// Failure is silent and returns false. A missing controller, a missing DLL or a pad that rejects the
/// command are all normal states on a machine where pads come and go; none of them is worth throwing
/// into a rumble path that games may fire at any moment.
/// </para>
/// </remarks>
public static class XInputRumble
{
    private const uint ErrorSuccess = 0;

    /// <summary>
    /// Vibrates the pad on the given XInput slot.
    /// </summary>
    /// <param name="slot">XInput slot (0..3), from the controller's identity.</param>
    /// <param name="leftMotor">Left (low-frequency) motor, 0 to 1.</param>
    /// <param name="rightMotor">Right (high-frequency) motor, 0 to 1.</param>
    /// <returns>False when the slot is invalid or the call failed, without throwing.</returns>
    public static bool SetVibration(int slot, double leftMotor, double rightMotor)
    {
        if (slot < 0 || slot >= XInputControllerSource.SlotCount)
        {
            return false;
        }

        try
        {
            var vibration = new XInputVibration
            {
                LeftMotorSpeed = ToSpeed(leftMotor),
                RightMotorSpeed = ToSpeed(rightMotor),
            };

            return XInputSetState((uint)slot, ref vibration) == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Amplitude 0..1 to the motor's 16-bit range.</summary>
    private static ushort ToSpeed(double amplitude)
        => (ushort)Math.Round(Math.Clamp(amplitude, 0.0, 1.0) * ushort.MaxValue);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(uint index, ref XInputVibration vibration);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }
}
