using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Ce qu'est une manette Steam, et rien d'autre.
/// </summary>
/// <remarks>
/// Ce fichier existe pour la meme raison que ses deux jumeaux, plus une : les valeurs d'une manette
/// Steam etaient la base implicite de tout le monde. Les familles PS5 et Xbox partaient de
/// <c>Default</c>, qui decrit une manette Steam, et retiraient ensuite ce qui ne leur allait pas.
/// Une famille construite en soustrayant d'une autre n'est pas une famille separee.
///
/// <para>
/// Cette manette est la seule des trois a avoir des trackpads, des palettes arriere, un bouton
/// Steam et un bouton Quick Access. C'est aussi la seule sur laquelle l'inversion Menu/View a ete
/// mesuree. Rien de tout cela ne doit atteindre les deux autres.
/// </para>
/// </remarks>
public static class SteamControllerDefaults
{
    /// <summary>La famille que ce fichier decrit, et la seule.</summary>
    public const ControllerKind Kind = ControllerKind.SteamController;

    /// <summary>Les boutons de gauche d'une manette Steam, palettes comprises.</summary>
    public static readonly SteamControllerButtons[] LeftSide =
    [
        SteamControllerButtons.LeftBumper,
        SteamControllerButtons.L4,
        SteamControllerButtons.L5,
        SteamControllerButtons.DPadUp,
        SteamControllerButtons.DPadLeft,
        SteamControllerButtons.DPadRight,
        SteamControllerButtons.DPadDown,
        SteamControllerButtons.LeftStick,
        SteamControllerButtons.View,
    ];

    /// <summary>Les boutons de droite d'une manette Steam, palettes comprises.</summary>
    public static readonly SteamControllerButtons[] RightSide =
    [
        SteamControllerButtons.RightBumper,
        SteamControllerButtons.R4,
        SteamControllerButtons.R5,
        SteamControllerButtons.Y,
        SteamControllerButtons.X,
        SteamControllerButtons.B,
        SteamControllerButtons.A,
        SteamControllerButtons.RightStick,
        SteamControllerButtons.Menu,
    ];

    /// <summary>
    /// Les reglages de depart d'une manette Steam.
    /// </summary>
    /// <remarks>
    /// Ses pads pilotent le curseur, donc ses sticks sont libres : le gauche prend les fleches. Ce
    /// cablage est correct ici et destructeur ailleurs, ce qui est exactement pourquoi il est ecrit
    /// ici et nulle part ailleurs.
    /// </remarks>
    /// <para>
    /// Une seule instance, figee, pour la meme raison que dans les deux autres fichiers : un
    /// enregistrement compare ses dictionnaires par reference.
    /// </para>
    public static readonly Sc2XboxedProfileSettings Settings = Sc2XboxedProfileSettings.Bare with
    {
        HasTrackpads = true,
        LeftStickMode = StickMotionMode.ArrowKeys,
        RightStickMode = StickMotionMode.Pointer,
        XboxButtons = ButtonMap.ToDictionary(Kind),
    };

    /// <summary>Le reglage manette-native de depart d'une manette Steam.</summary>
    public static XboxTuning Tuning => Settings.XboxTuning;

    /// <summary>
    /// La correspondance boutons de depart d'une manette Steam.
    /// </summary>
    /// <remarks>
    /// Menu et View sortent a l'envers de ce que leurs etiquettes suggerent. Mesure sur le materiel,
    /// annule une fois, puis remis. Ce constat a ensuite ete applique a toutes les manettes, ce qui
    /// croisait les deux boutons sur une DualSense et sur une manette Xbox, dont les etiquettes
    /// suivent deja la convention Xbox. Il reste ou il a ete mesure.
    ///
    /// <para>
    /// Les quatre palettes se rabattent sur les boutons de face — la disposition Xbox 360 n'a pas de
    /// palettes ou les envoyer — et c'est ce qui les rend utilisables.
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

            map[SteamControllerButtons.LeftBumper] = Xbox360Buttons.LeftShoulder;
            map[SteamControllerButtons.RightBumper] = Xbox360Buttons.RightShoulder;
            map[SteamControllerButtons.LeftStick] = Xbox360Buttons.LeftThumb;
            map[SteamControllerButtons.RightStick] = Xbox360Buttons.RightThumb;

            map[SteamControllerButtons.DPadUp] = Xbox360Buttons.DPadUp;
            map[SteamControllerButtons.DPadDown] = Xbox360Buttons.DPadDown;
            map[SteamControllerButtons.DPadLeft] = Xbox360Buttons.DPadLeft;
            map[SteamControllerButtons.DPadRight] = Xbox360Buttons.DPadRight;

            map[SteamControllerButtons.L4] = Xbox360Buttons.X;
            map[SteamControllerButtons.R4] = Xbox360Buttons.Y;
            map[SteamControllerButtons.L5] = Xbox360Buttons.A;
            map[SteamControllerButtons.R5] = Xbox360Buttons.B;

            map[SteamControllerButtons.Menu] = Xbox360Buttons.Back;
            map[SteamControllerButtons.View] = Xbox360Buttons.Start;

            return map;
        }
    }
}
