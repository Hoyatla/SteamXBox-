namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// A source that can say its controller has gone away without giving up on it.
/// </summary>
/// <remarks>
/// Most sources answer the question by ending their stream: the device closed, the reader stops,
/// and whoever was listening knows. <b>The XInput source deliberately does not.</b> It polls a slot
/// rather than holding a device, and a controller that sleeps often comes back on a different slot,
/// so ending the stream would force the whole source to be rebuilt for an ordinary nap. It forgets
/// the slot and keeps waiting instead.
///
/// <para>
/// The consequence was that an Xbox pad switched off mid-session was never reported as gone at all.
/// Measured on 11 August 2026: a DualSense produced three departures in one session and an Xbox pad
/// none, so its virtual pad stayed presented to games as a player who never presses anything, and
/// the device SteamXBox had hidden stayed hidden from every other application until the session
/// ended.
/// </para>
///
/// <para>
/// This is the narrow way out: the source says "gone for now" and stays alive. Waiting for silence
/// instead would have been generic and wrong — a connected HID controller that nobody is touching
/// sends nothing either, and would be declared absent while sitting on the desk.
/// </para>
/// </remarks>
public interface IReportsDeparture
{
    /// <summary>
    /// Raised when the source no longer has a controller, though it is still watching for one.
    /// </summary>
    /// <remarks>
    /// Raised once per absence, not once per attempt to find it again: the slot is polled every
    /// second while empty, and a listener that gives a device back does not want to be asked to do
    /// it sixty times a minute.
    /// </remarks>
    event Action? Departed;
}
