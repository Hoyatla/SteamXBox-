namespace SenSÉ.Core.Runtime;

/// <summary>
/// Asks the desktop environment for something, from the controller runtime.
/// </summary>
/// <remarks>
/// The two live in separate processes, so a controller button cannot call the environment directly.
/// A file dropped in the shared program folder is how the product already asks the environment to
/// come back and asks the overlay keyboard to show and close — one mechanism rather than three.
///
/// <para>
/// <b>Not synthesised keystrokes.</b> Both actions already have a keyboard gesture — two taps of
/// Shift, two taps of the section key — and the runtime could simply produce those. It would be a
/// few lines and it would be wrong: the environment's hook never swallows a keystroke, deliberately,
/// so a synthesised section key also lands in whatever text field has the caret. Pressing Menu on a
/// controller would clear the screen <i>and</i> type <c>§§</c> into the user's document.
/// </para>
/// </remarks>
public static class DesktopSignal
{
    /// <summary>Open the search launcher.</summary>
    public const string Search = "desktop-search.signal";

    /// <summary>Clear the screen, or put the windows back.</summary>
    public const string ClearScreen = "desktop-clear.signal";

    /// <summary>
    /// Drops the signal for the environment to pick up.
    /// </summary>
    /// <remarks>
    /// Silent on failure, and it has to be. This runs on the input path: a read-only folder or a file
    /// held open must cost one shortcut, never a controller that stops reporting.
    /// </remarks>
    public static void Raise(string name)
    {
        try
        {
            // Sous Debug/signal/, et non a la racine du produit : c'est la que le guetteur ecoute.
            // Les deux se sont deja contredits — le guetteur avait ete deplace, pas l'ecrivain, et
            // le bouton Menu de la manette ne faisait plus rien du tout.
            SenSÉ.Core.Diagnostics.CheminDebug.AssurerRacine();

            File.WriteAllText(
                SenSÉ.Core.Diagnostics.CheminDebug.Signal(name),
                DateTime.UtcNow.Ticks.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
