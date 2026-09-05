namespace SenSÉ.Core.Runtime;

/// <summary>
/// A controller whose firmware emits keyboard and mouse events on its own.
/// </summary>
/// <remarks>
/// The Steam Controller does this — its "lizard mode" moves the pointer straight from the pads
/// before any software is involved. It has to be silenced while SenSÉ drives the pointer
/// itself, or every gesture is applied twice.
///
/// An optional capability rather than a member of <see cref="IPhysicalControllerSource"/>: an Xbox
/// pad has no such layer, and forcing it to implement the method would leave a call that throws or,
/// worse, silently does nothing while looking like it worked.
/// </remarks>
public interface INativeLayerControl
{
    ValueTask SetNativeLayerEnabledAsync(bool enabled);
}

/// <summary>
/// A controller that can be switched off from software.
/// </summary>
/// <remarks>
/// Separate from <see cref="INativeLayerControl"/> because the two do not travel together. The
/// Steam Controller has both. A DualSense can be powered off over Bluetooth but has no native
/// pointer layer, so it will implement this one alone. An Xbox pad implements neither.
///
/// Splitting them now is what makes adding the DualSense additive: a new source, one interface, and
/// nothing in the loop changes.
/// </remarks>
public interface IPowerControl
{
    /// <summary>Asks the controller to switch off.</summary>
    /// <returns>True when the command was sent; false when the controller refused or was gone.</returns>
    bool SendPowerOff();
}

/// <summary>
/// A controller with trackpads.
/// </summary>
/// <remarks>
/// Declares which mapping drives the pointer in Profile mode. A trackpad reports a position, so the
/// pointer follows the finger and stops when it stops; a stick reports a held displacement, so it
/// drives a velocity. They are different mappings, not two settings of one.
///
/// A marker with no members, deliberately. The question is what the controller <em>is</em>, not
/// what it can be asked to do, and the alternative — inferring it from frames where both pads read
/// as released — would mistake a Steam Controller nobody is touching for a controller that has no
/// pads at all, and switch mappings under the user's hand.
/// </remarks>
public interface ITrackpadInput;
