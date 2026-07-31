namespace Sc2Xboxed.Core.Runtime;

/// <summary>
/// Who currently drives the physical controller. Only one owner at a time: Steam and SteamXBox
/// both write HID feature reports, so overlapping ownership means they fight over the device.
/// </summary>
public enum ControllerOwner
{
    /// <summary>
    /// SteamXBox drives the controller: HID stream open, native firmware layer disabled,
    /// virtual Xbox 360 pad plugged in, mapping and haptics active.
    /// </summary>
    SteamXBox,

    /// <summary>
    /// Steam is running and owns the controller. SteamXBox stands fully down: HID stream closed,
    /// native firmware layer restored, virtual pad unplugged, haptics muted.
    /// </summary>
    Steam,
}
