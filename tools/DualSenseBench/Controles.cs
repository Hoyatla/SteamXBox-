namespace DualSenseBench;

/// <summary>Une commande de la manette, telle qu'elle s'affiche dans la liste.</summary>
/// <param name="Nom">Ce que l'opérateur lit.</param>
/// <param name="Aide">Précision quand le nom seul peut se tromper de bouton.</param>
public sealed record Controle(string Nom, string Aide = "");

/// <summary>
/// Les commandes d'une DualSense, dans l'ordre où on les presse.
/// </summary>
/// <remarks>
/// L'ordre suit la main : la face, les épaules, les gâchettes, les clics de sticks, les petits
/// boutons, puis ce qui se pousse. Les commandes qui n'existent peut-être pas dans le rapport
/// Bluetooth de compatibilité — PS, muet, pavé — sont dedans comme les autres : le banc dira
/// « rien détecté » et ce sera la réponse, pas un oubli.
/// </remarks>
public static class Controles
{
    public static IReadOnlyList<Controle> Tout { get; } =
    [
        new("Croix"),
        new("Rond"),
        new("Carré"),
        new("Triangle"),
        new("L1"),
        new("R1"),
        new("L2", "presser à fond"),
        new("R2", "presser à fond"),
        new("L3", "clic du stick gauche"),
        new("R3", "clic du stick droit"),
        new("Create", "petit bouton de GAUCHE"),
        new("Options", "petit bouton de DROITE"),
        new("PS"),
        new("Muet", "sous le bouton PS"),
        new("Clic du pavé tactile"),
        new("Doigt glissé sur le pavé", "sans cliquer"),
        new("Croix directionnelle HAUT"),
        new("Croix directionnelle DROITE"),
        new("Croix directionnelle BAS"),
        new("Croix directionnelle GAUCHE"),
        new("Stick gauche à DROITE", "à fond"),
        new("Stick gauche en HAUT", "à fond"),
        new("Stick droit à DROITE", "à fond"),
        new("Stick droit en HAUT", "à fond"),
        new("Manette secouée", "sans toucher aucune commande"),
    ];
}
