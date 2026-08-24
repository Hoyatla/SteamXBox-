using Sc2Xboxed.Core.Input;
using Sc2Xboxed.Core.Output;

namespace Sc2Xboxed.Core.Mapping;

/// <summary>
/// Ce qu'est une manette PS5, et rien d'autre.
/// </summary>
/// <remarks>
/// Ce fichier ne parle que de la famille PS5. Il ne lit rien d'une autre famille et aucune autre
/// famille ne le lit. Une DualSense est une DualSense : elle n'est pas une manette Xbox avec
/// d'autres nombres, et elle n'est pas une manette Steam sans pads.
///
/// <para>
/// Ce qui se trouvait avant a la place de ce fichier : une branche <c>DualSense or XInput</c> pour
/// les reglages, et un <c>else</c> unique pour la correspondance des boutons. Deux materiels
/// differents servis par deux lignes partagees. Il n'y avait aucun endroit ou ecrire ce qui n'est
/// vrai que d'une DualSense.
/// </para>
///
/// <para>
/// Plusieurs valeurs sont aujourd'hui identiques a celles de <see cref="XboxControllerDefaults"/>.
/// Ce n'est pas une raison de refusionner les deux fichiers : un clavier et une souris peuvent
/// partager un taux de repetition sans devenir le meme appareil. La duplication est le but.
/// </para>
///
/// <para>
/// <b>Ce que cette famille n'a pas en Bluetooth.</b> Mesure au banc du 15 aout, mode de
/// compatibilite : les gachettes sont tout-ou-rien — une course lente et complete de L2 n'a produit
/// que deux valeurs, <c>0x00</c> et <c>0xFF</c>. Tout reglage de seuil analogique de gachette est
/// donc sans effet sur ce transport, et le rester tant que le mode complet <c>0x31</c> n'est pas
/// rallume. Le pave tactile n'y est qu'un contact, sans coordonnees, et ni gyroscope ni
/// accelerometre n'y sont emis. Rien de tout cela ne doit etre propose comme reglage a
/// l'utilisateur d'une manette connectee sans fil. Voir <c>mesures/dualsense-bt/</c>.
/// </para>
/// </remarks>
public static class Ps5ControllerDefaults
{
    /// <summary>La famille que ce fichier decrit, et la seule.</summary>
    public const ControllerKind Kind = ControllerKind.DualSense;

    /// <summary>
    /// Les boutons de gauche d'une DualSense, dans l'ordre ou l'interface les montre.
    /// </summary>
    /// <remarks>
    /// Pas de L4 ni de L5 : les palettes arriere sont des boutons de manette Steam. Les lister ici
    /// mettait quatre lignes mortes dans l'onglet PS5, reglables et sans effet, parce que la liste
    /// etait celle d'une manette Steam servie a tout le monde.
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

    /// <summary>Les boutons de droite d'une DualSense.</summary>
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
    /// Les reglages de depart d'une DualSense.
    /// </summary>
    /// <remarks>
    /// Pas de trackpads : la surface tactile existe mais n'est pas decodee, et pretendre le
    /// contraire ferait attendre au mapper des touchers qui n'arrivent jamais.
    ///
    /// <para>
    /// Stick gauche muet. Cable sur les fleches, un stick qui repose a quelques pourcents du centre
    /// maintient une direction enfoncee tant que la manette est allumee. Signale le 14 aout :
    /// "clavier et souris physique cassé". Le curseur reste sur le stick droit, la seule chose
    /// qu'une manette sans pad puisse raisonnablement faire sur un bureau.
    /// </para>
    /// </remarks>
    /// <para>
    /// Une seule instance, figee. Recalculee a chaque appel, elle fabriquait un dictionnaire neuf a
    /// chaque fois : deux lectures des memes reglages n'etaient alors jamais egales, parce qu'un
    /// enregistrement compare ses dictionnaires par reference. Construite au premier acces et non
    /// par l'initialiseur de type, pour la raison ecrite dans <c>SteamControllerDefaults.Deferred</c>.
    /// </para>
    public static Sc2XboxedProfileSettings Settings => Deferred.Value;

    /// <inheritdoc cref="Settings"/>
    /// <remarks>
    /// Hors de l'initialiseur de type comme chez ses deux jumeaux : lire
    /// <see cref="Sc2XboxedProfileSettings"/> depuis un initialiseur de type ferme un cycle, et
    /// selon l'ordre d'entree le <c>Bare</c> lu est encore a null.
    /// </remarks>
    private static readonly Lazy<Sc2XboxedProfileSettings> Deferred = new(()
        => Sc2XboxedProfileSettings.Bare with
        {
            HasTrackpads = false,
            LeftStickMode = StickMotionMode.None,
            RightStickMode = StickMotionMode.Pointer,
            XboxButtons = ButtonMap.ToDictionary(Kind),
        });

    /// <summary>Le reglage manette-native de depart d'une DualSense.</summary>
    public static XboxTuning Tuning => Settings.XboxTuning;

    /// <summary>
    /// La correspondance boutons de depart d'une DualSense.
    /// </summary>
    /// <remarks>
    /// Ecrite ici en entier, pas heritee. Options et Partager suivent deja la convention Xbox sur
    /// cette manette : Options sort en Start, Partager en Back. L'inversion Menu/View mesuree sur
    /// une manette Steam reste sur une manette Steam.
    ///
    /// <para>
    /// Une instance neuve a chaque appel. <see cref="XboxButtonMap"/> est modifiable, et deux
    /// mappers tenant la meme instance sont deux mappers dont l'un rebranche les boutons de l'autre.
    /// </para>
    /// </remarks>
    public static XboxButtonMap ButtonMap
    {
        get
        {
            var map = new XboxButtonMap();

            map[SteamControllerButtons.A] = Xbox360Buttons.A;               // croix
            map[SteamControllerButtons.B] = Xbox360Buttons.B;               // rond
            map[SteamControllerButtons.X] = Xbox360Buttons.X;               // carre
            map[SteamControllerButtons.Y] = Xbox360Buttons.Y;               // triangle

            map[SteamControllerButtons.LeftBumper] = Xbox360Buttons.LeftShoulder;    // L1
            map[SteamControllerButtons.RightBumper] = Xbox360Buttons.RightShoulder;  // R1
            map[SteamControllerButtons.LeftStick] = Xbox360Buttons.LeftThumb;        // L3
            map[SteamControllerButtons.RightStick] = Xbox360Buttons.RightThumb;      // R3

            map[SteamControllerButtons.DPadUp] = Xbox360Buttons.DPadUp;
            map[SteamControllerButtons.DPadDown] = Xbox360Buttons.DPadDown;
            map[SteamControllerButtons.DPadLeft] = Xbox360Buttons.DPadLeft;
            map[SteamControllerButtons.DPadRight] = Xbox360Buttons.DPadRight;

            map[SteamControllerButtons.Menu] = Xbox360Buttons.Start;        // options, a droite
            map[SteamControllerButtons.View] = Xbox360Buttons.Back;         // partager, a gauche

            return map;
        }
    }
}
