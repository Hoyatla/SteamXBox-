using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SenSÉ.Desktop.Search;
using SenSÉ.Desktop.Tools;
using SenSÉ.Plugins;
using SenSÉ.Tools.Assistant;
using SenSÉ.Tools.Search;

namespace SenSÉ.Desktop.Assistant;

/// <summary>
/// La conversation avec l'assistant local, et l'exécution de ce qu'il demande.
/// </summary>
/// <remarks>
/// <b>Ce que la fenêtre ajoute au cœur.</b> Le raisonnement et l'appel des outils vivent dans
/// <c>SenSÉ.Tools</c>, où ils sont atteignables par les tests ; ici on dessine, on tient
/// l'attente hors du fil d'affichage, et on branche l'exécution sur le chargeur de plugins. Un
/// outil nommé par le modèle passe exactement par le même chemin que si l'utilisateur avait
/// cliqué son bouton — <see cref="PluginTools.Perform"/>, avec la cible du manifeste.
///
/// <para>
/// C'est ce qui borne l'assistant : il ne peut rien faire qu'un utilisateur ne puisse faire, et
/// rien qu'un manifeste ne déclare. Il ne compose pas de commande et ne touche pas au disque.
/// </para>
/// </remarks>
public partial class AssistantWindow : Window
{
    private readonly Action<string>? _journal;

    /// <summary>L'assistant garde son fil tant que la fenêtre vit.</summary>
    private readonly AssistantLocal _agent = new();
    private bool _occupe;

    /// <summary>De quoi couper l'enchaînement en cours, quand l'utilisateur le demande.</summary>
    private CancellationTokenSource? _arret;

    private AssistantWindow(Action<string>? journal)
    {
        InitializeComponent();

        SenSÉ.Tools.Assistant.Memoire.AssurerDossiers();

        // La taille et la place que l'utilisateur lui a donnees, d'une session a l'autre.
        SuiviFenetre.Suivre(this, "assistant");

        _journal = journal;

        // Sans cela, le document arrive avec la marge d'une page imprimée : le texte se décale et
        // la conversation ne s'aligne plus sur le reste de la fenêtre.
        Echanges.Document.PagePadding = new Thickness(0);

        var modele = SenSÉ.Tools.Assistant.ServeurModele.Modele();

        Presentation.Text = modele is null
            ? "Aucun modèle installé dans Outils\\Modeles."
            : $"Modèle local : {Path.GetFileNameWithoutExtension(modele)}. "
              + "Il peut lancer les outils installés à votre place.";

        Dire("assistant", "Que voulez-vous faire ?");

        Saisie.KeyDown += (_, evenement) =>
        {
            // Entrée envoie, Maj+Entrée passe à la ligne : le geste attendu d'une zone de dialogue.
            if (evenement.Key == Key.Enter
                && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                evenement.Handled = true;
                Demander();
            }
        };

        Saisie.PreviewKeyDown += (_, evenement) =>
        {
            // Un fichier copié dans l'explorateur ne se colle pas comme du texte : le presse-papiers
            // contient le fichier, pas son chemin. Sans ceci, coller ne produisait rien, et
            // l'utilisateur en concluait que la fenêtre refusait le collage.
            if (evenement.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                return;
            }

            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var chemins = Clipboard.GetFileDropList().Cast<string?>()
                        .Where(c => c is { Length: > 0 })
                        .Select(c => c!)
                        .ToList();

                    if (chemins.Count > 0)
                    {
                        Deposer(chemins);
                        evenement.Handled = true;
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Presse-papiers tenu par une autre application : le collage ordinaire reprendra.
            }
        };

        // La levée UIPI reste, mais elle ne suffit pas — et c'est mesuré.
        //
        // Les trois messages Windows sont autorisés, l'élévation est vraie, AllowDrop est vrai, et
        // le dépôt échoue quand même : WPF reçoit les fichiers par OLE, donc par COM, et COM entre
        // deux niveaux d'intégrité ne se débloque pas par un filtre de messages. Le glisser depuis
        // l'Explorateur est donc impossible tant que le produit exige l'administrateur.
        //
        // Elle est conservée parce qu'elle ne coûte rien et couvre les dépôts qui passent bien par
        // WM_DROPFILES ; le chemin réel est le bouton « Fichier… », et le collage Ctrl+V.
        SourceInitialized += (_, _) => GlisserDepose.Autoriser(this, _journal);

        Loaded += (_, _) =>
        {
            // Les serveurs qui donnent prise sur la machine s'ouvrent avec cette fenetre, comme
            // le modele de langage plus bas. Ils n'ont pas d'autre client, et les laisser tourner
            // pour toute la session laissait de quoi injecter des frappes et un navigateur en
            // ecoute sur un port de debogage, longtemps apres la derniere conversation.
            ServeursMcp.Demarrer();

            // L'aiguilleur se leve avec la fenetre, en arriere-plan.
            //
            // Il pese 1,8 Go et repond en 232 ms une fois charge, mais son chargement dure
            // plusieurs secondes : le faire au premier message ferait payer cette attente a la
            // premiere demande, c'est-a-dire au pire moment. Ici, il a le temps de s'installer
            // pendant que l'utilisateur ecrit.
            //
            // Hors du fil d'affichage, evidemment — c'est ce fil qui doit rester libre pour que la
            // fenetre reponde. Et sans bruit si le moteur n'est pas la : une machine sans manifeste
            // d'orchestre marche exactement comme avant, les regles tranchent ce qu'elles savent.
            // L'instance de recherche se leve avec la fenetre, comme les serveurs MCP : elle pese
            // deux cents megaoctets et personne d'autre ne s'en sert. Absente, elle ne manque a
            // personne — la recherche retombe sur le navigateur.
            Task.Run(ServeurRecherche.Demarrer);

            Task.Run(() =>
            {
                if (SenSÉ.Tools.Assistant.ServeurModele.Voies()
                    .Contains("orchestre", StringComparer.OrdinalIgnoreCase))
                {
                    SenSÉ.Tools.Assistant.ServeurModele.Demarrer(
                        "orchestre", message => Dispatcher.Invoke(() => _journal?.Invoke(message)));
                }
            });

            Saisie.Focus();
            Rafraichir();
        };

        // Le modèle vit aussi longtemps que cette fenêtre, et pas une seconde de plus. Le garder
        // au-delà retiendrait cinq gigaoctets de mémoire vive pour une conversation refermée ; le
        // décharger plus tôt — ce qui arrivait — coupe la parole à un assistant en plein travail.
        Closing += (_, _) =>
        {
            var dump = AssistantMemoire.Consolider(_agent, message => _journal?.Invoke(message));
            _journal?.Invoke(dump);
        };
        Closed += (_, _) =>
        {
            SenSÉ.Tools.Assistant.ServeurModele.Arreter(_journal);
            ServeursMcp.Arreter();
            ServeurRecherche.Arreter();
        };
    }

    /// <summary>
    /// Redessine la zone des travaux : ce qui est en cours, ce qui attend, ce qui est fait.
    /// </summary>
    /// <remarks>
    /// <b>La conversation défile, le plan y disparaît.</b> Après six échanges, ce que l'assistant
    /// avait annoncé est hors de l'écran, et l'utilisateur n'a plus aucun moyen de vérifier où il en
    /// est sans remonter. Ici le plan reste posé.
    ///
    /// <para>
    /// C'est aussi le seul endroit où les deux décisions qui appartiennent à l'utilisateur se
    /// prennent : donner son accord au plan, et refermer un travail. Les laisser au modèle, c'était
    /// lui demander de juger de sa propre réussite — et celui-ci a déjà annoncé un générateur lancé
    /// qui ne l'était pas.
    /// </para>
    /// </remarks>
    /// <summary>Écrit sur la jauge ce que le fil occupe.</summary>
    /// <remarks>
    /// <b>En jetons et en pour cent, les deux.</b> Le pour cent dit s'il faut s'inquiéter, le
    /// nombre dit de combien on parle — et c'est lui qui rend la jauge utilisable pour décider
    /// entre compacter et vider. Le seuil de reprise automatique est nommé plutôt que caché : ce
    /// qui va déclencher un redémarrage de fil doit se voir venir.
    ///
    /// <para>
    /// Le compte vient du serveur, à chaque réponse. Avant le premier échange il vaut zéro, et la
    /// jauge le dit ainsi plutôt que de faire semblant d'estimer.
    /// </para>
    /// </remarks>
    private void Jauger()
    {
        var jetons = _agent.Jetons;
        var place = AssistantLocal.Place;
        var part = place > 0 ? (double)jetons / place : 0;

        Contexte.Value = Math.Clamp(part * 100, 0, 100);

        var seuil = (int)(AssistantLocal.Seuil * 100);

        ContexteTexte.Text = jetons == 0
            ? $"Contexte vide sur {place:N0} jetons."
            : $"Contexte : {jetons:N0} / {place:N0} jetons — {part:P0}. "
              + $"L'assistant se compacte seul au-delà de {seuil} %.";

        // Orange des le seuil franchi, et pas a quatre-vingt-quinze pour cent : c'est a ce moment-la
        // que la prochaine reponse repartira sur un fil neuf, donc a ce moment-la qu'il faut le
        // voir. Orange et non rouge : le contexte qui se remplit est le fonctionnement normal, pas
        // une panne — c'est un avertissement, et le rouge de ce theme dit l'echec.
        Contexte.Foreground = part >= AssistantLocal.Seuil
            ? (System.Windows.Media.Brush)FindResource("AccentOrangeBrush")
            : (System.Windows.Media.Brush)FindResource("AccentBrush");

        Compacter.IsEnabled = !_occupe && jetons > 0;
        Vider.IsEnabled = !_occupe && jetons > 0;
    }

    /// <summary>Compacte à la demande, avant que la place ne déborde d'elle-même.</summary>
    private async void CompacterClic(object sender, RoutedEventArgs e)
    {
        if (_occupe)
        {
            return;
        }

        _occupe = true;
        Travail.Visibility = Visibility.Visible;
        Jauger();

        try
        {
            _arret?.Dispose();
            _arret = new CancellationTokenSource();

            var jeton = _arret.Token;

            // Lue ICI, sur le fil d'affichage, et non dans le Task.Run : une case a cocher ne se
            // consulte pas depuis un autre fil. C'est la meme regle que pour « Interactif » dans
            // Repondre, et elle a ete enfreinte ici — chaque compactage levait « le thread appelant
            // ne peut pas acceder a cet objet », la place n'etait jamais rendue, et le message
            // s'affichait sans dire quel geste avait echoue.
            var seul = Seul.IsChecked == true;

            // Le modele est interroge : hors du fil d'affichage, comme un echange ordinaire.
            var garde = await Task.Run(() => _agent.Compacter(
                message => Dispatcher.Invoke(() => Dire("systeme", message)),
                jeton,
                seul));

            Dire("systeme", garde
                ? "Contexte compacté : l'état du travail est passé au carnet, le fil repart neuf."
                : "Rien à compacter : aucun travail en cours à écrire dans un carnet.");
        }
        catch (Exception exception)
        {
            _journal?.Invoke($"compacter failed: {exception.GetType().Name}: {exception.Message}");
            Dire("systeme", exception.Message);
        }
        finally
        {
            Travail.Visibility = Visibility.Collapsed;
            _occupe = false;
            Rafraichir();
        }
    }

    /// <summary>Jette le fil, après avoir dit ce que cela coûte.</summary>
    /// <remarks>
    /// Confirmé parce que c'est irréversible et que le bouton voisin, lui, ne l'est pas : deux
    /// boutons côte à côte dont un seul détruit demandent qu'on distingue lequel avant le clic et
    /// non après.
    /// </remarks>
    private void ViderClic(object sender, RoutedEventArgs e)
    {
        if (_occupe)
        {
            return;
        }

        var reponse = MessageBox.Show(
            "Vider le contexte de l'assistant ?\n\n"
            + "Le fil de la conversation est jeté. La consigne et les carnets restent — "
            + "ce qui n'a pas été noté dans un carnet est perdu.\n\n"
            + "« Compacter » fait de la place en gardant ce qui a été établi.",
            "Vider le contexte",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (reponse != MessageBoxResult.OK)
        {
            return;
        }

        _agent.Vider(message => Dire("systeme", message), Seul.IsChecked == true);
        Rafraichir();
    }

    /// <summary>Dit ce que le mode change, au moment où on le change.</summary>
    private void InteractifChange(object sender, RoutedEventArgs e)
        => Dire(
            "systeme",
            Interactif.IsChecked == true
                ? "Mode interactif armé : l'assistant peut prendre le premier plan, taper au "
                  + "clavier d'une autre fenêtre et déplacer la souris. En développement — "
                  + "attendez-vous à ce qu'il échoue, et gardez la main."
                : "Mode interactif désarmé : l'assistant travaille par les fichiers et les outils. "
                  + "Il ne touche ni au clavier, ni à la souris, ni au premier plan.");

    private void Rafraichir()
    {
        // Les capacités du carnet s'exécutent dans le Task.Run qui fait tourner le modèle, donc hors
        // du fil d'affichage. Marshaler ici plutôt qu'à chaque appel : un seul oubli au mauvais
        // endroit aurait fait tomber la fenêtre pendant une réponse, et seulement dans ce cas-là.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Rafraichir);
            return;
        }

        Jauger();

        Travaux.Children.Clear();

        var carnets = FichierTravail.Lister(_journal);

        // La zone reste là même vide, et dit qu'elle est vide.
        //
        // Se masquer paraissait propre : pas de bandeau inutile au-dessus de la conversation. Mais
        // une zone absente et une zone sans travail sont impossibles à distinguer, et l'utilisateur
        // qui venait justement d'énoncer un plan en a conclu que la fonction n'existait pas. Une
        // ligne grise coûte quinze pixels et répond à la question qu'il se posait.
        if (carnets.Count == 0)
        {
            Travaux.Children.Add(new TextBlock
            {
                Text = "Aucun travail en cours.",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextDimBrush"),
            });

            return;
        }

        foreach (var carnet in carnets)
        {
            Travaux.Children.Add(Vignette(carnet));
        }
    }

    /// <summary>Un carnet : son titre, son état, ses étapes, et ce que l'utilisateur peut décider.</summary>
    private StackPanel Vignette(Travail carnet)
    {
        var corps = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var faites = carnet.Taches.Count(t => t.Faite);

        corps.Children.Add(new TextBlock
        {
            Text = $"{carnet.Titre}  ({faites}/{carnet.Taches.Count})",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });

        // Ce qui reste dicible sans bouton : corriger un plan.
        //
        // L'accord préalable a disparu, pas la possibilité de changer d'avis. Changer un plan,
        // c'est dire en quoi, et cela s'écrit dans la conversation — un bouton ne saurait pas quoi
        // demander. La phrase reste donc, pour que la zone ne se lise pas comme un aller simple.
        corps.Children.Add(new TextBlock
        {
            Text = "Pour changer quelque chose, dites-le simplement dans la conversation.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush"),
        });

        foreach (var tache in carnet.Taches)
        {
            corps.Children.Add(new TextBlock
            {
                Text = (tache.Faite ? "  ✓  " : "  ·  ") + tache.Texte,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource(
                    tache.Faite ? "TextDimBrush" : "TextSecondaryBrush"),
            });
        }

        var boutons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };

        // « Terminé » disait le contraire de ce qu'il faisait.
        //
        // Le mot se lit comme un accord — « c'est bon, vas-y » — alors que le bouton efface le
        // carnet. Le 23 août, l'utilisateur l'a cliqué juste après avoir répondu à une question de
        // l'assistant : le carnet a disparu, l'appel « travail_accepter » qui suivait a répondu
        // « Aucun carnet de ce nom », et le modèle a lancé le travail sans accord ni plan.
        //
        // Le libellé nomme donc maintenant l'effacement, et la confirmation est demandée : c'est la
        // seule action irréversible de cette fenêtre.
        // Le même geste, mais pas le même mot selon le moment.
        //
        // Avant l'accord, c'est un plan qu'on abandonne — rien n'a été fait, et « effacer » ferait
        // craindre de perdre quelque chose. Après, c'est un travail qu'on referme parce qu'il
        // convient. Le libellé unique « Terminé » avait déjà causé un dégât : lu comme « c'est bon,
        // vas-y », il effaçait le carnet au moment où l'utilisateur voulait donner son accord.
        var restantes = FichierTravail.Restantes(carnet);

        var refermer = new Button
        {
            Content = restantes.Count == 0 ? "Terminer ce travail" : "Refermer sans finir",
            Padding = new Thickness(10, 4, 10, 4),
        };

        // Ce qui reste est nommé, pas compté.
        //
        // « Êtes-vous sûr ? » ne dit rien que l'utilisateur ne sache déjà. Les étapes non cochées,
        // écrites une par une, disent ce qu'il perd s'il se trompe de bouton — et c'est la seule
        // question à laquelle il a besoin de répondre.
        refermer.Click += (_, _) =>
        {
            var reste = restantes.Count == 0
                ? ""
                : "\n\nIl reste :\n· " + string.Join("\n· ", restantes);

            var reponse = MessageBox.Show(
                $"Refermer le travail « {carnet.Titre} » ?{reste}"
                + "\n\nIl sera rangé dans Travaux\\Finis, où il reste lisible. "
                + "Cela ne supprime aucun fichier produit.",
                restantes.Count == 0 ? "Terminer ce travail" : "Refermer sans finir",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (reponse != MessageBoxResult.OK)
            {
                return;
            }

            FichierTravail.Effacer(carnet.Titre, _journal);

            Dire(
                "systeme",
                restantes.Count == 0
                    ? $"Travail « {carnet.Titre} » terminé, rangé dans Travaux\\Finis."
                    : $"Travail « {carnet.Titre} » refermé avec {restantes.Count} étape(s) non faite(s), "
                      + "rangé dans Travaux\\Finis.");

            Rafraichir();
        };

        boutons.Children.Add(refermer);
        corps.Children.Add(boutons);

        return corps;
    }

    /// <summary>Ouvre la fenêtre, ou ramène celle qui est déjà là.</summary>
    public static string Ouvrir(Action<string>? journal)
    {
        var deja = Application.Current.Windows.OfType<AssistantWindow>().FirstOrDefault();

        if (deja is not null)
        {
            deja.Activate();

            return "";
        }

        try
        {
            new AssistantWindow(journal).Show();

            return "";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"assistant panel could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return "Le panneau de l'assistant n'a pas pu être dessiné.";
        }
    }

    private void EnvoyerClic(object sender, RoutedEventArgs e) => Demander();

    /// <summary>Accepte le survol d'un fichier, refuse le reste.</summary>
    private void SurvolFichier(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Un fichier déposé devient son chemin, dans la phrase en cours.
    /// </summary>
    /// <remarks>
    /// C'est la seule valeur qu'un assistant ne peut pas deviner, et la plus pénible à taper. La
    /// consigne du modèle lui interdit d'inventer un chemin ; le déposer est la façon de le lui
    /// donner. Plusieurs fichiers d'un coup deviennent plusieurs chemins, un par ligne, parce
    /// qu'une phrase qui les enchaîne sans séparation n'est plus lisible par personne.
    /// </remarks>
    private void DepotFichier(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } chemins)
        {
            return;
        }

        Deposer(chemins);
        e.Handled = true;
    }

    /// <summary>Écrit des chemins dans la saisie, sans écraser ce qui s'y trouve.</summary>
    private void Deposer(IReadOnlyList<string> chemins)
    {
        var texte = string.Join(Environment.NewLine, chemins);

        if (Saisie.Text.Length > 0 && !Saisie.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            Saisie.AppendText(Environment.NewLine);
        }

        Saisie.AppendText(texte + Environment.NewLine);
        Saisie.CaretIndex = Saisie.Text.Length;
        Saisie.Focus();
    }

    /// <summary>
    /// Envoie une demande au modèle, et tient la fenêtre pendant qu'il travaille.
    /// </summary>
    /// <param name="dicte">
    /// Ce qu'un bouton de choix envoie à la place de la saisie. Null pour ce que l'utilisateur a
    /// tapé, ce qui est le cas ordinaire.
    /// </param>
    /// <param name="seul">
    /// Vrai pour cette demande-ci seulement, quand elle vient du bouton « Fais-le toi-même ». La
    /// case, elle, vaut pour toutes.
    /// </param>
    private async void Demander(string? dicte = null, bool seul = false)
    {
        var demande = (dicte ?? Saisie.Text).Trim();

        if (_occupe || demande.Length == 0)
        {
            return;
        }

        // Autonome par la case, par le bouton, ou parce qu'un travail est déjà en route.
        //
        // Ce dernier cas est le point 4 : un travail commencé se reprend seul. Redemander quel
        // outil employer au milieu d'un travail en cours reviendrait à faire rejouer à
        // l'utilisateur un choix qu'il a déjà fait, à chaque étape.
        var autonome = seul
            || Seul.IsChecked == true
            || FichierTravail.Lister(_journal).Any(c => c.Taches.Exists(t => !t.Faite));

        _occupe = true;

        if (dicte is null)
        {
            Saisie.Clear();
        }

        Dire("vous", demande);

        Travail.Visibility = Visibility.Visible;
        Envoyer.Visibility = Visibility.Collapsed;
        Arreter.Visibility = Visibility.Visible;
        Arreter.IsEnabled = true;

        _arret?.Dispose();
        _arret = new CancellationTokenSource();

        try
        {
            var journal = _journal;
            var outils = Disponibles(journal);

            // Le modèle met plusieurs secondes, et davantage s'il doit d'abord charger. Tenir cette
            // attente sur le fil d'affichage figerait la fenêtre — y compris la barre censée dire
            // que ça travaille.
            // Lu ici, sur le fil d'affichage, et non dans le Task.Run : une case a cocher ne se
            // consulte pas depuis un autre fil.
            var interactif = Interactif.IsChecked == true;

            var capacites = Capacites(outils, journal, autonome, interactif);

            // Note avant, compare apres : c'est ce qui dit si une recherche a eu lieu pendant ce
            // tour, quel que soit le chemin — instance ou navigateur — qui l'a moissonnee.
            _recolteAvant = SenSÉ.Tools.Assistant.RechercheWeb.Derniere;

            var reponse = await Task.Run(() => _agent.Repondre(
                demande,
                outils,
                capacites,
                (manifeste, reglages) => Lancer(manifeste, reglages, journal),
                message => Dispatcher.Invoke(() => Dire("systeme", message)),
                _arret.Token,
                autonome));

            Dire("assistant", reponse);

            // LA BIBLIOGRAPHIE EST ECRITE ICI, PAS PAR LE MODELE.
            //
            // Il rédigeait la sienne, et elle était fausse : le 9 septembre il a terminé par
            // « Sources : Le Monde [1], France Info [2], 20 Minutes [3-4], La Dépêche [5] » —
            // cinq entrées dont trois jamais employées, et une attribution inversée. C'est le seul
            // endroit du dispositif où une erreur est indétectable pour le lecteur : une
            // bibliographie a l'autorité de l'exactitude. Elle vient donc de la récolte, c'est-à-
            // dire de ce qui a réellement été ouvert et lu.
            if (SenSÉ.Tools.Assistant.RechercheWeb.Derniere is { } citees
                && !ReferenceEquals(citees, _recolteAvant))
            {
                Dire("systeme", SenSÉ.Tools.Assistant.RechercheWeb.Bibliographie(citees));
            }
        }
        catch (Exception exception)
        {
            _journal?.Invoke($"assistant failed: {exception.GetType().Name}: {exception.Message}");
            Dire("systeme", exception.Message);
        }
        finally
        {
            Travail.Visibility = Visibility.Collapsed;
            Arreter.Visibility = Visibility.Collapsed;
            Envoyer.Visibility = Visibility.Visible;
            _occupe = false;
            Saisie.Focus();
            Rafraichir();
        }
    }

    /// <summary>
    /// Choisit un fichier ou un dossier et l'écrit dans la conversation.
    /// </summary>
    /// <remarks>
    /// <b>Ce bouton remplace le glisser-déposer, qui est impossible ici.</b> SenSÉ exige
    /// l'administrateur — HidHide et ViGEmBus n'ont pas le choix — et WPF reçoit les fichiers par
    /// OLE, donc par COM. Entre deux niveaux d'intégrité, COM est bloqué, et aucun filtre de
    /// messages n'y change rien.
    ///
    /// <para>
    /// La mesure a tranché : les trois messages Windows sont autorisés, l'élévation est vraie,
    /// <c>AllowDrop</c> est vrai, et le dépôt échoue. Il ne s'agissait pas d'un réglage à trouver.
    /// Deux corrections ont été livrées sur cette hypothèse avant que la mesure ne l'écarte.
    /// </para>
    ///
    /// <para>
    /// Le sélecteur accepte aussi un dossier : la boîte de dialogue de Windows le permet depuis
    /// .NET 8, et c'est ce que l'utilisateur veut la moitié du temps — un dossier d'images à animer,
    /// à monter ou à trier.
    /// </para>
    /// </remarks>
    private void ChoisirClic(object sender, RoutedEventArgs e)
    {
        var boite = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un fichier à montrer à l'assistant",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Tous les fichiers|*.*",
        };

        if (boite.ShowDialog(this) == true && boite.FileNames.Length > 0)
        {
            Deposer(boite.FileNames);
        }
    }

    /// <summary>Choisit un dossier plutôt qu'un fichier.</summary>
    private void ChoisirDossierClic(object sender, RoutedEventArgs e)
    {
        var boite = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choisir un dossier à montrer à l'assistant",
        };

        if (boite.ShowDialog(this) == true && boite.FolderName.Length > 0)
        {
            Deposer([boite.FolderName]);
        }
    }

    /// <summary>Entrée dans le champ des tâches : le geste attendu est d'ajouter.</summary>
    private void TacheTouche(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confier();
        }
    }

    private void AjouterClic(object sender, RoutedEventArgs e) => Confier();

    /// <summary>
    /// Ajoute à « Mes tâches » ce que l'utilisateur vient d'écrire.
    /// </summary>
    /// <remarks>
    /// <b>Un carnet qui appartient à l'utilisateur, distinct de ceux que l'assistant se note.</b>
    /// La zone ne savait que montrer : tout ce qui s'y trouvait venait du modèle. Or une tâche
    /// dictée dans la conversation disparaît au défilement, et l'assistant la traite comme une
    /// demande à satisfaire sur-le-champ — alors que « pense à agrandir les photos du mariage »
    /// n'appelle pas une action immédiate, seulement de ne pas être oublié.
    ///
    /// <para>
    /// Ce carnet-là est accepté d'office. L'accord protège l'utilisateur d'un plan que le modèle
    /// aurait mal compris ; demander à quelqu'un d'approuver ce qu'il vient d'écrire lui-même
    /// n'aurait servi qu'à lui apprendre à cliquer sans lire.
    /// </para>
    /// </remarks>
    /// <summary>Cache l'invite dès qu'on écrit, la remet quand le champ redevient vide.</summary>
    private void TacheChangee(object sender, TextChangedEventArgs e)
        => Invite.Visibility = Tache.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Confier()
    {
        var texte = Tache.Text.Trim();

        // Un bouton ne doit jamais rester muet.
        //
        // Il sortait ici sans rien dire quand le champ était vide. L'utilisateur a pressé
        // « Ajouter » en attendant qu'il ouvre une fenêtre, n'a rien vu venir, et en a conclu que
        // l'ajout de travaux n'existait pas. Le champ était pourtant à côté — mais un rectangle
        // sombre sans invite ne se lit pas comme un endroit où écrire.
        if (texte.Length == 0)
        {
            Tache.Focus();
            Dire("systeme", "Écrivez la tâche dans le champ, puis pressez Ajouter ou Entrée.");

            return;
        }

        // Les étapes déjà là sont reprises telles quelles : Noter réécrit le carnet entier, et
        // s'en passer effacerait la liste à chaque ajout.
        var etapes = FichierTravail.Lire(Miennes, _journal) is { } carnet
            ? carnet.Taches.Select(t => t.Texte).ToList()
            : [];

        etapes.Add(texte);

        FichierTravail.Noter(Miennes, etapes, _journal);

        Tache.Clear();
        Dire("systeme", $"Tâche ajoutée : {texte}");
        Rafraichir();
    }

    /// <summary>Le carnet des tâches que l'utilisateur donne lui-même.</summary>
    private const string Miennes = "Mes tâches";

    /// <summary>Demande l'arrêt de l'enchaînement en cours.</summary>
    /// <remarks>
    /// <b>Ce que le bouton arrête, et ce qu'il n'arrête pas.</b> Il coupe l'enchaînement : le
    /// modèle ne repart pas pour un tour, et les outils qu'il avait demandés dans la foulée ne sont
    /// pas lancés. Ce qui est déjà parti au générateur continue — cinq minutes de carte graphique ne
    /// se rappellent pas — et le dire est plus honnête que de laisser croire à un arrêt total.
    /// </remarks>
    private void ArreterClic(object sender, RoutedEventArgs e)
    {
        _arret?.Cancel();

        Arreter.IsEnabled = false;
        Dire("systeme", "Arrêt demandé. Une génération déjà lancée va jusqu'à son terme.");
    }

    /// <summary>
    /// Ce que l'assistant sait faire en plus de lancer un outil.
    /// </summary>
    /// <remarks>
    /// Trois gestes qu'aucun manifeste ne peut décrire, parce qu'ils appartiennent à
    /// l'environnement : poser un panneau devant l'utilisateur, y régler une option sous ses yeux,
    /// ouvrir une fenêtre du produit. C'est la différence entre un lanceur et un assistant.
    ///
    /// <para>
    /// Chacun reste borné : les valeurs admises sont énumérées ici — les identifiants des outils
    /// installés, les fenêtres qui existent — et le modèle ne peut rien nommer d'autre. Tout passe
    /// par le fil d'affichage, sans quoi toucher à une fenêtre depuis le fil du modèle ferait
    /// tomber l'application.
    /// </para>
    /// </remarks>
    /// <param name="autonome">
    /// Vrai quand l'assistant agit seul. <c>proposer_choix</c> n'est alors pas déclarée : une
    /// capacité offerte est une capacité employée, et un assistant à qui l'on a demandé de faire
    /// seul qui pose quand même la question n'a pas obéi à moitié — il n'a pas obéi.
    /// </param>
    private IReadOnlyList<AssistantLocal.Capacite> Capacites(
        IReadOnlyList<PluginManifest> outils,
        Action<string>? journal,
        bool autonome = false,
        bool interactif = false)
    {
        // Les outils compilés — calculatrice, capture, presse-papiers — n'ont pas de manifeste :
        // ils sont dans le produit. Les omettre revenait à cacher à l'assistant la moitié de ce que
        // l'utilisateur voit dans sa grille, et à lui faire répondre « je ne sais pas faire » à
        // propos d'une tuile posée sous ses yeux.
        var compiles = ToolRegistry.Builtin;

        var identifiants = outils.Select(o => o.Id)
            .Concat(compiles.Select(t => t.Id))
            .ToList();

        var quiEstQui = string.Join(", ",
            outils.Select(o => $"{o.Id} = {o.Name}")
                .Concat(compiles.Select(t => $"{t.Id} = {t.Label}")));

        var capacites = new List<AssistantLocal.Capacite>
        {
            new AssistantLocal.Capacite(
                "ouvrir_outil",
                "Ouvre un outil devant l'utilisateur : le panneau de réglages pour ceux qui en ont, "
                + "la fenêtre de l'outil pour les autres. À employer quand il veut voir ou choisir "
                + "lui-même, plutôt que de lancer un travail dans son dos.",
                [new AssistantLocal.Parametre("outil", $"L'outil à ouvrir. {quiEstQui}", identifiants)],
                reglages => Dispatcher.Invoke(() =>
                {
                    var demande = Valeur(reglages, "outil");

                    var choisi = outils.FirstOrDefault(o => string.Equals(
                        o.Id, demande, StringComparison.OrdinalIgnoreCase));

                    if (choisi is not null)
                    {
                        PluginTools.Ouvrir(choisi, journal);

                        return $"« {choisi.Name} » ouvert.";
                    }

                    var compile = compiles.FirstOrDefault(t => string.Equals(
                        t.Id, demande, StringComparison.OrdinalIgnoreCase));

                    if (compile is null)
                    {
                        return $"Outil inconnu : {demande}";
                    }

                    // La fenêtre d'environnement, pas celle de l'assistant : la capture doit
                    // s'effacer devant l'écran à capturer, et c'est le centre de contrôle qui gêne.
                    ToolRegistry.Launch(compile, Application.Current.MainWindow ?? this);

                    return $"« {compile.Label} » ouvert.";
                })),

            new AssistantLocal.Capacite(
                "regler_option",
                "Change un réglage dans le panneau déjà ouvert d'un outil. L'utilisateur voit le "
                + "changement se produire. Le panneau doit être ouvert.",
                [
                    new AssistantLocal.Parametre("outil", "L'outil dont le panneau est ouvert.", identifiants),
                    new AssistantLocal.Parametre("reglage", "L'identifiant du réglage.", []),
                    new AssistantLocal.Parametre("valeur", "La valeur à poser.", []),
                ],
                reglages => Dispatcher.Invoke(() => PluginPanelWindow.Regler(
                    Valeur(reglages, "outil"),
                    Valeur(reglages, "reglage"),
                    Valeur(reglages, "valeur")))),

            new AssistantLocal.Capacite(
                "calculer",
                "Évalue une expression avec la calculatrice de SenSÉ : opérations, "
                + "parenthèses, puissances, racines, trigonométrie, factorielle.",
                [
                    new AssistantLocal.Parametre(
                        "expression", "L'expression à évaluer, par exemple « (12+5)*3 » ou « sqrt(2) ».", []),
                ],
                reglages => Calculer(Valeur(reglages, "expression"))),

            new AssistantLocal.Capacite(
                "ouvrir_fenetre",
                "Ouvre une fenêtre de SenSÉ : les réglages du produit, le moniteur d'activité "
                + "qui montre ce qui tourne, ou les jeux de modèles — à ouvrir quand un modèle "
                + "manque pour ce que l'utilisateur demande.",
                [
                    new AssistantLocal.Parametre(
                        "fenetre", "La fenêtre à ouvrir.", ["reglages", "activite", "modeles"]),
                ],
                reglages => Dispatcher.Invoke(() => Fenetre(Valeur(reglages, "fenetre"), journal))),
        };

        // Le choix, avant d'agir : montrer les routes plutôt que d'en prendre une.
        //
        // Une même demande — « une vidéo d'après un texte » — se sert de plusieurs façons, et
        // laquelle convient n'appartient pas au modèle. Mesuré le 24 août : il a répondu par un
        // cours en deux étapes là où l'utilisateur attendait qu'on lui montre ses outils, puis a
        // enchaîné vingt appels sans jamais demander s'il devait le faire lui-même.
        //
        // Déclarée seulement quand l'assistant n'est pas autonome : voir le paramètre.
        if (!autonome)
        {
            capacites.Add(new AssistantLocal.Capacite(
                "proposer_choix",
                "Montre à l'utilisateur les routes possibles pour sa demande, et attends qu'il "
                + "choisisse. À appeler AVANT d'agir sur une demande neuve, jamais au milieu d'un "
                + "travail déjà accepté. N'écris pas toi-même l'option « fais-le toi-même » : "
                + "l'hôte l'ajoute. Après cet appel, ne dis rien de plus — la main est à "
                + "l'utilisateur.",
                [
                    new AssistantLocal.Parametre(
                        "question", "Ce qui est à décider, en une phrase courte.", []),
                    new AssistantLocal.Parametre(
                        "options",
                        "Les routes, séparées par un point-virgule. Deux à quatre, chacune nommant "
                        + "l'outil et ce qu'elle donne — « Créer une image, puis l'animer ».",
                        []),
                ],
                reglages => Dispatcher.Invoke(() => Proposer(
                    Valeur(reglages, "question"),
                    Valeur(reglages, "options"))),
                Interne: true));
        }

        // Le carnet : écrire ailleurs que dans sa tête.
        //
        // Le modèle travaille sur huit mille jetons, partagés avec la conversation, la déclaration
        // de tous les outils et son raisonnement — et une image lui en coûte mille. Une demande en
        // dix étapes n'y tient pas : au huitième tour, le début a disparu. Ces quatre capacités lui
        // donnent un endroit où poser le plan et le relire.
        capacites.AddRange(Carnet(journal));

        // ECRIRE UN DOCUMENT NE DEMANDE PAS DE FENETRE.
        //
        // Le 9 septembre 2026, a « ecris trois paragraphes et enregistre-les en Word », le modele
        // a lance l'Editeur Texte, tente d'y poser le contenu, recu « le panneau n'est pas
        // ouvert », rouvert l'outil, retente, note un carnet, coche une etape qu'il n'avait pas
        // faite, rempli son contexte et rendu la main. Quatre fenetres a l'ecran, zero document
        // sur le disque.
        //
        // La cause n'etait pas le modele mais le chemin : ecrire passait forcement par une
        // interface graphique, alors que la demande voulait un fichier.
        capacites.Add(new AssistantLocal.Capacite(
            "document_ecrire",
            "Ecrit un document sur le disque SANS ouvrir de fenetre : Word (.docx), .html, .md ou "
            + ".txt. C'EST LA FAÇON D'ECRIRE UN DOCUMENT — n'ouvre pas l'editeur pour cela. Rend "
            + "le chemin du fichier. Une ligne vide separe deux paragraphes.",
            [
                new AssistantLocal.Parametre(
                    "titre", "Le titre du document. Il l'ouvre et le nomme.", []),
                new AssistantLocal.Parametre(
                    "texte", "Le corps entier, redige. Une ligne vide entre deux paragraphes.", []),
                new AssistantLocal.Parametre(
                    "format", "docx (defaut), html, md, txt — ou pdf, odt, rtf si LibreOffice est la.",
                    ["docx", "html", "md", "txt", "pdf", "odt", "rtf"]),
            ],
            reglages => Rediger(
                Valeur(reglages, "titre"),
                Valeur(reglages, "texte"),
                Valeur(reglages, "format"),
                journal)));

        // COMPOSER UN GRAPHE EST LE TRAVAIL DE L'ATELIER, PLUS CELUI DU GENERATEUR.
        //
        // Ces capacités arrivaient ici en bloc — flux_catalogue, flux_noeud, flux_verifier,
        // flux_lancer — et n'avaient de sens qu'ensemble : chercher sans pouvoir lire le détail
        // d'un nœud ne donne que des noms, lire sans pouvoir éprouver laisse le modèle deviner
        // s'il a bien câblé, éprouver sans pouvoir lancer ne produit rien. Le raisonnement tient
        // toujours ; c'est l'établi qui a changé.
        //
        // Elles sont déclarées par AssistantAtelier, avec les autres verbes de l'Atelier, parce
        // qu'un modèle qui apprend deux vocabulaires pour un seul geste en choisit un au hasard —
        // c'est exactement ce qui vient de se produire avec les deux verbes de recherche web.
        //
        // Le message d'absence est retiré avec elles. « Le générateur n'est pas installé » laissait
        // entendre qu'il pourrait l'être, et donc qu'il manquait quelque chose : il ne manque rien,
        // il a été remplacé.

        // La recherche n'est déclarée que si elle peut aboutir. Une capacité qui échoue à chaque
        // appel est pire que son absence : le modèle la voit dans sa liste, la juge pertinente, la
        // rappelle, et la conversation tourne en rond — c'est exactement ce qu'avait produit la
        // capacité qui déchargeait le générateur. Ce qu'il ne peut pas nommer, il ne peut pas y
        // insister.
        var web = SearchPolicyStore.Load(journal).Web.Effective;

        // DEUX VERBES DE RECHERCHE ETAIENT DECLARES, ET LE MODELE PRENAIT LE MOINS BON.
        //
        // « recherche_web », de la famille AssistantRecherche, et « chercher_web », declare ici,
        // faisaient la meme chose par deux chemins de qualite differente. Mesure du 9 septembre
        // 2026 : a « fais-moi une recherche sur les actualites de nvidia », le modele a appele
        // « recherche_web » — celui qui rendait une liste de liens sans auteur, sans date de
        // publication et sans bibliographie. Toute la tracabilite passait a la trappe parce qu'un
        // second verbe existait.
        //
        // « recherche_web » passe desormais par RechercheWeb comme tout le reste. Il n'y a donc
        // plus rien a declarer ici quand une instance repond : un seul verbe, un seul rendu.
        if (web.Provider == WebSearchProvider.Instance
            && !string.IsNullOrWhiteSpace(web.InstanceUrl))
        {
            journal?.Invoke("assistant: recherche web par l'instance configurée.");

            capacites.Add(Consigner(journal));
        }
        else if (SenSÉ.Tools.Assistant.AssistantCdp.Pret)
        {
            // LE NAVIGATEUR EST LE REPLI, PAS LE REMPLACANT.
            //
            // Le modele tenait deja cdp_navigate et cdp_eval — donc les moyens de chercher — et
            // repondait « je ne peux pas faire de recherche web » : personne ne lui avait dit que
            // ces verbes en etaient un. Le nom et la description ne changent pas d'un chemin a
            // l'autre, expres : ce qui change est la plomberie, pas le vocabulaire du modele.
            //
            // Une instance reste preferable quand il y en a une — elle interroge un moteur qui
            // veut bien etre interroge, la ou un navigateur pilote se heurte aux murs de
            // consentement. Mais un repli qui marche vaut mieux qu'une capacite absente.
            journal?.Invoke("assistant: recherche web par le navigateur intégré (aucune instance configurée).");

            capacites.Add(new AssistantLocal.Capacite(
                // LE MEME NOM QUE L'AUTRE CHEMIN. Un modele qui apprend deux verbes pour une seule
                // chose en choisit un au hasard ; ici il n'y en a qu'un, et ce qui change derriere
                // — instance ou navigateur — ne le regarde pas.
                "recherche_web",
                "Cherche sur le web et rend les extraits trouvés AVEC leurs sources. À employer "
                + "pour tout ce qui n'est pas dans ce que tu sais déjà : une actualité, la version "
                + "d'un logiciel, un prix, un fait daté. Une question par appel, en mots-clés "
                + "plutôt qu'en phrase. Cite ensuite chaque affirmation par son libellé.",
                [
                    new AssistantLocal.Parametre(
                        "sujet", "Ce qu'il faut chercher, en quelques mots-clés.", []),
                ],
                reglages => ChercherParLeNavigateur(Valeur(reglages, "sujet"), journal)));

            capacites.Add(Consigner(journal));
        }
        else
        {
            journal?.Invoke(
                "assistant: pas de recherche web, aucune instance configurée dans les réglages.");
        }

        // Memoire 3 niveaux, branchee sur le disque.
        capacites.AddRange(AssistantMemoire.Creer());

        // Confier a un specialiste, seulement s'il y en a un.
        //
        // La voie « dialogue » est retiree de la liste : c'est celle qui parle en ce moment, et se
        // confier une tache a soi-meme est un tour perdu — le travers exact que ce produit a deja
        // vu avec les capacites qui ne pouvaient pas repondre.
        var specialistes = AssistantLocal.Specialistes(journal);

        if (specialistes.Count > 0)
        {
            capacites.Add(new AssistantLocal.Capacite(
                "confier_specialiste",
                "Confie une sous-tâche au modèle spécialisé qui la fera mieux ou plus vite que toi. "
                + "Donne-lui une demande FORMULÉE EN ENTIER : il ne voit ni la conversation, ni les "
                + "carnets, ni ce que tu sais — seulement la phrase que tu lui écris. Il rend son "
                + "texte, que tu reprends ensuite à ton compte. À employer quand la sous-tâche est "
                + "nette et se suffit à elle-même, pas pour lui déléguer ton jugement.",
                [
                    new AssistantLocal.Parametre(
                        "voie", "Le spécialiste à qui confier la sous-tâche.", specialistes),
                    new AssistantLocal.Parametre(
                        "demande", "La sous-tâche, écrite en entier et compréhensible seule.", []),
                ],
                reglages => AssistantLocal.Confier(
                    Valeur(reglages, "voie"),
                    Valeur(reglages, "demande"),
                    journal)));
        }
        else
        {
            journal?.Invoke("assistant: aucun spécialiste déclaré, la délégation n'est pas proposée.");
        }

        // Le pilotage d'applications reelles, seulement s'il a ete arme.
        //
        // Ces deux familles agissent sur ce que l'utilisateur est en train de faire : prendre le
        // premier plan, taper au clavier d'une autre fenetre, deplacer la souris. Elles sont aussi
        // celles qui echouent le plus — le premier plan refuse par Windows, la fenetre trouvee mais
        // pas l'endroit ou ecrire — et un modele qui les a sous la main y revient tour apres tour.
        //
        // Non declarees plutot que refusees : on ne s'entete pas sur ce qu'on ignore, et leur
        // declaration rend sa place a la conversation. C'est la meme regle que pour les recherches
        // distantes non configurees.
        if (interactif)
        {
            capacites.AddRange(SenSÉ.Tools.Assistant.AssistantDebug.Creer());
            capacites.AddRange(AssistantSaisie.Creer());
        }

        // Le navigateur reste, arme ou non : il pilote une page par le protocole, sans toucher au
        // clavier, a la souris ni au premier plan de l'utilisateur. C'est precisement l'alternative
        // au mode interactif, pas un morceau de celui-ci.
        capacites.AddRange(SenSÉ.Tools.Assistant.AssistantCdp.Creer());
        capacites.AddRange(AssistantRecherche.Creer(journal));

        // L'Atelier : un subprocess HTTP qui demarre avec l'environnement (cf. ServeurAtelier).
        // Declare comme AssistantCdp : disponible que la fenetre soit en mode interactif ou non,
        // parce que ses Capacite n'agissent pas sur la machine de l'utilisateur. Chargees
        // seulement si le client HTTP a ete initialise (Atelier lance et Demarrer appele) :
        // sinon, des Capacite qui echouent a chaque appel font tourner le modele en rond.
        if (!string.IsNullOrEmpty(SenSÉ.Tools.Assistant.AssistantAtelier.UrlBase))
        {
            capacites.AddRange(SenSÉ.Tools.Assistant.AssistantAtelier.Creer());
        }
        else
        {
            journal?.Invoke("assistant: pas de Capacite Atelier, l'Atelier n'est pas demarre (ServeurAtelier.Demarrer pas appele).");
        }

        return capacites;
    }

    /// <summary>
    /// Le carnet de l'assistant : noter, relire, cocher, refermer.
    /// </summary>
    /// <remarks>
    /// <b>Ce n'est pas l'assistant qui décide que c'est fini.</b> Un modèle qui déclare sa tâche
    /// accomplie se trompe avec aplomb : celui-ci a déjà annoncé un générateur lancé qui ne l'était
    /// pas. Refermer un carnet exige donc que l'utilisateur l'ait dit, et la déclaration de la
    /// capacité le lui répète — c'est tout ce qu'on peut faire tenir dans une consigne, mais le
    /// carnet oublié, lui, disparaîtra seul au bout d'un mois.
    /// </remarks>
    private IReadOnlyList<AssistantLocal.Capacite> Carnet(Action<string>? journal)
        =>
        [
            new AssistantLocal.Capacite(
                "travail_noter",
                "Écris ton plan dans un carnet, pour ne pas avoir à le retenir. À n'employer que "
                + "lorsque la demande déborde d'un simple enchaînement : plusieurs outils, "
                + "plusieurs fichiers, ou une demande étoffée au fil de la discussion. Pour deux ou "
                + "trois gestes, fais-les au lieu de les écrire. Le carnet couvre TOUT ce qui a été "
                + "demandé depuis le début du fil, pas seulement le dernier message. Réécrire un "
                + "carnet existant garde ce qui était déjà coché — c'est ainsi qu'on le corrige "
                + "quand l'utilisateur demande un changement.",
                [
                    new AssistantLocal.Parametre(
                        "titre", "Ce que l'utilisateur a demandé, en quelques mots.", []),
                    new AssistantLocal.Parametre(
                        "taches",
                        "Les étapes, une par ligne ou séparées par un point-virgule.",
                        []),
                ],
                reglages =>
                {
                    var note = FichierTravail.Noter(
                        Valeur(reglages, "titre"),
                        FichierTravail.Decouper(Valeur(reglages, "taches")),
                        journal);

                    Rafraichir();

                    // Plus d'accord à attendre : le plan est noté et le travail commence.
                    //
                    // « Énonce ce plan et attends son accord » faisait redemander la permission
                    // d'exécuter une demande que l'utilisateur venait de formuler. Il reste libre
                    // de corriger en le disant — la zone des travaux le rappelle — mais le défaut
                    // est d'avancer, pas d'attendre.
                    return Resumer(note) + "\nCommence maintenant par la première étape.";
                },
                Interne: true),

            // Il n'y a pas de « travail_relire ».
            //
            // Elle a existé une heure. Les carnets ouverts sont désormais écrits dans la consigne à
            // chaque tour : le modèle les a sous les yeux sans rien demander. Garder la capacité
            // aurait coûté sa déclaration dans un contexte de huit mille jetons, et invité un
            // modèle de quatre milliards de paramètres à dépenser un tour entier pour relire ce
            // qu'il venait de lire.
            new AssistantLocal.Capacite(
                "travail_retenir",
                "Retiens un fait établi, pour ne pas avoir à le redemander : ce que l'utilisateur "
                + "a dit une fois, le nom exact d'un nœud que tu as fini par trouver, une valeur "
                + "choisie. À appeler dès que tu apprends quelque chose que tu regretterais "
                + "d'oublier — ce qui est retenu survit à ton contexte, le reste non.",
                [
                    new AssistantLocal.Parametre("titre", "Le titre du carnet.", []),
                    new AssistantLocal.Parametre(
                        "faits",
                        "Les faits à retenir, un par ligne ou séparés par un point-virgule.",
                        []),
                ],
                reglages =>
                {
                    var titre = Valeur(reglages, "titre");

                    var retenu = FichierTravail.Retenir(
                        titre, FichierTravail.Decouper(Valeur(reglages, "faits")), journal);

                    Rafraichir();

                    // L'échec dit quoi faire, comme pour l'accord : « aucun carnet » sans suite
                    // laissait le modèle retenter le même appel jusqu'à épuiser ses tours.
                    return retenu is null
                        ? $"Aucun carnet « {titre} » : ouvre-le d'abord avec travail_noter, "
                          + "puis retiens."
                        : Resumer(retenu);
                },
                Interne: true),

            new AssistantLocal.Capacite(
                "travail_cocher",
                "Marque une étape comme faite, une fois qu'elle l'est réellement — pas quand tu "
                + "l'as lancée.",
                [
                    new AssistantLocal.Parametre("titre", "Le titre du carnet.", []),
                    new AssistantLocal.Parametre("tache", "L'étape achevée.", []),
                ],
                reglages =>
                {
                    var titre = Valeur(reglages, "titre");
                    var tache = Valeur(reglages, "tache");

                    var coche = FichierTravail.Cocher(titre, tache, journal);

                    Rafraichir();

                    return coche is null ? Manque(titre, tache, journal) : Resumer(coche);
                },
                Interne: true),

            new AssistantLocal.Capacite(
                "travail_terminer",
                "Referme un carnet et le range dans Travaux\\Finis. À n'appeler QUE lorsque "
                + "l'utilisateur a confirmé que le travail lui convient. Ne l'appelle jamais de ta "
                + "propre initiative, même si toutes les étapes sont cochées : c'est lui qui juge "
                + "du résultat, pas toi. Refusé tant qu'une étape reste à faire.",
                [new AssistantLocal.Parametre("titre", "Le titre du carnet.", [])],
                reglages =>
                {
                    var titre = Valeur(reglages, "titre");

                    // Un carnet ne se referme pas sur des étapes non cochées.
                    //
                    // Le 7 septembre 2026, un travail s'est rangé dans Finis alors que sa dernière
                    // étape — ouvrir le dossier — n'avait pas été faite. Rien ne l'en empêchait :
                    // le verbe effaçait sans regarder. Le carnet devient alors un compte rendu qui
                    // se contredit, coché à moitié et pourtant clos, et la seule trace de ce qui
                    // restait à faire disparaît avec lui.
                    //
                    // Le refus nomme ce qui manque, pour être une consigne et non un mur : la
                    // réponse dit quoi faire ensuite, cocher ou reprendre.
                    if (FichierTravail.Lire(titre, journal) is { } carnet)
                    {
                        var restantes = FichierTravail.Restantes(carnet);

                        if (restantes.Count > 0)
                        {
                            return $"Refusé : « {titre} » a encore {restantes.Count} étape(s) non "
                                + "faite(s) : « " + string.Join(" », « ", restantes) + " ». "
                                + "Termine-les, ou coche-les avec travail_cocher si elles sont "
                                + "faites. Si tu ne peux pas les finir, dis-le à l'utilisateur et "
                                + "laisse-lui refermer le carnet lui-même.";
                        }
                    }

                    var efface = FichierTravail.Effacer(titre, journal);

                    Rafraichir();

                    return efface
                        ? "Carnet refermé, rangé dans Travaux\\Finis."
                        : "Aucun carnet de ce nom.";
                },
                Interne: true),
        ];

    private static string Resumer(SenSÉ.Tools.Assistant.Travail travail)
        => FichierTravail.Resumer(travail);

    /// <summary>
    /// Dit ce qui manque quand un cochage échoue : le carnet, ou l'étape.
    /// </summary>
    /// <remarks>
    /// <b>« Carnet ou étape introuvable » ne permettait de corriger ni l'un ni l'autre.</b> Le
    /// modèle ne savait pas s'il s'était trompé de titre ou d'étape, ne pouvait donc pas choisir
    /// quoi changer, et le journal ne le disait pas non plus. Nommer ce qui existe réellement lui
    /// rend le choix possible — et rend la panne lisible pour qui relit la session.
    /// </remarks>
    private static string Manque(string titre, string tache, Action<string>? journal)
    {
        if (FichierTravail.Lire(titre, journal) is not { } carnet)
        {
            var ouverts = FichierTravail.Lister(journal);

            return ouverts.Count == 0
                ? "Aucun travail ouvert. Ouvre-en un avec travail_noter avant de cocher."
                : $"Pas de carnet « {titre} ». Carnets ouverts : "
                  + string.Join(" / ", ouverts.Select(c => c.Titre))
                  + ". Reprends le titre exactement.";
        }

        return $"Le carnet « {carnet.Titre} » ne porte pas d'étape « {tache} ». Ses étapes : "
            + string.Join(" / ", carnet.Taches.Select(t => t.Texte))
            + ". Recopie le début de l'une d'elles.";
    }

    private static string Carnets(Action<string>? journal)
    {
        var carnets = FichierTravail.Lister(journal);

        if (carnets.Count == 0)
        {
            return "Aucun carnet en cours.";
        }

        return string.Join(
            "\n",
            carnets.Select(FichierTravail.Resumer));
    }

    private static string Composer(string flux, bool soumettre, Action<string>? journal)
        => flux.Trim().Length == 0
            ? "Donne le flux à juger."
            : SenSÉ.Tools.Generation.FluxTravail.Composer(flux, soumettre, journal);

    /// <summary>Nombre de résultats donnés au modèle.</summary>
    /// <remarks>
    /// Le modèle travaille sur huit mille jetons de contexte, partagés avec la conversation, la
    /// déclaration de tous les outils et son propre raisonnement. L'instance en renvoie jusqu'à
    /// vingt-cinq ; les lui donner tous remplirait sa fenêtre avec des pages qu'il ne lira pas et
    /// lui ferait oublier la question posée. Cinq répondent à une question factuelle.
    /// </remarks>
    private const int Resultats = 5;

    /// <summary>Longueur au-delà de laquelle un extrait est coupé.</summary>
    private const int Extrait = 240;

    /// <summary>Cherche sans ouvrir de fenêtre.</summary>
    /// <remarks>
    /// <b>Le navigateur n'est plus le chemin, il est le secours.</b> Mesuré le 9 septembre 2026 :
    /// la façade du moteur rend dix résultats en 0,65 s sur une simple requête HTTP, et trois pages
    /// d'article sur quatre répondent de même, signature comprise. Ce que cet ordre achète n'est pas
    /// de la vitesse mais de la discrétion — le cas courant ne fait plus apparaître de fenêtre à
    /// l'écran. Un service rendu à l'utilisateur n'a pas à l'interrompre pour s'exécuter.
    ///
    /// <para>
    /// Le secours n'est offert que si mcp-cdp écoute déjà : sans lui la recherche marche quand
    /// même, les pages récalcitrantes gardant l'aperçu du moteur.
    /// </para>
    /// </remarks>
    private string ChercherParLeNavigateur(string sujet, Action<string>? journal)
    {
        var recolte = SenSÉ.Tools.Assistant.RechercheWeb.Moissonner(
            sujet,
            SenSÉ.Tools.Assistant.RechercheWeb.ParHttp,
            SenSÉ.Tools.Assistant.AssistantCdp.Pret ? ParLeNavigateur : null,
            journal);

        return SenSÉ.Tools.Assistant.RechercheWeb.Rediger(recolte);
    }

    /// <summary>La récolte d'avant ce tour, pour savoir si une recherche a eu lieu pendant.</summary>
    /// <remarks>
    /// <b>Comparée plutôt que signalée par un drapeau.</b> La recherche peut être moissonnée par
    /// l'instance — dans <c>AssistantRecherche</c>, ailleurs — ou par le navigateur ici : un
    /// drapeau posé à un seul de ces endroits manquerait l'autre, et la bibliographie ne
    /// s'afficherait que pour la moitié des recherches. La récolte, elle, est la même quel que
    /// soit le chemin.
    /// </remarks>
    private SenSÉ.Tools.Assistant.Recolte? _recolteAvant;

    /// <summary>Le secours : la page rendue par le navigateur, quand HTTP n'a pas suffi.</summary>
    /// <remarks>
    /// On demande à la page son HTML plutôt que son texte, pour que l'extraction soit la MÊME des
    /// deux côtés. Deux extractions parallèles finiraient par diverger, et une source citée
    /// différemment selon le chemin qui l'a lue est une trace qui ment sur elle-même.
    /// </remarks>
    private static SenSÉ.Tools.Assistant.Lecture? ParLeNavigateur(string url)
    {
        if (SenSÉ.Tools.Assistant.AssistantCdp.Naviguer(url).Length > 0)
        {
            return null;
        }

        var rendu = SenSÉ.Tools.Assistant.AssistantCdp.Evaluer("document.documentElement.outerHTML");

        if (rendu.StartsWith("mcp-cdp", StringComparison.Ordinal))
        {
            return null;
        }

        // Le CDP rend une chaine JSON : on la deballe avant de l'analyser comme du HTML.
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(rendu) is System.Text.Json.Nodes.JsonValue valeur
                && valeur.TryGetValue<string>(out var html))
            {
                rendu = html;
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return SenSÉ.Tools.Assistant.RechercheWeb.LireLeHtml(rendu);
    }

    /// <summary>La capacité qui écrit la dernière recherche dans un dossier daté.</summary>
    /// <remarks>
    /// Déclarée des deux côtés — instance ou navigateur — parce qu'elle ne dépend pas du chemin
    /// qui a moissonné, seulement de ce qui a été récolté.
    /// </remarks>
    private static AssistantLocal.Capacite Consigner(Action<string>? journal)
        => new(
            "dossier_recherche",
            "Écrit la DERNIÈRE recherche dans un dossier daté : ta synthèse, les pages telles "
            + "qu'elles ont été lues, et un manifeste. À employer quand l'utilisateur veut garder, "
            + "imprimer ou convertir le résultat plutôt que le lire dans le fil. Rend le chemin "
            + "du dossier.",
            [
                new AssistantLocal.Parametre(
                    "synthese", "Ta réponse rédigée, avec ses renvois entre crochets.", []),
            ],
            reglages => Consigner(
                reglages.TryGetValue("synthese", out var lu) ? lu.Trim() : "", journal));

    /// <summary>Écrit la dernière récolte et la synthèse du modèle dans un dossier daté.</summary>
    private static string Consigner(string synthese, Action<string>? journal)
    {
        if (SenSÉ.Tools.Assistant.RechercheWeb.Derniere is not { } recolte)
        {
            return "Rien à consigner : aucune recherche n'a encore été faite. "
                + "Appelle recherche_web d'abord.";
        }

        try
        {
            var dossier = SenSÉ.Tools.Assistant.RechercheWeb.Dossier(recolte, synthese, journal);

            return $"Dossier écrit : {dossier}\n"
                + "Il contient RECHERCHE.md, RECHERCHE.html, sources.json et les pages conservées. "
                + "Pour un PDF ou un Word, lance l'outil « convertir-document » sur le "
                + "RECHERCHE.html de ce dossier.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal?.Invoke($"dossier de recherche: {exception.GetType().Name}: {exception.Message}");

            return "Le dossier n'a pas pu être écrit : " + exception.Message;
        }
    }

    /// <summary>Écrit un document, et rend au modèle un chemin plutôt qu'une promesse.</summary>
    /// <remarks>
    /// Le chemin est rendu en entier, et c'est ce qui permet d'enchaîner : <c>FichierProduit</c>
    /// le reconnaît dans la phrase, donc l'outil suivant peut le reprendre tel quel — le convertir,
    /// l'ouvrir, le joindre.
    /// </remarks>
    private static string Rediger(string titre, string texte, string format, Action<string>? journal)
    {
        var rendu = SenSÉ.Tools.Documents.Redaction.Ecrire(titre, texte, format, journal);

        return rendu.Reussi
            ? $"Document écrit : {rendu.Chemin}"
            : rendu.Probleme;
    }

    private static string Valeur(IReadOnlyDictionary<string, string> reglages, string nom)
        => reglages.TryGetValue(nom, out var lu) ? lu.Trim() : "";

    /// <summary>
    /// Calcule avec l'évaluateur du produit, jamais de tête.
    /// </summary>
    /// <remarks>
    /// Un modèle de langage se trompe en arithmétique, et se trompe avec aplomb. La calculatrice
    /// de SenSÉ, elle, est testée. Lui déléguer le calcul rend la réponse vérifiable — et c'est
    /// exactement ce que « utiliser la calculatrice » veut dire, par opposition à l'ouvrir.
    /// </remarks>
    private static string Calculer(string expression)
    {
        var resultat = SenSÉ.Tools.Calculator.ExpressionEvaluator.Evaluate(expression);

        return resultat.Error is { Length: > 0 } faute
            ? $"Calcul impossible : {faute}"
            : $"{expression} = {SenSÉ.Tools.Calculator.ExpressionEvaluator.Format(resultat.Value)}";
    }

    private static string Fenetre(string nom, Action<string>? journal)
    {
        switch (nom.ToLowerInvariant())
        {
            case "reglages":
                var deja = Application.Current.Windows
                    .OfType<Settings.SenSÉSettingsWindow>().FirstOrDefault();

                if (deja is not null)
                {
                    deja.Activate();
                }
                else
                {
                    new Settings.SenSÉSettingsWindow().Show();
                }

                return "Réglages ouverts.";

            case "activite":
                SenSÉ.Desktop.Activite.ActiviteWindow.Ouvrir(journal);

                return "Moniteur d'activité ouvert.";

            case "modeles":
                SenSÉ.Desktop.Modeles.ModelesWindow.Ouvrir(journal);

                return "Jeux de modèles ouverts.";

            default:
                return $"Fenêtre inconnue : {nom}.";
        }
    }

    /// <summary>
    /// Les outils que le modèle a le droit de nommer.
    /// </summary>
    /// <remarks>
    /// <b>Tous les outils installés et activés, réglages ou pas.</b> La version précédente exigeait
    /// des réglages déclarés, ce qui écartait toutes les tuiles qui ne font qu'agir — le générateur
    /// multimédia, le moniteur d'activité. Conséquence constatée : à « génère-moi un fichier
    /// multimédia », l'assistant ouvrait les réglages du produit, faute d'avoir le générateur dans
    /// son monde. Il ne comprenait pas mal : l'outil n'existait pas pour lui.
    ///
    /// <para>
    /// Un outil sans réglage devient simplement une fonction sans paramètre. C'est exactement ce
    /// qu'il est : un geste, pas un formulaire.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<PluginManifest> Disponibles(Action<string>? journal)
    {
        var outils = PluginCatalog.Scan(PluginTools.Folder).Loaded
            .Where(m => m.Kind == PluginCategory.Tool
                        && PluginLifecycle.IsEnabled(m.Id, m.Enabled)

                        // L'assistant ne s'ouvre pas lui-même : il est déjà là.
                        && !m.Id.Equals("assistant", StringComparison.OrdinalIgnoreCase));

        // Les choix qui se lisent sur la machine sont résolus avant d'être déclarés, sinon le
        // modèle recevrait une énumération périmée et n'aurait aucun moyen de le savoir. Il
        // écrirait alors un nom de modèle que le manifeste annonce et que le disque ignore.
        //
        // La volée entière passe en un seul appel : le générateur n'est sondé qu'une fois pour tous
        // les outils, et non une fois par outil.
        return SenSÉ.Tools.Generation.OptionsVivantes.Resoudre(outils, journal);
    }

    /// <summary>
    /// Exécute l'outil nommé, par le chemin ordinaire du chargeur.
    /// </summary>
    /// <remarks>
    /// La cible du manifeste porte des jetons <c>{id}</c> ; ils reçoivent ce que le modèle a
    /// fourni, et à défaut la valeur par défaut déclarée. Aucun autre texte n'entre dans la cible :
    /// le modèle remplit des cases, il n'écrit pas de commande.
    /// </remarks>
    private string Lancer(
        PluginManifest manifeste,
        IReadOnlyDictionary<string, string> reglages,
        Action<string>? journal)
    {
        var action = manifeste.Content.LastOrDefault(
            c => c.Kind.Equals("action", StringComparison.OrdinalIgnoreCase));

        // Pas d'action : c'est une tuile, on l'ouvre. Ce n'est pas un échec.
        //
        // Treize des outils installés n'ont aucune action — ce sont des fenêtres qu'on ouvre, pas
        // des travaux qu'on lance. Ils étaient pourtant déclarés au modèle comme lançables, et
        // rendaient « n'a pas d'action à lancer », qui ne dit pas quoi faire à la place. Le 23 août
        // l'assistant a tenté « Jeux de modèles » deux fois de suite avant d'abandonner.
        //
        // Ouvrir est exactement ce qu'un utilisateur obtiendrait en cliquant la tuile : le geste
        // existe, il était seulement inaccessible par ce chemin.
        if (action is null)
        {
            return Dispatcher.Invoke(() =>
            {
                PluginTools.Ouvrir(manifeste, journal);

                return $"« {manifeste.Name} » n'a rien à lancer : sa fenêtre est ouverte devant "
                    + "l'utilisateur.";
            });
        }

        var cible = action.Target;

        // La recette d'abord, comme dans le panneau — et c'est ce qui manquait.
        //
        // La résolution de « {recette:id} » vivait dans PanneauOutil, donc elle ne s'appliquait
        // qu'à un clic de l'utilisateur. Lancé par l'assistant, l'outil recevait « {recette:moteur} »
        // tel quel et rendait « Le flux est absent : {recette:moteur} » — cinq fois de suite le
        // 24 août, le modèle essayant « SVD », puis « Wan 2.2 », puis un nom de fichier de modèle,
        // sans qu'aucun de ces essais ne puisse aboutir. Une recette qui n'existe que pour le
        // panneau n'est pas une recette, c'est une moitié de mécanisme.
        foreach (var choix in manifeste.Content.Where(c => c.Recettes.Count > 0 && c.Id.Length > 0))
        {
            var demande = reglages.GetValueOrDefault(choix.Id, "");

            // Par identifiant ou par libellé : le modèle lit la déclaration et rend volontiers
            // « Wan 2.2 » là où l'outil attend « wan22 ».
            var recette = choix.Recettes.FirstOrDefault(r =>
                r.Id.Equals(demande, StringComparison.OrdinalIgnoreCase)
                || r.Label.Equals(demande, StringComparison.OrdinalIgnoreCase));

            // JAMAIS de repli sur la première recette.
            //
            // <b>Le défaut que ceci corrige, et c'était le pire de tous.</b> Un « ?? Recettes[0] »
            // faisait qu'un moteur non reconnu tombait sur la première route de la liste — depuis
            // que texte-vers-vidéo y figure en tête, cela voulait dire : ignorer l'image que
            // l'utilisateur venait de désigner et générer depuis le texte. Quelle que soit l'image
            // fournie, il sortait la même vidéo. Une erreur servie comme un résultat, ce qui est
            // pire qu'une erreur : elle ne se voit pas, elle s'accuse.
            if (recette is null)
            {
                var offertes = string.Join(", ", choix.Recettes.Select(r => $"« {r.Label} »"));

                return $"Route inconnue : « {demande} ». Celles de cet outil sont : {offertes}. "
                    + "Reprends avec l'une d'elles, exactement.";
            }

            // La route doit suivre ce qu'on lui donne.
            //
            // Une route qui ne nomme pas {image} n'en emploie aucune : lui en passer une, c'est la
            // jeter en silence et rendre une vidéo qui n'a rien à voir avec ce que l'utilisateur a
            // désigné. Il n'y verrait qu'un outil qui se moque de lui.
            if (reglages.GetValueOrDefault("image", "").Trim().Length > 0
                && !recette.Target.Contains("{image}", StringComparison.OrdinalIgnoreCase))
            {
                var avecImage = choix.Recettes
                    .Where(r => r.Target.Contains("{image}", StringComparison.OrdinalIgnoreCase))
                    .Select(r => $"« {r.Label} »")
                    .ToList();

                return avecImage.Count == 0
                    ? $"« {recette.Label} » ne part pas d'une image, et aucune route de cet outil "
                      + "ne le fait. Relance sans image."
                    : $"« {recette.Label} » ne part pas d'une image et ignorerait celle que tu "
                      + $"donnes. Pour partir d'une image : {string.Join(", ", avecImage)}.";
            }

            cible = cible.Replace(
                "{recette:" + choix.Id + '}', recette.Target, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var champ in manifeste.Content)
        {
            if (champ.Id.Length == 0)
            {
                continue;
            }

            var valeur = reglages.TryGetValue(champ.Id, out var fourni) && fourni.Length > 0
                ? fourni
                : champ.Value;

            // Le modèle a dit « Ample », l'outil attend 180. La traduction se fait ici, au même
            // endroit et par la même règle que pour l'utilisateur qui choisit dans une liste.
            if (champ.Options.Count > 0)
            {
                valeur = OptionsOutil.Valeur(champ.Options, valeur);
            }

            cible = cible.Replace('{' + champ.Id + '}', valeur, StringComparison.OrdinalIgnoreCase);
        }

        return PluginTools.Perform(action.Does, cible, journal);
    }

    /// <summary>
    /// Ajoute un tour à la conversation et suit le fil.
    /// </summary>
    /// <remarks>
    /// Un paragraphe par tour dans un document unique : c'est ce qui rend la conversation
    /// sélectionnable d'un bout à l'autre. Le nom de qui parle est une ligne du même paragraphe,
    /// et non un contrôle à part, sinon la sélection s'arrêterait à chaque bloc.
    /// </remarks>
    private void Dire(string qui, string texte)
    {
        if (texte.Trim().Length == 0)
        {
            return;
        }

        var couleur = qui switch
        {
            "vous" => "TextPrimaryBrush",
            "systeme" => "TextDimBrush",
            _ => "AccentBrush",
        };

        var nom = qui switch
        {
            "vous" => "Vous",
            "systeme" => "",
            _ => "Assistant",
        };

        var paragraphe = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };

        if (nom.Length > 0)
        {
            paragraphe.Inlines.Add(new Run(nom)
            {
                FontSize = 10,
                Foreground = (Brush)FindResource(couleur),
            });

            paragraphe.Inlines.Add(new LineBreak());
        }

        paragraphe.Inlines.Add(new Run(texte.Trim())
        {
            FontSize = qui == "systeme" ? 11 : 13,
            FontStyle = qui == "systeme" ? FontStyles.Italic : FontStyles.Normal,
            Foreground = (Brush)FindResource(
                qui == "systeme" ? "TextDimBrush" : "TextSecondaryBrush"),
        });

        Echanges.Document.Blocks.Add(paragraphe);
        Echanges.ScrollToEnd();
    }

    /// <summary>
    /// Pose un choix devant l'utilisateur : la question, les routes, et de quoi cliquer.
    /// </summary>
    /// <remarks>
    /// <b>Écrit ET cliquable, les deux.</b> Le produit se pilote à la manette, où taper coûte un
    /// clavier à l'écran et une minute ; un bouton s'atteint à la croix directionnelle. Mais la
    /// liste numérotée reste dans le texte, parce qu'elle survit à la conversation copiée, se relit
    /// après coup, et se répond au clavier par ceux qui en ont un.
    ///
    /// <para>
    /// <b>« Fais-le toi-même » est ajouté par l'hôte, jamais par le modèle.</b> C'est une garantie
    /// et non une commodité : l'option d'abandonner la main doit être présente à chaque choix, et
    /// formulée pareil, sans dépendre de ce qu'un modèle de neuf milliards de paramètres a pensé à
    /// écrire ce tour-ci.
    /// </para>
    /// </remarks>
    private string Proposer(string question, string options)
    {
        var routes = options
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();

        if (routes.Count == 0)
        {
            return "Aucune option lisible : rends-les separees par des points-virgules.";
        }

        const string Seule = "Fais-le toi-même";

        var texte = new StringBuilder(question.Trim());

        for (var rang = 0; rang < routes.Count; rang++)
        {
            texte.AppendLine().Append(rang + 1).Append(". ").Append(routes[rang]);
        }

        texte.AppendLine().Append(routes.Count + 1).Append(". ").Append(Seule);

        Dire("assistant", texte.ToString());

        var boutons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

        foreach (var route in routes)
        {
            boutons.Children.Add(Choix(route, route, boutons, seul: false));
        }

        // Le dernier, et visiblement à part : ce n'est pas une route de plus, c'est la décision de
        // ne pas choisir.
        boutons.Children.Add(Choix(Seule, Seule, boutons, seul: true));

        Echanges.Document.Blocks.Add(new BlockUIContainer(boutons));
        Echanges.ScrollToEnd();

        return "Choix pose devant l'utilisateur. N'ajoute rien : attends sa reponse.";
    }

    /// <summary>Un bouton de choix, qui s'efface avec ses voisins une fois cliqué.</summary>
    /// <remarks>
    /// Les boutons disparaissent au clic — tous, pas seulement celui qu'on a pris. Laissés là, ils
    /// resteraient cliquables au milieu d'une conversation qui a avancé, et un second clic
    /// relancerait un choix déjà tranché.
    /// </remarks>
    private Button Choix(string libelle, string envoi, Panel rangee, bool seul)
    {
        var bouton = new Button
        {
            Content = libelle,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 6),
            FontWeight = seul ? FontWeights.SemiBold : FontWeights.Normal,
        };

        bouton.Click += (_, _) =>
        {
            rangee.Children.Clear();
            Demander(envoi, seul);
        };

        return bouton;
    }

    /// <summary>Met toute la conversation dans le presse-papiers.</summary>
    /// <remarks>
    /// La sélection à la souris fonctionne, mais sur une conversation longue elle demande de faire
    /// défiler en maintenant le bouton. Un bouton évite ce geste, qui est celui qu'on rate.
    /// </remarks>
    private void CopierClic(object sender, RoutedEventArgs e)
    {
        var tout = new TextRange(
            Echanges.Document.ContentStart,
            Echanges.Document.ContentEnd).Text;

        if (tout.Trim().Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(tout);
            Dire("systeme", "Conversation copiée dans le presse-papiers.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Dire("systeme", "Le presse-papiers est occupé par une autre application.");
        }
    }

    /// <summary>
    /// Appelé par le ProactifRunner quand l'EventBus reçoit un événement
    /// pertinent. Délègue à AssistantProactif, puis affiche la réponse
    /// dans la conversation avec le préfixe "de lui-même".
    /// </summary>
    public void SurEvenementProactif(SenSÉ.Mcp.Bus.Evenement evenement, Action<string>? journal = null)
    {
        if (evenement is null) return;
        if (_occupe) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var reponse = await AssistantProactif.ProvoquerAsync(_agent, evenement, journal)
                    .ConfigureAwait(false);
                _ = Dispatcher.BeginInvoke(() => Dire("systeme", reponse));
            }
            catch (Exception ex)
            {
                journal?.Invoke($"proactif échoué: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }
}
