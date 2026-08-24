using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SteamXBox.Desktop.Tools;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Atelier;

/// <summary>
/// Plusieurs outils qui vont ensemble, réunis en un classeur à intercalaires.
/// </summary>
/// <remarks>
/// <b>Pourquoi réunir plutôt que multiplier les tuiles.</b> Créer une image, l'animer, l'agrandir :
/// ce sont trois moments d'un même travail, et non trois outils qu'on choisit indépendamment. Trois
/// tuiles obligeaient à fermer l'une pour ouvrir la suivante, et à retrouver à la main le fichier
/// que la précédente venait de produire. Un classeur garde les trois sous la main, dans l'ordre où
/// l'on s'en sert.
///
/// <para>
/// <b>Rien n'est réécrit.</b> Chaque onglet est le panneau que l'outil aurait eu seul, dessiné par
/// le même code depuis le même manifeste. Les outils réunis restent des manifestes ordinaires,
/// installables et désinstallables un par un ; seul l'endroit où ils s'affichent change. Un
/// classeur qui aurait sa propre interface aurait fait diverger les deux, et c'est celle qu'on
/// n'ouvre pas qui aurait pourri.
/// </para>
///
/// <para>
/// <b>Les deux dossiers.</b> Ce qu'on dépose et ce qui en sort sont les deux questions qu'on se
/// pose en travaillant ; jusqu'ici il fallait retrouver le chemin à la main, quelque part sous
/// <c>Outils</c>. Ils sont là, et ils ouvrent l'explorateur de Windows, pas une liste maison :
/// l'utilisateur y fait ce qu'il sait déjà faire.
/// </para>
/// </remarks>
public partial class AtelierWindow : Window
{
    private readonly List<PanneauOutil> _panneaux = [];
    private readonly Action<string>? _journal;

    /// <summary>
    /// Ouvre le classeur nommé par une cible, ou ramène celui qui est déjà ouvert.
    /// </summary>
    /// <param name="cible">
    /// Les identifiants des outils à réunir, séparés par <c>|</c>, dans l'ordre d'affichage.
    /// </param>
    /// <param name="ouvrirSur">
    /// L'outil à mettre au premier plan, quand on ouvre le classeur pour l'un d'eux en
    /// particulier. Vide pour le premier onglet.
    /// </param>
    public static string Ouvrir(string cible, Action<string>? journal, string ouvrirSur = "")
    {
        var deja = Application.Current.Windows.OfType<AtelierWindow>().FirstOrDefault();

        if (deja is not null)
        {
            deja.Montrer(ouvrirSur);
            deja.Activate();

            return "";
        }

        try
        {
            var fenetre = new AtelierWindow(cible, journal);

            if (fenetre._panneaux.Count == 0)
            {
                fenetre.Close();

                return "Aucun des outils réunis n'est installé.";
            }

            fenetre.Show();
            fenetre.Montrer(ouvrirSur);

            return "";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"atelier could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return "L'atelier n'a pas pu être dessiné.";
        }
    }

    private AtelierWindow(string cible, Action<string>? journal)
    {
        InitializeComponent();

        _journal = journal;

        var outils = PluginCatalog.Scan(PluginTools.Folder).Loaded
            .Where(m => m.Kind == PluginCategory.Tool)
            .ToList();

        // Le classeur retrouve son propre manifeste par la cible : c'est lui qui dit ce que ce
        // regroupement veut dire, et l'écrire une seconde fois dans le code ferait deux titres à
        // tenir d'accord.
        var sien = outils.Find(m =>
            m.Does.Equals(PluginActions.Atelier, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Target, cible, StringComparison.OrdinalIgnoreCase));

        Title = sien?.Name ?? "Atelier";
        Titre.Text = Title;
        Aide.Text = sien?.Hint ?? "";

        var installes = outils.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        // L'ordre est celui de la cible, pas celui du disque : c'est l'ordre dans lequel on
        // travaille, et le manifeste du classeur est le seul endroit où quelqu'un l'a décidé.
        foreach (var id in PluginActions.Reunis(cible))
        {
            if (!installes.TryGetValue(id, out var manifeste))
            {
                journal?.Invoke($"atelier : « {id} » n'est pas installé, onglet omis.");

                continue;
            }

            // Un outil que l'utilisateur a éteint dans les réglages reste éteint ici : le classeur
            // regroupe, il ne passe pas outre.
            if (!PluginLifecycle.IsEnabled(manifeste.Id, manifeste.Enabled))
            {
                continue;
            }

            var panneau = new PanneauOutil(manifeste, journal);
            _panneaux.Add(panneau);

            Onglets.Items.Add(new TabItem
            {
                Header = manifeste.Name,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(4, 10, 4, 4),
                    Content = panneau,
                },
            });
        }

        if (Onglets.Items.Count > 0)
        {
            Onglets.SelectedIndex = 0;
        }

        Prechauffer(outils);
    }

    /// <summary>
    /// Démarre le générateur pendant que l'utilisateur remplit le formulaire.
    /// </summary>
    /// <remarks>
    /// <b>Le serveur n'a pas besoin de démarrer au clic, il a besoin d'être prêt au clic.</b> Le
    /// produit vit sur un disque externe : le premier démarrage d'une session coûte environ deux
    /// minutes, le temps d'ouvrir un par un les soixante-douze mille fichiers de Python. Attendues
    /// après avoir cliqué sur « Créer », ces deux minutes ressemblent à une panne — c'est arrivé
    /// deux fois, l'utilisateur quittant avant la fin.
    ///
    /// <para>
    /// Or on passe bien une minute à écrire une description et à régler des curseurs. Démarré à
    /// l'ouverture de la fenêtre, le serveur est prêt quand on clique, et l'attente a disparu
    /// derrière le travail. Cela ne coûte rien de plus : il démarrait de toute façon.
    /// </para>
    ///
    /// <para>
    /// Seulement si un onglet en a besoin. Un classeur qui réunirait des outils sans rapport avec
    /// la génération allumerait sinon le plus gros consommateur du produit pour rien.
    /// </para>
    /// </remarks>
    private void Prechauffer(IReadOnlyList<PluginManifest> outils)
    {
        var utile = _panneaux.Any(p => outils
            .FirstOrDefault(m => m.Id == p.Id)?.Content
            .Exists(c => c.Does.Equals(PluginActions.Flux, StringComparison.OrdinalIgnoreCase))
            == true);

        if (!utile || !SteamXBox.Tools.Generation.ComfyServer.Installe)
        {
            return;
        }

        if (SteamXBox.Tools.Generation.ComfyServer.Repond())
        {
            return;
        }

        Etat.Text = "Le générateur démarre pendant que vous réglez...";

        Task.Run(() => SteamXBox.Tools.Generation.ComfyServer.Preparer(Avancement));
    }

    /// <summary>Montre l'avancement du préchauffage sans quitter le fil de l'attente.</summary>
    private void Avancement(string message)
    {
        _journal?.Invoke(message);
        Dispatcher.BeginInvoke(() => Etat.Text = message);
    }

    /// <summary>Met l'onglet d'un outil au premier plan.</summary>
    /// <remarks>
    /// Un identifiant vide, ou qui ne désigne aucun onglet, laisse la sélection où elle est :
    /// l'assistant peut demander le classeur sans viser un outil, et un onglet retiré du classeur
    /// ne doit pas faire échouer l'ouverture.
    /// </remarks>
    public void Montrer(string outil)
    {
        if (outil.Length == 0)
        {
            return;
        }

        var rang = _panneaux.FindIndex(p => p.Id.Equals(outil, StringComparison.OrdinalIgnoreCase));

        if (rang >= 0 && rang < Onglets.Items.Count)
        {
            Onglets.SelectedIndex = rang;
        }
    }

    private void EntreeClic(object sender, RoutedEventArgs e) => Dossier("input");

    private void SortieClic(object sender, RoutedEventArgs e) => Dossier("output");

    /// <summary>
    /// Ouvre un dossier de l'outil dans l'explorateur.
    /// </summary>
    /// <remarks>
    /// Le dossier est créé s'il manque : ouvrir l'explorateur sur un chemin absent donne une erreur
    /// de Windows qui parle d'un raccourci cassé, ce qui ne dit rien de vrai — le dossier n'a
    /// simplement pas encore servi.
    ///
    /// <para>
    /// Par l'explorateur et non par une liste maison : SteamXBox demande les droits administrateur,
    /// et tout ce qu'il lance en hérite. <c>explorer.exe</c>, lui, tourne sous le compte de
    /// l'utilisateur, et ce qu'il ouvre s'ouvre normalement.
    /// </para>
    /// </remarks>
    private void Dossier(string quel)
    {
        var ou = Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI", quel);

        try
        {
            Directory.CreateDirectory(ou);

            var depart = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            depart.ArgumentList.Add(ou);

            Process.Start(depart)?.Dispose();

            Etat.Text = ou;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException
                      or System.ComponentModel.Win32Exception)
        {
            _journal?.Invoke($"atelier : dossier {quel} non ouvert : {exception.Message}");
            Etat.Text = $"Le dossier n'a pas pu être ouvert : {ou}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Chaque panneau enregistre ce que son outil a demandé de retenir, et se retire du registre
        // que l'assistant consulte. Fermer le classeur sans cela laisserait l'assistant croire que
        // des panneaux sont encore ouverts.
        foreach (var panneau in _panneaux)
        {
            panneau.Fermer();
        }

        base.OnClosed(e);
    }
}
