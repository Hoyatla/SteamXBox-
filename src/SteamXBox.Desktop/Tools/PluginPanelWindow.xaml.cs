using System.Windows;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// La fenêtre d'un outil déclaratif : un titre, une phrase, et son panneau.
/// </summary>
/// <remarks>
/// Le dessin lui-même vit dans <see cref="PanneauOutil"/>, parce qu'il sert aussi aux onglets de
/// l'atelier. Ce qui reste ici est ce qui appartient à une fenêtre et à elle seule : son titre, et
/// le fait d'en ramener une déjà ouverte plutôt que d'en empiler une seconde.
/// </remarks>
public partial class PluginPanelWindow : Window
{
    private readonly PanneauOutil _panneau;

    private PluginPanelWindow(PluginManifest manifeste, Action<string>? log)
    {
        InitializeComponent();

        _panneau = new PanneauOutil(manifeste, log);

        Title = manifeste.Name;
        PanelTitle.Text = manifeste.Name;
        PanelHint.Text = manifeste.Hint;
        Hote.Child = _panneau;
    }

    /// <summary>
    /// Ouvre le panneau de l'outil, ou ramène celui qui est déjà ouvert.
    /// </summary>
    /// <remarks>
    /// Une fenêtre qui échoue à se construire reste inscrite auprès de l'application, à moitié
    /// bâtie, ses champs non initialisés. Parcourir cette liste sans le prévoir avait transformé un
    /// panneau cassé en une tuile qui levait une exception différente à chaque pression — la
    /// première faute cachée derrière la seconde. La construction est gardée, et la recherche
    /// tolère une fenêtre qui n'a jamais fini.
    /// </remarks>
    public static string Open(PluginManifest manifest, Action<string>? log)
    {
        var deja = Application.Current.Windows.OfType<PluginPanelWindow>()
            .FirstOrDefault(f => f._panneau?.Id == manifest.Id);

        if (deja is not null)
        {
            deja.Activate();

            return "";
        }

        try
        {
            new PluginPanelWindow(manifest, log).Show();

            return "";
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin panel '{manifest.Id}' could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return $"{manifest.Name} : le panneau n'a pas pu être dessiné.";
        }
    }

    /// <summary>Règle une option d'un panneau ouvert, où qu'il soit posé.</summary>
    /// <remarks>
    /// Délégué au registre des panneaux plutôt qu'aux fenêtres de l'application : un panneau devenu
    /// onglet d'atelier n'est plus une fenêtre, et cette capacité se serait tue sans rien dire.
    /// </remarks>
    public static string Regler(string outil, string reglage, string valeur)
        => PanneauOutil.Regler(outil, reglage, valeur);

    protected override void OnClosed(EventArgs e)
    {
        _panneau?.Fermer();
        base.OnClosed(e);
    }
}
