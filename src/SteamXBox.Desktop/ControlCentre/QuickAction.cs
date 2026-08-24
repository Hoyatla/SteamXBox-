using System.Diagnostics;
using SteamXBox.Desktop.Tools;

namespace SteamXBox.Desktop.ControlCentre;

/// <summary>One tile of the control centre.</summary>
/// <param name="Glyph">Segoe Fluent Icons code point, from <see cref="Glyphs"/>.</param>
/// <param name="Label">Short label, in French, which doubles as the translation key.</param>
/// <param name="Hint">One line describing what the tile does, shown under the title on focus.</param>
/// <param name="YieldsForeground">
/// Whether the window must get out of the way before the action runs. True only for the actions
/// that send keystrokes: those go to whatever window is in front, and it must not be this one.
/// </param>
/// <param name="WithEnvironment">
/// Set instead of <paramref name="Invoke"/> for actions that need the environment window itself —
/// the capture has to hide and restore it. Typed rather than left as an empty Invoke that some
/// other file secretly intercepts. Returns a line for the status area.
/// </param>
/// <param name="Icon">
/// Une géométrie à dessiner au lieu du glyphe, pour les tuiles dont l'icône vient de
/// <c>PluginIcons.xaml</c> plutôt que de la police. Les deux ne cohabitent pas sur une même tuile :
/// la géométrie l'emporte quand elle est là.
/// </param>
public sealed record QuickAction(
    string Glyph,
    string Label,
    string Hint,
    Action Invoke,
    bool YieldsForeground = false,
    Func<System.Windows.Window, string>? WithEnvironment = null,
    System.Windows.Media.Geometry? Icon = null)
    : System.ComponentModel.INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Visible quand la tuile porte une géométrie.</summary>
    /// <remarks>
    /// Calculé ici plutôt que par un convertisseur : deux propriétés lues directement par le gabarit
    /// évitent d'ajouter une ressource de conversion pour une question à laquelle la tuile sait déjà
    /// répondre.
    /// </remarks>
    public System.Windows.Visibility IconVisibility
        => Icon is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    /// <summary>Visible quand la tuile porte un glyphe de police.</summary>
    public System.Windows.Visibility GlyphVisibility
        => Icon is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Comment la tuile obtient son compte, quand elle en a un.</summary>
    /// <remarks>
    /// La fonction est gardée, pas son résultat. Gardé, le résultat aurait été celui de l'unique
    /// instant où la grille a été bâtie — voir <see cref="Compteur"/>.
    /// </remarks>
    public Func<string>? Compte { get; init; }

    /// <summary>
    /// Un chiffre montré sous l'icône, quand la tuile a quelque chose à compter.
    /// </summary>
    /// <remarks>
    /// Vide pour presque toutes : une tuile n'affiche un compte que si ce compte veut dire quelque
    /// chose.
    ///
    /// <para>
    /// <b>Demandé à chaque lecture, et non retenu.</b> Il l'a été : la liste des tuiles est bâtie
    /// par l'initialiseur d'un membre statique, donc une seule fois par processus, et un compte
    /// figé là valait pour l'instant du démarrage — celui où, précisément, rien ne tourne encore.
    /// L'utilisateur lançait le générateur, rouvrait la grille, et le moniteur d'activité affichait
    /// toujours rien pendant que deux serveurs occupaient la carte.
    /// </para>
    ///
    /// <para>
    /// Un calcul à chaque lecture ne suffirait pourtant pas : rien ne relit une liaison WPF sans
    /// qu'on le lui dise. <see cref="Rafraichir"/> est ce signal, et la fenêtre le donne quand elle
    /// revient devant l'utilisateur. Le compte est donc pris là, et retenu jusqu'au signal suivant :
    /// deux propriétés liées le lisent chacune, et recenser les processus deux fois par
    /// rafraîchissement ne dirait rien de plus.
    /// </para>
    /// </remarks>
    public string Compteur => _compteur ??= Compte?.Invoke() ?? "";

    private string? _compteur;

    /// <summary>Visible seulement quand il y a quelque chose à compter.</summary>
    public System.Windows.Visibility CompteurVisibility
        => Compteur.Length == 0
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    /// <summary>Redemande le compte à l'outil et le fait relire par la tuile.</summary>
    /// <remarks>
    /// Appelé quand l'environnement revient au premier plan, donc au moment où quelqu'un regarde.
    /// Rien pour une tuile sans compte : la très grande majorité, qui n'a aucune raison de payer
    /// une notification.
    /// </remarks>
    public void Rafraichir()
    {
        if (Compte is null)
        {
            return;
        }

        _compteur = Compte();

        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Compteur)));
        PropertyChanged?.Invoke(
            this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CompteurVisibility)));
    }
}

/// <summary>
/// What the control centre offers: SteamXBox's own tools, then shortcuts into Windows.
/// </summary>
/// <remarks>
/// The grid is composed from two lists rather than written out by hand, because the two halves are
/// not the same kind of thing.
///
/// The tools come from <see cref="ToolRegistry"/>. They belong to SteamXBox, and each is one field
/// away from running as its own process — which is how they will eventually become plugins.
///
/// The shortcuts below stay shortcuts. They open a Windows panel that already exists and works;
/// what costs the user time is finding it, and a deep link removes that cost today where rebuilding
/// the panel would take weeks and end up worse. There is nothing here worth extracting.
///
/// Real in-place controls — a volume slider, a Wi-Fi switch, changing the output device without
/// leaving the window — need Core Audio and the radio APIs. Absent rather than present and dead.
/// </remarks>
public static class QuickActions
{
    public static IReadOnlyList<QuickAction> All => Toutes();

    /// <summary>La liste refaite à l'instant, parce que les dossiers ont pu changer.</summary>
    /// <remarks>
    /// C'était un membre statique initialisé une fois par processus. Un outil déposé pendant que
    /// SteamXBox tourne restait donc invisible jusqu'au redémarrage suivant, ce qui contredit la
    /// règle du produit : un outil est un dossier, déposé il est installé. La grille redemande
    /// maintenant la liste quand la veille signale que les dossiers ont bougé.
    /// </remarks>
    public static IReadOnlyList<QuickAction> Toutes() =>
    [
        .. ToolRegistry.All.Select(FromTool),

        // Les outils declares NE SONT PAS ajoutes ici pour l'instant, et c'est deliberé.
        //
        // Plugins/ contient des manifestes qui decrivent des outils que l'environnement implemente
        // DEJA en dur — « Convertir un document » en tete, avec sa barre de progression et sa vraie
        // conversion. Les ajouter produisait deux tuiles pour un seul outil, dont une qui ouvrait une
        // fenetre generique sans progression ni conversion : l'utilisateur cliquait la mauvaise.
        //
        // Le chargeur (PluginTiles / PluginWindow / PluginVerbs) reste ecrit et compile. Le brancher
        // demande d'abord de decider ce qui arrive quand un manifeste porte le meme id qu'un outil
        // compile : l'ignorer, le remplacer, ou refuser de charger. Cette question n'est pas
        // tranchee, et tant qu'elle ne l'est pas, l'environnement garde ses outils.

        .. WindowsShortcuts,
    ];

    /// <summary>Turns a tool into a tile, so the grid does not care which half it came from.</summary>
    private static QuickAction FromTool(ToolDescriptor tool) => new(
        tool.Glyph,
        tool.Label,
        tool.Hint,
        Invoke: () => { },
        WithEnvironment: environment => ToolRegistry.Launch(tool, environment),
        Icon: tool.Icon)
    {
        // Le compte est demandé à l'outil, jamais calculé ici : la grille ne sait pas ce qu'un
        // outil aurait à compter, et n'a pas à l'apprendre. La fonction est passée telle quelle,
        // et non son résultat : cette liste est bâtie une fois pour toute la session.
        Compte = tool.Compte,
    };

    /// <summary>Links into panels Windows already provides.</summary>
    private static IEnumerable<QuickAction> WindowsShortcuts =>
    [
        new(Glyphs.Volume, "Sortie audio", "Casque, enceintes ou HDMI", () => OpenSettings("sound")),
        new(Glyphs.Wifi, "Wi-Fi", "Reseaux et connexion sans fil", () => OpenSettings("network-wifi")),
        new(Glyphs.Bluetooth, "Bluetooth", "Appareils et appairage", () => OpenSettings("bluetooth")),
        new(Glyphs.Brightness, "Luminosite", "Affichage et luminosite", () => OpenSettings("display")),
        new(Glyphs.QuietHours, "Ne pas deranger", "Assistant de concentration", () => OpenSettings("quiethours")),
        new(Glyphs.TaskView, "Gestionnaire des taches", "Processus et performances", () => Launch("taskmgr.exe")),
    ];

    /// <summary>Opens a Settings page directly, skipping the navigation the notes complain about.</summary>
    private static void OpenSettings(string page) => Start($"ms-settings:{page}");

    private static void Launch(string executable) => Start(executable);

    private static void Start(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch
        {
            // Not every edition of Windows carries every one of these — Settings pages come and go
            // between releases. A missing one is not worth interrupting the user over.
        }
    }

}
