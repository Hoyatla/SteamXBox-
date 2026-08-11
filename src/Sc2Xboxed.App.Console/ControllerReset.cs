using Sc2Xboxed.Hid;

namespace Sc2Xboxed.App.Console;

/// <summary>
/// Puts a Steam Controller back to the state it has when nobody has touched it.
/// </summary>
/// <remarks>
/// <b>Steam does not put the controller back.</b> Steam Input takes over the firmware layer for as
/// long as Steam runs — its own mappings, its own trackpad modes — and on the way out it leaves
/// whatever it last wrote. A controller that has been through a Steam session is therefore in a
/// state neither Steam nor SteamXBox chose, and the symptom is a pad that does nothing recognisable
/// until it is unplugged and put back.
///
/// <para>
/// Reclaiming after Steam has to begin from a known state rather than assume one. This asks the
/// controller for its default mappings — the same command the native layer is restored with — so
/// that whatever SteamXBox applies next is applied to a device in a state it recognises.
/// </para>
///
/// <para>
/// <b>Opened and closed here.</b> The reclaim happens between two sessions of reading, at a moment
/// when nothing holds the device: the source that read it was disposed when SteamXBox stood down,
/// and the one that will read it next does not exist yet. So this opens the device for the length of
/// one feature report and lets go.
/// </para>
/// </remarks>
internal static class ControllerReset
{
    /// <summary>
    /// Asks the controller to return to its own defaults, and says what happened.
    /// </summary>
    /// <remarks>
    /// Never throws. A controller that went away with Steam, or has not finished reappearing, is an
    /// ordinary outcome here — the reclaim continues either way, and the source that opens next is
    /// what will really decide whether there is a controller to read.
    /// </remarks>
    internal static void ToNative(Action<string> log)
    {
        try
        {
            var device = new SteamHidDiscovery(log).FindPreferredControllerDevice();

            if (device is null)
            {
                log("Reset after Steam: no Steam Controller to reset.");
                return;
            }

            using var stream = device.Open();

            SteamControllerLizardMode.Enable(stream, new object());

            log("Reset after Steam: the controller is back to its own defaults.");
        }
        catch (Exception exception)
        {
            log($"Reset after Steam failed, continuing anyway: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
