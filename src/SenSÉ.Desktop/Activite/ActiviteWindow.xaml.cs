using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SenSÉ.Desktop.Settings;
using SenSÉ.Desktop.Tools;
using SenSÉ.Tools.Activite;

namespace SenSÉ.Desktop.Activite;

/// <summary>
/// Ce qui tourne au nom de SenSÉ, et de quoi l'arrêter.
/// </summary>
/// <remarks>
/// <b>Un gestionnaire des tâches restreint à ce produit.</b> Celui de Windows montre tout et ne
/// dit rien : parmi trois cents lignes, il ne distingue pas le <c>python.exe</c> qui sert la
/// génération d'images de n'importe quel autre. Ici la liste ne contient que ce qui appartient au
/// produit, chaque ligne dit d'où elle vient, et l'arrêt est à portée de clic.
///
/// <para>
/// La séparation en trois familles n'est pas décorative. Arrêter un programme natif ferme une
/// partie du produit ; arrêter un outil rend de la mémoire vidéo ; arrêter une dépendance coupe
/// un programme qui ne nous appartient pas et que l'utilisateur a peut-être lancé lui-même. Ce ne
/// sont pas les mêmes gestes, et les mélanger dans une liste unique inviterait à les confondre.
/// </para>
/// </remarks>
public partial class ActiviteWindow : Window
{
    private readonly Action<string>? _journal;

    private ActiviteWindow(Action<string>? journal)
    {
        InitializeComponent();

        // La taille et la place que l'utilisateur lui a donnees, d'une session a l'autre.
        SuiviFenetre.Suivre(this, "activite");

        _journal = journal;
        Loaded += (_, _) => Peupler();
    }

    /// <summary>Ouvre la fenêtre, ou ramène celle qui est déjà là.</summary>
    public static string Ouvrir(Action<string>? journal)
    {
        var deja = Application.Current.Windows.OfType<ActiviteWindow>().FirstOrDefault();

        if (deja is not null)
        {
            deja.Activate();

            return "";
        }

        try
        {
            new ActiviteWindow(journal).Show();

            return "";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"activite panel could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return "Le moniteur d'activité n'a pas pu être dessiné.";
        }
    }

    /// <summary>Le nombre d'événements en cours, pour l'afficher sous l'icône de la tuile.</summary>
    public static string Compte()
    {
        try
        {
            var combien = Recensement.Lister(CheminsDependances()).Count;

            return combien == 0 ? "" : combien.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (Exception)
        {
            // Un compte indisponible ne doit pas empêcher la grille de s'afficher.
            return "";
        }
    }

    /// <summary>
    /// Les chemins que les manifestes de dépendance déclarent, résolus.
    /// </summary>
    /// <remarks>
    /// La même source que l'écran des réglages : une dépendance ajoutée là apparaît ici sans une
    /// ligne de plus.
    /// </remarks>
    private static IReadOnlyList<string> CheminsDependances()
        => Settings.Dependances.Lister(PluginTools.Folder).Select(d => d.Ou).ToList();

    private void RafraichirClic(object sender, RoutedEventArgs e) => Peupler();

    /// <summary>Arrête les serveurs inscrits, et rend la mémoire vidéo.</summary>
    private void ToutArreterClic(object sender, RoutedEventArgs e)
    {
        var arretees = SenSÉ.Tools.Serveurs.Ressources.ArreterTout();

        Etat.Text = arretees == 0
            ? "Aucun serveur inscrit n'était en cours."
            : $"{arretees} serveur(s) arrêté(s).";

        _journal?.Invoke($"activite: {Etat.Text}");
        Peupler();
    }

    /// <summary>
    /// Ce que la carte porte, et ce que le produit dit en porter.
    /// </summary>
    /// <remarks>
    /// Les deux chiffres côte à côte, parce qu'ils ne mesurent pas la même chose et que l'écart est
    /// instructif : une carte GeForce en WDDM ne dit pas quelle application consomme quoi —
    /// <c>nvidia-smi</c> rend <c>[N/A]</c> par processus, vérifié. Le total vient donc de la carte,
    /// et le détail de ce que chaque serveur déclare en démarrant. Montrer l'un sans l'autre
    /// laisserait croire à une mesure là où il n'y a qu'une déclaration.
    /// </remarks>
    private static string Memoire()
    {
        var (utilise, total) = SenSÉ.Tools.Serveurs.Ressources.MemoireVideo();
        var declare = SenSÉ.Tools.Serveurs.Ressources.CoutVideoMo();

        if (total == 0)
        {
            return declare == 0 ? "" : $"  Coût vidéo déclaré : {declare} Mo.";
        }

        var dit = declare == 0 ? "" : $", dont {declare} Mo déclarés par les serveurs";

        return $"  Mémoire vidéo : {utilise} Mo sur {total}{dit}.";
    }

    private void Peupler()
    {
        Familles.Children.Clear();

        IReadOnlyList<Evenement> evenements;
        var inaccessibles = 0;

        try
        {
            evenements = Recensement.Lister(CheminsDependances(), out inaccessibles);
        }
        catch (Exception exception)
        {
            _journal?.Invoke($"activite scan failed: {exception.GetType().Name}: {exception.Message}");
            Resume.Text = "Le recensement a échoué.";
            Etat.Text = exception.Message;

            return;
        }

        Resume.Text = evenements.Count switch
        {
            0 => "Rien ne tourne au nom de SenSÉ.",
            1 => "Un événement en cours.",
            _ => $"{evenements.Count} événements en cours.",
        };

        Resume.Text += Memoire();

        // Dit plutôt que tu : une liste incomplète présentée comme complète est le pire service à
        // rendre à quelqu'un qui cherche un programme qui refuse de mourir.
        if (inaccessibles > 0)
        {
            Resume.Text += $"  {inaccessibles} processus n'ont pas pu être examinés — "
                + "lancez SenSÉ en administrateur pour les voir.";
        }

        Section("SenSÉ", Famille.Natif, evenements,
            "Le produit lui-même. Arrêter une de ces lignes en ferme une partie.");

        Section("Outils", Famille.Outil, evenements,
            "Lancés par un outil. Ils survivent à la fermeture de la fenêtre qui les a démarrés, "
            + "et gardent la mémoire vidéo tant qu'ils tournent.");

        Section("Dépendances", Famille.Dependance, evenements,
            "Des programmes extérieurs. Ils peuvent avoir été lancés par autre chose que SenSÉ.");
    }

    private void Section(string titre, Famille famille, IReadOnlyList<Evenement> tous, string explication)
    {
        var siens = tous.Where(e => e.Famille == famille).ToList();

        var bloc = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };

        bloc.Children.Add(new TextBlock
        {
            Text = $"{titre}  ({siens.Count})",
            FontSize = 15,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });

        bloc.Children.Add(new TextBlock
        {
            Text = explication,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextDimBrush"),
        });

        if (siens.Count == 0)
        {
            bloc.Children.Add(new TextBlock
            {
                Text = "aucun",
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("TextDimBrush"),
            });
        }

        foreach (var evenement in siens)
        {
            bloc.Children.Add(Ligne(evenement));
        }

        Familles.Children.Add(bloc);
    }

    private Border Ligne(Evenement evenement)
    {
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texte = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        texte.Children.Add(new TextBlock
        {
            Text = evenement.Nom,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });

        texte.Children.Add(new TextBlock
        {
            Text = evenement.Detail,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextDimBrush"),
        });

        Grid.SetColumn(texte, 0);
        grille.Children.Add(texte);

        var bouton = new Button
        {
            Content = "Arrêter",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        bouton.Click += (_, _) =>
        {
            bouton.IsEnabled = false;
            Etat.Text = Recensement.Arreter(evenement.Pid, CheminsDependances());
            _journal?.Invoke($"activite: {Etat.Text}");
            Peupler();
        };

        Grid.SetColumn(bouton, 1);
        grille.Children.Add(bouton);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("BgMediumBrush"),
            Child = grille,
        };
    }
}
