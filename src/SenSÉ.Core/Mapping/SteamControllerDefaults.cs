using SenSÉ.Core.Input;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Mapping;

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
    /// enregistrement compare ses dictionnaires par reference. Construite au premier acces et non
    /// par l'initialiseur de type — <c>Deferred</c>, juste en dessous, dit pourquoi.
    /// </para>
    public static SenSÉProfileSettings Settings => Deferred.Value;

    /// <summary>
    /// Les memes reglages, construits au premier acces plutot que par l'initialiseur de type.
    /// </summary>
    /// <remarks>
    /// <b>Le defaut que ceci corrige.</b> Un cycle entre deux initialiseurs de type.
    /// <see cref="SenSÉProfileSettings"/> construit son <c>Bare</c> ; l'initialiseur d'instance
    /// de cet enregistrement appelle <c>XboxButtonMap.Default</c>, qui lit <see cref="LeftSide"/>
    /// — donc l'initialiseur de cette classe-ci. Celui-ci relisait <c>Bare</c>, encore a null
    /// puisque toujours en cours de construction plus bas dans la meme pile. <c>null with { ... }</c>
    /// leve une NullReferenceException, l'initialiseur de type est marque en echec, et toute lecture
    /// ulterieure de cette classe releve la meme exception jusqu'a la fin du processus.
    ///
    /// <para>
    /// Lequel des deux initialiseurs commencait decidait de tout, et rien d'autre : entrer par
    /// <see cref="Settings"/> allait bien, entrer par <c>Bare</c> cassait tout. Invisible tant qu'un
    /// seul thread ouvre le bal, et une serie de tests sur un thread par coeur tire cet ordre au
    /// sort a chaque execution — d'ou 68 tests en echec d'un coup, une fois sur vingt, sans qu'aucun
    /// d'eux ne soit en cause.
    /// </para>
    ///
    /// <para>
    /// Sortir ces reglages de l'initialiseur de type rend le graphe acyclique : cette classe ne
    /// depend plus que de ses propres tableaux, et <c>Bare</c> n'est lu qu'une fois celui-ci
    /// termine. <see cref="Lazy{T}"/> et non un <c>??=</c> : l'instance doit rester unique, deux
    /// threads arrivant ensemble en fabriqueraient deux et des reglages relus ne seraient plus egaux
    /// aux premiers.
    /// </para>
    /// </remarks>
    private static readonly Lazy<SenSÉProfileSettings> Deferred = new(()
        => SenSÉProfileSettings.Bare with
        {
            HasTrackpads = true,
            LeftStickMode = StickMotionMode.ArrowKeys,
            RightStickMode = StickMotionMode.Pointer,
            XboxButtons = ButtonMap.ToDictionary(Kind),
        });

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
