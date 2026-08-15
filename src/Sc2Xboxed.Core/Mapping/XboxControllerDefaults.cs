using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Ce qu'est une manette Xbox, et rien d'autre.
/// </summary>
/// <remarks>
/// Ce fichier ne parle que de la famille Xbox. Il ne lit rien d'une autre famille et aucune autre
/// famille ne le lit. Voir <see cref="Ps5ControllerDefaults"/>, son jumeau, pour le detail de ce
/// qui se trouvait a la place des deux fichiers.
/// </remarks>
public static class XboxControllerDefaults
{
    /// <summary>La famille que ce fichier decrit, et la seule.</summary>
    public const ControllerKind Kind = ControllerKind.XInput;

    /// <summary>
    /// Les boutons de gauche d'une manette Xbox, dans l'ordre ou l'interface les montre.
    /// </summary>
    /// <remarks>
    /// Pas de L4 ni de L5. Les palettes arriere sont des boutons de manette Steam ; une manette Xbox
    /// de base n'en a pas, et les lister mettait deux lignes mortes dans l'onglet Xbox.
    /// </remarks>
    public static readonly SteamControllerButtons[] LeftSide =
    [
        SteamControllerButtons.LeftBumper,
        SteamControllerButtons.DPadUp,
        SteamControllerButtons.DPadLeft,
        SteamControllerButtons.DPadRight,
        SteamControllerButtons.DPadDown,
        SteamControllerButtons.LeftStick,
        SteamControllerButtons.View,
    ];

    /// <summary>Les boutons de droite d'une manette Xbox.</summary>
    public static readonly SteamControllerButtons[] RightSide =
    [
        SteamControllerButtons.RightBumper,
        SteamControllerButtons.Y,
        SteamControllerButtons.X,
        SteamControllerButtons.B,
        SteamControllerButtons.A,
        SteamControllerButtons.RightStick,
        SteamControllerButtons.Menu,
    ];

    /// <summary>
    /// Les reglages de depart d'une manette Xbox.
    /// </summary>
    /// <remarks>
    /// Pas de trackpads, et le stick gauche muet pour la meme raison que sur une DualSense : cable
    /// sur les fleches, un stick au repos maintient une direction enfoncee et le clavier physique
    /// devient inutilisable. Le curseur reste sur le stick droit.
    /// </remarks>
    /// <para>
    /// Une seule instance, figee, pour la meme raison que cote PS5 : un enregistrement compare ses
    /// dictionnaires par reference, donc des reglages recalcules ne sont jamais egaux a eux-memes.
    /// </para>
    public static readonly Sc2XboxedProfileSettings Settings = Sc2XboxedProfileSettings.Bare with
    {
        HasTrackpads = false,
        LeftStickMode = StickMotionMode.None,
        RightStickMode = StickMotionMode.Pointer,
        XboxButtons = ButtonMap.ToDictionary(Kind),
    };

    /// <summary>Le reglage manette-native de depart d'une manette Xbox.</summary>
    public static XboxTuning Tuning => Settings.XboxTuning;

    /// <summary>
    /// La correspondance boutons de depart d'une manette Xbox.
    /// </summary>
    /// <remarks>
    /// Ecrite ici en entier, pas heritee. Les etiquettes de cette manette suivent deja la convention
    /// Xbox : Menu sort en Start, View en Back. L'inversion mesuree sur une manette Steam reste sur
    /// une manette Steam.
    ///
    /// <para>
    /// Une instance neuve a chaque appel, pour la meme raison que cote PS5 : deux mappers tenant la
    /// meme instance sont deux mappers dont l'un rebranche les boutons de l'autre.
    /// </para>
    /// </remarks>
    public static XboxButtonMap ButtonMap
    {
        get
        {
            var map = new XboxButtonMap();

            map[SteamControllerButtons.A] = Xbox360Buttons.A;
            map[SteamControllerButtons.B] = Xbox360Buttons.B;
            map[SteamControllerButtons.X] = Xbox360Buttons.X;
            map[SteamControllerButtons.Y] = Xbox360Buttons.Y;

            map[SteamControllerButtons.LeftBumper] = Xbox360Buttons.LeftShoulder;    // LB
            map[SteamControllerButtons.RightBumper] = Xbox360Buttons.RightShoulder;  // RB
            map[SteamControllerButtons.LeftStick] = Xbox360Buttons.LeftThumb;        // L3
            map[SteamControllerButtons.RightStick] = Xbox360Buttons.RightThumb;      // R3

            map[SteamControllerButtons.DPadUp] = Xbox360Buttons.DPadUp;
            map[SteamControllerButtons.DPadDown] = Xbox360Buttons.DPadDown;
            map[SteamControllerButtons.DPadLeft] = Xbox360Buttons.DPadLeft;
            map[SteamControllerButtons.DPadRight] = Xbox360Buttons.DPadRight;

            map[SteamControllerButtons.Menu] = Xbox360Buttons.Start;   // a droite
            map[SteamControllerButtons.View] = Xbox360Buttons.Back;    // a gauche

            return map;
        }
    }
}
