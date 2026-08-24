using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SteamXBox.Tools.Modeles;

namespace SteamXBox.Desktop.Modeles;

/// <summary>
/// Le choix des jeux de modèles, selon ce que la machine peut porter.
/// </summary>
/// <remarks>
/// <b>Pourquoi ce choix doit exister.</b> Le produit dépend de modèles, et ces modèles changeront
/// au besoin des clients : celui qui a une carte de 8 Go et celui qui en a 24 n'installeront pas
/// les mêmes, et aucun des deux ne doit avoir à le deviner. Sans cet écran, il ne reste que deux
/// issues — un produit qui ne tourne que sur la machine de son auteur, ou un client qui télécharge
/// treize gigaoctets pour découvrir que sa carte ne les prend pas.
///
/// <para>
/// <b>Ce qui ne tient pas est montré quand même, et dit pourquoi.</b> Cacher les jeux trop gros
/// laisserait croire qu'ils n'existent pas ; les proposer sans rien dire laisserait recommencer la
/// même erreur. Ils apparaissent donc, marqués, et leur bouton est éteint : l'utilisateur voit ce
/// qu'une meilleure carte lui donnerait, sans pouvoir se tromper aujourd'hui.
/// </para>
///
/// <para>
/// <b>La licence est en face du nom.</b> Un modèle sous licence non commerciale est utilisable pour
/// essayer et interdit dans un produit vendu — et rien ne le distingue à l'usage. Ce projet a déjà
/// livré une icône dont la gratuité exigeait une attribution visible ; la découverte s'est faite
/// tard et a coûté cher.
/// </para>
/// </remarks>
public partial class ModelesWindow : Window
{
    private readonly Action<string>? _journal;
    private readonly List<JeuModeles> _jeux;
    private CancellationTokenSource? _annulation;

    /// <summary>Ouvre l'écran, ou ramène celui qui est déjà là.</summary>
    public static string Ouvrir(Action<string>? journal)
    {
        var deja = Application.Current.Windows.OfType<ModelesWindow>().FirstOrDefault();

        if (deja is not null)
        {
            deja.Activate();

            return "";
        }

        try
        {
            new ModelesWindow(journal).Show();

            return "";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"modeles panel could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return "L'écran des jeux de modèles n'a pas pu être dessiné.";
        }
    }

    private ModelesWindow(Action<string>? journal)
    {
        InitializeComponent();

        // La taille et la place que l'utilisateur lui a donnees, d'une session a l'autre.
        SuiviFenetre.Suivre(this, "modeles");

        _journal = journal;
        _jeux = [.. JeuxModeles.Charger(journal)];

        Closed += (_, _) => _annulation?.Cancel();

        Dessiner();
    }

    private void Dessiner()
    {
        Liste.Children.Clear();

        var libre = InstallationModeles.MemoireLibreMo();
        var conseils = JeuxModeles.Conseils(_jeux, libre);

        var classe = libre <= 0 ? null
            : libre <= 6000 ? Nom(Palier.Bureau)
            : libre <= 12000 ? Nom(Palier.Jeu)
            : Nom(Palier.Expert);

        Carte.Text = InstallationModeles.Carte() + (conseils.Count == 0
            ? _jeux.Count == 0
                ? " Aucun jeu n'est déclaré."
                : " Aucun jeu déclaré ne tient dans cette mémoire."
            : classe is null ? "" : $" Cette machine correspond au « {classe} ».");

        if (_jeux.Count == 0)
        {
            Etat.Text = "Rien à proposer : la déclaration est absente ou illisible. "
                + $"Elle est attendue en {JeuxModeles.Fichier}";

            return;
        }

        // Ce qui est déjà là vient en premier, puis les paliers.
        //
        // L'ordre suit la question que l'utilisateur se pose en ouvrant l'écran : « qu'est-ce que
        // j'ai ? », puis « qu'est-ce que je peux prendre ? ». Une liste qui commence par des
        // propositions oblige à chercher dans le tas ce qui est déjà installé.
        var installes = _jeux
            .Where(j => JeuxModeles.Etat(j, JeuxModeles.Racine) == EtatJeu.Installe)
            .ToList();

        if (installes.Count > 0)
        {
            Titre("Installés sur cette machine");

            // Un jeu installé qui est aussi le meilleur pour cette carte le dit. Sans cela, ne
            // voir « conseillé » nulle part se lit comme « rien ne convient », alors que la vraie
            // réponse est « vous avez déjà ce qu'il faut ».
            foreach (var jeu in installes.OrderBy(j => j.Role, StringComparer.Ordinal))
            {
                Liste.Children.Add(Vignette(jeu, libre, conseils.FirstOrDefault(c => c.Id == jeu.Id)));
            }
        }

        // Les paliers sont nommés par la machine et non par la qualité : « le meilleur » ne veut
        // rien dire à qui ne sait pas ce que sa carte porte, alors qu'un poste familial se
        // reconnaît sans rien connaître aux modèles.
        foreach (var palier in new[] { Palier.Bureau, Palier.Jeu, Palier.Expert })
        {
            var groupe = _jeux
                .Where(j => j.Palier == palier && !installes.Contains(j))
                .OrderBy(j => j.Role, StringComparer.Ordinal)
                .ThenByDescending(j => j.MemoireVideoMo)
                .ToList();

            if (groupe.Count == 0)
            {
                continue;
            }

            Titre(Nom(palier), Convient(palier, libre) ? "AccentBrush" : "TextSecondaryBrush");

            foreach (var jeu in groupe)
            {
                Liste.Children.Add(Vignette(jeu, libre, conseils.FirstOrDefault(c => c.Id == jeu.Id)));
            }
        }
    }

    private void Titre(string texte, string pinceau = "TextSecondaryBrush")
        => Liste.Children.Add(new TextBlock
        {
            Text = texte,
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 8),
            Foreground = (Brush)FindResource(pinceau),
        });

    /// <summary>Le palier, nommé par la machine à laquelle il s'adresse.</summary>
    private static string Nom(Palier palier) => palier switch
    {
        Palier.Bureau => "Set PC Bureau / Famille",
        Palier.Jeu => "Set PC Gaming",
        _ => "Set Expert exigeant",
    };

    /// <summary>Ce palier correspond-il à la carte de cette machine ?</summary>
    /// <remarks>
    /// Le titre du palier qui convient est mis en avant, plutôt qu'une mention ajoutée à côté :
    /// c'est la première chose que l'œil cherche en ouvrant l'écran, et une couleur se voit avant
    /// qu'une phrase se lise.
    /// </remarks>
    private static bool Convient(Palier palier, int libre) => libre > 0 && palier switch
    {
        Palier.Bureau => libre <= 6000,
        Palier.Jeu => libre is > 6000 and <= 12000,
        _ => libre > 12000,
    };

    /// <summary>Un jeu : ce qu'il est, ce qu'il coûte, et ce qu'on peut en faire.</summary>
    private Border Vignette(JeuModeles jeu, int libre, JeuModeles? conseille)
    {
        var etat = JeuxModeles.Etat(jeu, JeuxModeles.Racine);
        var tient = libre == 0 || jeu.Tient(libre);

        var corps = new StackPanel();

        corps.Children.Add(new TextBlock
        {
            Text = jeu.Nom + (ReferenceEquals(jeu, conseille) ? "   — conseillé" : ""),
            FontSize = 14,
            Foreground = (Brush)FindResource(
                ReferenceEquals(jeu, conseille) ? "AccentBrush" : "TextPrimaryBrush"),
        });

        if (jeu.Pour.Length > 0)
        {
            corps.Children.Add(Ligne(jeu.Pour, "TextSecondaryBrush"));
        }

        var memoire = jeu.MemoireVideoMo.ToString(CultureInfo.InvariantCulture);

        corps.Children.Add(Ligne(
            $"{JeuxModeles.Poids(jeu.Octets)} à télécharger · {memoire} Mio de mémoire vidéo"
            + $" · licence {(jeu.Licence.Length > 0 ? jeu.Licence : "non déclarée")}"
            + (jeu.Modules.Count > 0 ? $" · module {string.Join(", ", jeu.Modules)}" : ""),
            "TextDimBrush"));

        if (!tient)
        {
            corps.Children.Add(Ligne(
                $"Ne tient pas : cette carte offre {libre.ToString(CultureInfo.InvariantCulture)} Mio "
                + "libres. Il tournerait en échangeant avec la mémoire vive, "
                + "des dizaines de fois plus lentement.",
                "TextDimBrush"));
        }

        var bouton = new Button
        {
            Content = etat switch
            {
                EtatJeu.Installe => "Installé",
                EtatJeu.Partiel => "Reprendre",
                _ => "Installer",
            },
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = etat != EtatJeu.Installe && tient,
        };

        bouton.Click += (_, _) => Installer(jeu, bouton);

        corps.Children.Add(bouton);

        return new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Background = (Brush)FindResource("BgMediumBrush"),
            CornerRadius = new CornerRadius(4),
            Child = corps,
        };
    }

    private TextBlock Ligne(string texte, string pinceau) => new()
    {
        Text = texte,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 0),
        Foreground = (Brush)FindResource(pinceau),
    };

    /// <summary>
    /// Télécharge un jeu, hors du fil d'affichage.
    /// </summary>
    /// <remarks>
    /// Treize gigaoctets sur le fil d'affichage figeraient l'environnement une heure durant, et
    /// Windows afficherait « ne répond pas » — ce qui se lit comme un plantage. C'est la faute déjà
    /// commise avec le démarrage du générateur.
    /// </remarks>
    private void Installer(JeuModeles jeu, Button bouton)
    {
        bouton.IsEnabled = false;
        _annulation = new CancellationTokenSource();

        var jeton = _annulation.Token;
        var journal = _journal;

        Etat.Text = $"Téléchargement de « {jeu.Nom} »...";

        Task.Run(() => InstallationModeles.Installer(
                jeu,
                JeuxModeles.Racine,
                a => Dispatcher.Invoke(() => Etat.Text =
                    $"{a.Fichier} — {(a.Part * 100).ToString("0", CultureInfo.InvariantCulture)} % "
                    + $"(fichier {a.Rang.ToString(CultureInfo.InvariantCulture)} sur "
                    + $"{a.Combien.ToString(CultureInfo.InvariantCulture)})"),
                journal,
                jeton))
            .ContinueWith(
                fini => Dispatcher.Invoke(() =>
                {
                    var refus = fini.IsFaulted
                        ? fini.Exception?.GetBaseException().Message ?? "échec"
                        : fini.Result;

                    Etat.Text = refus.Length > 0
                        ? refus
                        : $"« {jeu.Nom} » est installé."
                          + (jeu.Modules.Count > 0
                              ? $" Il lui faut encore le module {string.Join(", ", jeu.Modules)}."
                              : "");

                    // Redessiné plutôt que corrigé au coup par coup : l'état d'un jeu se lit sur le
                    // disque, et un bouton mis à jour à la main finirait par mentir.
                    Dessiner();
                }),
                TaskScheduler.Default);
    }
}
