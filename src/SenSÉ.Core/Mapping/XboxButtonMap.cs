using SenSÉ.Core.Input;
using SenSÉ.Core.Output;

namespace SenSÉ.Core.Mapping;

/// <summary>
/// Which Xbox 360 button each physical Steam Controller button produces in Xbox360 mode.
/// </summary>
/// <remarks>
/// Steam and Quick Access are deliberately absent: they drive SenSÉ itself — launching Steam and
/// switching modes — so letting a profile rebind them would be a way to lock yourself out of the
/// application from the controller.
///
/// <see cref="Default"/> reproduces the mapping that was previously hard-coded, so a profile that
/// changes nothing behaves exactly as before. Note that it also mirrors the four back paddles onto
/// the face buttons (L4→X, R4→Y, L5→A, R5→B), which is what makes them usable at all: the Xbox 360
/// layout has no paddles to send them to.
/// </remarks>
public sealed class XboxButtonMap
{
    /// <summary>
    /// Les boutons de gauche d'une famille, dans l'ordre ou l'interface les montre.
    /// </summary>
    /// <remarks>
    /// Par famille, parce que les familles n'ont pas les memes boutons. Une seule liste servait tout
    /// le monde et c'etait celle d'une manette Steam : les onglets PS5 et Xbox montraient quatre
    /// lignes de palettes arriere que ces manettes n'ont pas, reglables et sans effet.
    /// </remarks>
    public static SteamControllerButtons[] LeftSideFor(ControllerKind kind) => kind switch
    {
        Ps5ControllerDefaults.Kind => Ps5ControllerDefaults.LeftSide,
        XboxControllerDefaults.Kind => XboxControllerDefaults.LeftSide,
        _ => SteamControllerDefaults.LeftSide,
    };

    /// <inheritdoc cref="LeftSideFor"/>
    public static SteamControllerButtons[] RightSideFor(ControllerKind kind) => kind switch
    {
        Ps5ControllerDefaults.Kind => Ps5ControllerDefaults.RightSide,
        XboxControllerDefaults.Kind => XboxControllerDefaults.RightSide,
        _ => SteamControllerDefaults.RightSide,
    };

    /// <summary>Tout ce qu'une famille peut rebrancher.</summary>
    public static IEnumerable<SteamControllerButtons> AllFor(ControllerKind kind)
        => LeftSideFor(kind).Concat(RightSideFor(kind));

    /// <summary>Every physical button a profile may rebind, in the order the interface shows them.</summary>
    public static SteamControllerButtons[] LeftSide => SteamControllerDefaults.LeftSide;

    public static SteamControllerButtons[] RightSide => SteamControllerDefaults.RightSide;

    /// <summary>
    /// L'union de tous les boutons de toutes les familles, pour la lecture d'un fichier de profil.
    /// </summary>
    /// <remarks>
    /// Une union, pas un defaut. Elle sert a relire un profil ecrit par une autre famille ou par une
    /// version anterieure sans en perdre les entrees ; elle ne dit pas ce qu'une manette possede.
    /// Pour ca, <see cref="AllFor"/>.
    /// </remarks>
    public static IEnumerable<SteamControllerButtons> All
        => SteamControllerDefaults.LeftSide.Concat(SteamControllerDefaults.RightSide);

    private readonly Dictionary<SteamControllerButtons, Xbox360Buttons> _map = [];

    /// <summary>The mapping for a Steam Controller, which is what this product started as.</summary>
    public static XboxButtonMap Default => DefaultFor(ControllerKind.SteamController);

    /// <summary>
    /// The mapping a controller of this kind should start with.
    /// </summary>
    /// <remarks>
    /// Only Menu and View differ, and the reason is worth stating because it looked like a bug for a
    /// long time. On a Steam Controller the two buttons produce Back and Start the other way round
    /// from what their labels suggest — measured on the hardware, reverted once, and put back. That
    /// finding was then applied to every controller, and on a DualSense or an Xbox pad, whose labels
    /// already follow the Xbox convention, it crosses them: Options and Start come out as Back.
    ///
    /// <para>
    /// So the quirk stays where it was measured and nowhere else. A controller is not a Steam
    /// Controller unless it is one.
    /// </para>
    /// </remarks>
    /// <para>
    /// Un aiguillage et rien d'autre. Chaque famille ecrit sa disposition en entier dans son propre
    /// fichier. Ce qui se trouvait ici — un <c>Common</c> partage plus un <c>else</c> pour tout ce
    /// qui n'est pas une manette Steam — servait la PS5 et la Xbox par la meme ligne, et faisait de
    /// la disposition d'une manette Steam la base de celle des deux autres.
    /// </para>
    public static XboxButtonMap DefaultFor(ControllerKind kind) => kind switch
    {
        Ps5ControllerDefaults.Kind => Ps5ControllerDefaults.ButtonMap,
        XboxControllerDefaults.Kind => XboxControllerDefaults.ButtonMap,
        _ => SteamControllerDefaults.ButtonMap,
    };

    public Xbox360Buttons this[SteamControllerButtons physical]
    {
        get => _map.TryGetValue(physical, out var output) ? output : Xbox360Buttons.None;
        set => _map[physical] = value;
    }

    /// <summary>Translates a frame's pressed buttons into the Xbox 360 buttons to report.</summary>
    public Xbox360Buttons Apply(SteamControllerButtons pressed)
    {
        var mapped = Xbox360Buttons.None;

        foreach (var physical in All)
        {
            if (pressed.HasFlag(physical))
            {
                mapped |= this[physical];
            }
        }

        // Steam is not rebindable but still has to reach the game as Guide.
        if (pressed.HasFlag(SteamControllerButtons.Steam))
        {
            mapped |= Xbox360Buttons.Guide;
        }

        return mapped;
    }

    /// <summary>Serialisable form: physical button name to Xbox button name.</summary>
    public Dictionary<string, string> ToDictionary()
        => All.ToDictionary(b => b.ToString(), b => this[b].ToString());

    /// <summary>
    /// Forme enregistrable limitee aux boutons que cette famille possede.
    /// </summary>
    /// <remarks>
    /// Un profil PS5 n'a rien a dire des palettes arriere d'une manette Steam. Les ecrire quand meme
    /// mettait quatre entrees mortes dans chaque fichier de profil, que la prochaine relecture prend
    /// pour des reglages.
    /// </remarks>
    public Dictionary<string, string> ToDictionary(ControllerKind kind)
        => AllFor(kind).ToDictionary(b => b.ToString(), b => this[b].ToString());

    /// <summary>
    /// Rebuilds a map from stored names, falling back to the default for anything missing or
    /// unrecognised. A profile written by a newer build, or edited by hand, must never produce a
    /// controller with dead buttons.
    /// </summary>
    public static XboxButtonMap FromDictionary(IReadOnlyDictionary<string, string>? stored)
        => FromDictionary(stored, ControllerKind.SteamController);

    /// <inheritdoc cref="FromDictionary(IReadOnlyDictionary{string, string})"/>
    public static XboxButtonMap FromDictionary(
        IReadOnlyDictionary<string, string>? stored, ControllerKind kind)
    {
        var map = DefaultFor(kind);
        if (stored is null)
        {
            return map;
        }

        foreach (var physical in All)
        {
            if (stored.TryGetValue(physical.ToString(), out var name)
                && Enum.TryParse<Xbox360Buttons>(name, ignoreCase: true, out var output))
            {
                map[physical] = output;
            }
        }

        return map;
    }
}
