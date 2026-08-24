using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// Le panneau d'un outil déclaratif, dessiné par l'hôte à partir de ce que l'outil décrit.
/// </summary>
/// <remarks>
/// <b>La règle la plus contraignante du contrat, et la plus importante.</b> Dix outils écrits par
/// dix personnes, chacun dessinant sa fenêtre, feraient dix fenêtres étrangères — aucune suivant le
/// thème, aucune navigable à la manette. Ici l'outil dit ce qu'il contient et ceci le dessine : un
/// outil écrit par un utilisateur s'atteint au pad sans que son auteur y ait jamais pensé.
///
/// <para>
/// Le prix est énoncé dans le contrat et assumé : un outil ne peut pas avoir une interface dont
/// l'hôte n'a pas les mots. Le vocabulaire s'étend quand un outil réel le demande, jamais par
/// anticipation.
/// </para>
///
/// <para>
/// <b>Pourquoi un contrôle et non une fenêtre.</b> Le même panneau sert désormais à deux hôtes :
/// sa propre fenêtre pour un outil isolé, et un onglet d'atelier quand plusieurs outils qui vont
/// ensemble sont réunis. Le dessiner deux fois aurait produit deux rendus qui divergent — celui
/// qu'on améliore et celui qu'on oublie.
/// </para>
/// </remarks>
public sealed class PanneauOutil : UserControl
{
    /// <summary>
    /// Les panneaux dessinés, quel que soit leur hôte.
    /// </summary>
    /// <remarks>
    /// L'assistant règle une option d'un panneau ouvert en le retrouvant ici. Avant, il parcourait
    /// les fenêtres de l'application ; un panneau devenu onglet n'en est plus une, et la capacité
    /// se serait tue sans rien dire. Un registre ne dépend pas de l'endroit où le panneau est posé.
    /// </remarks>
    private static readonly List<PanneauOutil> Vivants = [];

    private readonly PluginManifest _manifeste;
    private readonly Action<string>? _journal;
    private readonly Dictionary<string, Func<string>> _valeurs = [];

    /// <summary>La cible qu'emporte la recette retenue, par identifiant de choix.</summary>
    /// <remarks>
    /// Tenue à part de <see cref="_valeurs"/> parce qu'elle n'est pas de même nature : une valeur
    /// vient de l'utilisateur et s'échappe avant d'entrer dans une cible, une recette EST une cible
    /// et ses barres verticales sont des séparateurs. Les mélanger reviendrait à échapper les
    /// séparateurs du manifeste, donc à envoyer un graphe entier comme s'il n'était qu'un mot.
    /// </remarks>
    private readonly Dictionary<string, Func<string>> _recettes = [];
    private readonly Dictionary<string, FrameworkElement> _controles = [];

    /// <summary>Ce qui est propre à l'utilisateur plutôt qu'à l'outil.</summary>
    private readonly HashSet<string> _jamaisRetenu = [];

    private readonly StackPanel _corps = new();
    private readonly ProgressBar _travail;
    private readonly TextBlock _etat;

    public PanneauOutil(PluginManifest manifeste, Action<string>? journal)
    {
        // Les choix qui se lisent sur la machine sont résolus à l'ouverture, jamais au chargement du
        // catalogue : la liste des modèles installés change pendant que le produit tourne, et un
        // panneau ouvert ce soir doit montrer ce qui est là ce soir. Le générateur n'est pas démarré
        // pour autant — s'il dort, le manifeste sert de repli.
        _manifeste = SteamXBox.Tools.Generation.OptionsVivantes.Resoudre(manifeste, journal);
        _journal = journal;

        _travail = new ProgressBar
        {
            Height = 3,
            Margin = new Thickness(0, 14, 0, 0),
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
        };

        _etat = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
        };

        var tout = new StackPanel();
        tout.Children.Add(_corps);
        tout.Children.Add(_travail);
        tout.Children.Add(_etat);

        Content = tout;

        Dessiner(PluginMemory.Load(_manifeste, journal));

        Vivants.Add(this);
    }

    /// <summary>L'identifiant de l'outil que ce panneau montre.</summary>
    public string Id => _manifeste.Id;

    /// <summary>Son nom, pour l'onglet ou le titre.</summary>
    public string Nom => _manifeste.Name;

    /// <summary>Ce qu'il fait, en une phrase.</summary>
    public string Aide => _manifeste.Hint;

    /// <summary>Enregistre ce que l'outil a demandé de retenir, et se retire du registre.</summary>
    public void Fermer()
    {
        Sauver();
        Vivants.Remove(this);
    }

    /// <summary>
    /// Règle une option d'un panneau ouvert, comme si l'utilisateur l'avait fait.
    /// </summary>
    /// <remarks>
    /// <b>Le panneau reste la seule vérité.</b> L'assistant ne tient pas d'état parallèle : il
    /// déplace le curseur, choisit dans la liste, écrit dans la case — et l'utilisateur voit le
    /// changement se produire sous ses yeux. Un assistant qui garderait ses propres valeurs et les
    /// enverrait à l'exécution laisserait un panneau qui affiche autre chose que ce qui va tourner.
    ///
    /// <para>
    /// Rien n'est accordé au-delà de ce que la brique permet : une valeur hors de la liste d'un
    /// choix est refusée en nommant les valeurs valides, plutôt qu'écrite en force.
    /// </para>
    /// </remarks>
    public static string Regler(string outil, string reglage, string valeur)
    {
        var panneau = Vivants.Find(p =>
            string.Equals(p.Id, outil, StringComparison.OrdinalIgnoreCase));

        if (panneau is null)
        {
            return $"Le panneau de « {outil} » n'est pas ouvert.";
        }

        return panneau.Dispatcher.Invoke(() => panneau.Appliquer(reglage, valeur));
    }

    private string Appliquer(string reglage, string valeur)
    {
        if (!_controles.TryGetValue(reglage, out var controle))
        {
            return $"Réglage inconnu : « {reglage} ». Ceux de cet outil : "
                + $"{string.Join(", ", _controles.Keys)}.";
        }

        switch (controle)
        {
            case TextBox boite:
                boite.Text = valeur;

                return $"{reglage} : {valeur}";

            case Slider curseur:
                if (!double.TryParse(
                        valeur, NumberStyles.Float, CultureInfo.InvariantCulture, out var nombre))
                {
                    return $"« {valeur} » n'est pas un nombre.";
                }

                curseur.Value = Math.Clamp(nombre, curseur.Minimum, curseur.Maximum);

                return $"{reglage} : {(int)curseur.Value}";

            case ComboBox liste:
                // Le libellé comme la valeur : l'assistant a pu retenir « Ample » ou « 180 » selon
                // ce qu'il a lu, et lui refuser l'un des deux serait une pédanterie qui casse une
                // demande parfaitement claire.
                var choix = liste.Items.OfType<OptionOutil>().Cast<OptionOutil?>().FirstOrDefault(o =>
                    o!.Value.Libelle.Equals(valeur, StringComparison.OrdinalIgnoreCase)
                    || o.Value.Valeur.Equals(valeur, StringComparison.OrdinalIgnoreCase));

                if (choix is null)
                {
                    var options = string.Join(", ", liste.Items.OfType<OptionOutil>().Select(o => o.Libelle));

                    return $"« {valeur} » n'est pas proposé. Valeurs possibles : {options}.";
                }

                liste.SelectedItem = choix.Value;

                return $"{reglage} : {choix.Value.Libelle}";

            case Grid grille:
                // Le sélecteur de fichier est une grille dont la première case porte le chemin.
                if (grille.Children.OfType<TextBox>().FirstOrDefault() is { } chemin)
                {
                    chemin.Text = valeur;

                    return $"{reglage} : {valeur}";
                }

                return $"Le réglage « {reglage} » ne peut pas être écrit.";

            default:
                return $"Le réglage « {reglage} » ne peut pas être écrit.";
        }
    }

    /// <summary>Un contrôle par élément déclaré.</summary>
    private void Dessiner(IReadOnlyDictionary<string, string> retenu)
    {
        foreach (var item in _manifeste.Content)
        {
            var garde = item.Id.Length > 0 && retenu.TryGetValue(item.Id, out var vu) ? vu : item.Value;

            switch (item.Kind.ToLowerInvariant())
            {
                case "text":
                    Poser(item, Texte(item, garde));
                    break;

                case "number":
                    Poser(item, Nombre(item, garde));
                    break;

                case "choice":
                    Poser(item, Choix(item, garde));
                    break;

                case "file":
                    Poser(item, Fichier(item));
                    break;

                case "action":
                    _corps.Children.Add(Bouton(item));
                    break;
            }
        }
    }

    /// <summary>
    /// Là où vont les réglages qu'un outil déclare comme des conséquences.
    /// </summary>
    /// <remarks>
    /// Créé seulement s'il en existe : un panneau qui montrerait « Réglages avancés » sur un outil
    /// qui n'en a pas promettrait quelque chose qui n'est pas là.
    /// </remarks>
    private StackPanel? _avances;

    private StackPanel Avances()
    {
        if (_avances is not null)
        {
            return _avances;
        }

        _avances = new StackPanel();

        var repli = new Expander
        {
            Header = "Réglages avancés",
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            Content = _avances,
        };

        _corps.Children.Add(repli);

        return _avances;
    }

    /// <summary>Une ligne étiquetée, la seule mise en page qu'un outil puisse demander.</summary>
    private void Poser(PluginContentItem item, FrameworkElement controle)
    {
        // Retenu ici parce que c'est le seul endroit où tous les contrôles passent : l'assistant
        // peut ainsi régler une option sans que chaque constructeur ait à s'en occuper, et sans
        // qu'on oublie le jour où une brique s'ajoute.
        if (item.Id.Length > 0)
        {
            _controles[item.Id] = controle;
        }

        // Une conséquence ne se pose pas au même rang qu'un choix. Un réglage dont le manifeste dit
        // « à laisser tel quel » au premier plan invite à casser quelque chose sans rien offrir.
        var ou = item.Avance ? Avances() : _corps;

        if (item.Label.Length > 0)
        {
            ou.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            });
        }

        ou.Children.Add(controle);

        // L'explication vient sous le contrôle et non au-dessus : l'œil descend l'étiquette, le
        // réglage, puis la raison. Placée avant, elle éloignerait le nom de la chose qu'il nomme.
        if (item.Hint.Length > 0)
        {
            ou.Children.Add(new TextBlock
            {
                Text = item.Hint,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("TextDimBrush"),
            });
        }
    }

    private TextBox Texte(PluginContentItem item, string valeur)
    {
        var boite = new TextBox { Text = valeur, Padding = new Thickness(6, 4, 6, 4) };

        Retenir(item, () => boite.Text);

        return boite;
    }

    /// <summary>
    /// Un nombre, borné par ce que l'outil a déclaré.
    /// </summary>
    /// <remarks>
    /// Un curseur plutôt qu'une case : on ne peut pas lui donner une valeur hors bornes, donc l'hôte
    /// n'a jamais à valider ce qu'un utilisateur a tapé, et il s'emploie à la manette — une case
    /// appellerait le clavier à l'écran pour un nombre entre un et soixante.
    /// </remarks>
    private Slider Nombre(PluginContentItem item, string valeur)
    {
        var curseur = new Slider
        {
            Minimum = item.Min,
            Maximum = Math.Max(item.Min + 1, item.Max),
            Value = double.TryParse(valeur, out var lu) ? lu : item.Min,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,
        };

        Retenir(item, () => ((int)curseur.Value).ToString(CultureInfo.InvariantCulture));

        return curseur;
    }

    /// <summary>
    /// Un document que l'utilisateur désigne, et rien d'autre.
    /// </summary>
    /// <remarks>
    /// Le panneau appartient à l'hôte, donc le sélecteur aussi, et l'outil reçoit un chemin — celui
    /// qu'on a montré. Il ne voit jamais le dossier d'où il vient, ne liste rien, et ne peut pas
    /// atteindre un second fichier. C'est la boîte à pouvoirs que décrit le contrat, et c'est ce qui
    /// permet à un outil de toucher un document sans qu'on lui confie le disque.
    ///
    /// <para>
    /// Jamais retenu d'une session à l'autre, même si l'outil le demande. Le chemin vers le document
    /// de quelqu'un est la seule valeur ici qui parle de lui plutôt que de l'outil.
    /// </para>
    /// </remarks>
    private Grid Fichier(PluginContentItem item)
    {
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chemin = new TextBox
        {
            Text = "",
            IsReadOnly = true,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var parcourir = new Button
        {
            Content = "Choisir…",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(6, 0, 0, 0),
        };

        parcourir.Click += (_, _) =>
        {
            var dialogue = new Microsoft.Win32.OpenFileDialog
            {
                Title = item.Label.Length > 0 ? item.Label : "Choisir un document",
                Filter = item.Options.Count > 0
                    ? $"Documents ({string.Join(", ", item.Options.Select(o => "*." + o))})|"
                      + string.Join(";", item.Options.Select(o => "*." + o))
                    : "Tous les fichiers|*.*",
                CheckFileExists = true,
            };

            // La fenêtre qui porte ce panneau, quelle qu'elle soit : sa propre fenêtre, ou l'atelier
            // qui l'a mis en onglet.
            var hote = Window.GetWindow(this) ?? Application.Current.MainWindow;

            if (hote is not null ? dialogue.ShowDialog(hote) == true : dialogue.ShowDialog() == true)
            {
                chemin.Text = dialogue.FileName;
                _etat.Text = "";
            }
        };

        Grid.SetColumn(chemin, 0);
        Grid.SetColumn(parcourir, 1);
        grille.Children.Add(chemin);
        grille.Children.Add(parcourir);

        _valeurs[item.Id] = () => chemin.Text;
        _jamaisRetenu.Add(item.Id);

        return grille;
    }

    /// <summary>
    /// Une liste où l'on lit un mot et où l'outil reçoit un chiffre.
    /// </summary>
    /// <remarks>
    /// Une option s'écrit <c>180=Ample</c> : la liste montre « Ample », la cible reçoit 180.
    /// Personne ne sait ce que vaut 127 sur une échelle de mouvement, et le savoir n'est pas le
    /// travail de l'utilisateur — c'est celui de qui a écrit le manifeste, une fois.
    ///
    /// <para>
    /// Une option sans signe égal reste elle-même : les listes lues sur la machine — les modèles
    /// installés — n'ont pas de libellé et n'en veulent pas.
    /// </para>
    /// </remarks>
    /// <summary>Les recettes que cette machine peut réellement exécuter.</summary>
    /// <remarks>
    /// <b>Le disque décide, pas le manifeste.</b> Une recette déclare les modèles sans lesquels
    /// elle ne peut pas tourner ; celles dont il manque un fichier ne sont pas proposées. Proposer
    /// un moteur absent, c'est promettre un travail qui échouera au chargement, plusieurs minutes
    /// plus tard, sur un message que personne ne rattache au menu où le choix a été fait.
    ///
    /// <para>
    /// Une recette sans exigence est toujours offerte : c'est ainsi qu'un graphe qui ne dépend
    /// d'aucun poids particulier reste disponible partout.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<RecetteOutil> Disponibles(PluginContentItem item)
    {
        var racine = SteamXBox.Tools.Modeles.JeuxModeles.Racine;

        return [.. item.Recettes.Where(r => r.Exige.All(f =>
            File.Exists(Path.Combine(racine, f.Replace('/', Path.DirectorySeparatorChar)))))];
    }

    private ComboBox Choix(PluginContentItem item, string valeur)
    {
        if (item.Recettes.Count > 0)
        {
            return Moteur(item, valeur);
        }

        var boite = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        var options = OptionsOutil.Lire(item.Options);

        foreach (var option in options)
        {
            boite.Items.Add(option);
        }

        // La valeur retenue est celle du flux, jamais le libellé : c'est elle qui a un sens pour
        // l'outil, et elle survit à une reformulation du libellé.
        var cherche = OptionsOutil.Valeur(item.Options, valeur);

        boite.SelectedItem = options.Any(o => o.Valeur == cherche)
            ? options.First(o => o.Valeur == cherche)
            : options.FirstOrDefault();

        Retenir(item, () => boite.SelectedItem is OptionOutil choisi ? choisi.Valeur : "");

        return boite;
    }

    /// <summary>Le menu des recettes, et la cible que chacune emporte avec elle.</summary>
    /// <remarks>
    /// Deux choses sont retenues plutôt qu'une : l'identifiant de la recette, pour que
    /// <c>remembers</c> retrouve le choix d'une session à l'autre, et sa cible, que
    /// <see cref="Substituer"/> ira chercher sous <c>{recette:id}</c>. La cible n'est pas une valeur
    /// d'utilisateur — c'est du manifeste — et c'est pourquoi elle ne passe pas par l'échappement
    /// des barres : ses barres à elle SONT des séparateurs.
    /// </remarks>
    private ComboBox Moteur(PluginContentItem item, string valeur)
    {
        var boite = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        var pretes = Disponibles(item);

        foreach (var recette in pretes)
        {
            boite.Items.Add(new OptionOutil(
                recette.Id, recette.Label.Length > 0 ? recette.Label : recette.Id));
        }

        if (pretes.Count == 0)
        {
            // Le panneau reste ouvert et le dit : la liste vide sans explication se lit comme une
            // panne, alors qu'il ne manque que des fichiers, et qu'un écran sait où les prendre.
            boite.Items.Add(new OptionOutil("", "Aucun moteur installé — ouvrez les jeux de modèles"));
            boite.IsEnabled = false;
        }

        // OptionOutil est une structure : FirstOrDefault rend une option vide plutôt que null, et
        // la retenue se fait donc sur la présence explicite du choix mémorisé.
        var offertes = boite.Items.OfType<OptionOutil>().ToList();

        boite.SelectedItem = offertes.Any(o => o.Valeur == valeur)
            ? offertes.First(o => o.Valeur == valeur)
            : offertes.FirstOrDefault();

        Retenir(item, () => boite.SelectedItem is OptionOutil choisi ? choisi.Valeur : "");

        _recettes[item.Id] = () => boite.SelectedItem is OptionOutil choisi
            ? pretes.FirstOrDefault(r => r.Id == choisi.Valeur)?.Target ?? ""
            : "";

        return boite;
    }

    /// <summary>
    /// Un bouton qui exécute une des actions de l'hôte.
    /// </summary>
    /// <remarks>
    /// La cible peut nommer une valeur du panneau, écrite <c>{id}</c>. C'est tout ce qu'un outil de
    /// premier niveau approche du calcul : substituer une valeur que l'utilisateur a choisie, jamais
    /// en déduire une.
    /// </remarks>
    private Button Bouton(PluginContentItem item)
    {
        var bouton = new Button
        {
            Content = item.Label.Length > 0 ? item.Label : item.Does,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Hors du fil d'affichage, toujours.
        //
        // Convertir un document n'est pas instantané : un vrai a pris soixante et onze secondes, et
        // sur le fil d'affichage c'est soixante et onze secondes de SteamXBox figé — plein écran,
        // refusant de se réduire, posé sous tout le reste. Vu de l'utilisateur, la machine était
        // morte.
        //
        // Toutes les actions passent par là, pas seulement la lente. Lancer une application peut
        // bloquer sur un chemin réseau, et une règle qui ne vaut que pour l'action qu'on sait lente
        // aujourd'hui est une règle qu'on oubliera à la prochaine.
        bouton.Click += async (_, _) =>
        {
            // Le tirage se fait avant la lecture des valeurs, et il se voit : le curseur bouge sous
            // les yeux de l'utilisateur, et le champ garde ensuite la valeur qui a produit l'image.
            // Sans cela, « refais-en un autre » donnerait un résultat qu'on ne saurait pas retrouver.
            Tirer(item.Hasard);

            Sauver();

            var quoi = item.Does;
            var cible = Substituer(item.Target);

            bouton.IsEnabled = false;
            _travail.Visibility = Visibility.Visible;
            _etat.Text = "En cours…";

            try
            {
                var journal = _journal;
                var dit = await Task.Run(() => PluginTools.Perform(quoi, cible, journal));

                _etat.Text = dit.Length > 0 ? dit : "Terminé.";
            }
            catch (Exception exception)
            {
                _journal?.Invoke(
                    $"plugin action '{quoi}' failed: {exception.GetType().Name}: {exception.Message}");

                _etat.Text = exception.Message;
            }
            finally
            {
                _travail.Visibility = Visibility.Collapsed;
                bouton.IsEnabled = true;
            }
        };

        return bouton;
    }

    /// <summary>
    /// Retire au sort la valeur d'un réglage, dans les bornes qu'il déclare.
    /// </summary>
    /// <remarks>
    /// <b>Dans ses bornes, jamais au-delà.</b> Un tirage libre produirait une valeur que le champ
    /// refuse et que l'outil rejettera plus loin — un bouton qui se saborde. Les bornes sont celles
    /// que le manifeste a écrites, et le curseur les porte déjà.
    ///
    /// <para>
    /// Un identifiant qui ne désigne rien ne fait rien : le catalogue refuse déjà ce cas au
    /// chargement, et une exception ici transformerait une faute de manifeste en bouton qui tue le
    /// panneau.
    /// </para>
    /// </remarks>
    private void Tirer(string reglage)
    {
        if (reglage.Length == 0 || !_controles.TryGetValue(reglage, out var controle))
        {
            return;
        }

        switch (controle)
        {
            case Slider curseur:
                curseur.Value = Math.Floor(
                    curseur.Minimum + (Random.Shared.NextDouble() * (curseur.Maximum - curseur.Minimum)));

                break;

            case ComboBox liste when liste.Items.Count > 0:
                liste.SelectedIndex = Random.Shared.Next(liste.Items.Count);

                break;
        }
    }

    /// <summary>Remplace <c>{id}</c> dans une cible par ce que le panneau porte.</summary>
    /// <remarks>
    /// La valeur est échappée avant d'entrer dans la cible. Sans cela, une barre verticale tapée par
    /// l'utilisateur devenait un séparateur : l'invite « un chat roux | style aquarelle » ajoutait
    /// un réglage que personne n'avait écrit, et l'outil refusait de partir sur une phrase
    /// parfaitement légitime. Voir <see cref="Segments"/>, qui porte les deux moitiés de la règle.
    /// </remarks>
    private string Substituer(string cible)
    {
        if (!cible.Contains('{'))
        {
            return cible;
        }

        // La recette d'abord, et sans échappement : elle apporte le graphe et ses liaisons, donc
        // ses barres verticales sont des séparateurs. Ce qu'elle contient en {id} est substitué
        // juste après, par la boucle ordinaire — c'est ainsi qu'un graphe déclaré dans le manifeste
        // reçoit les valeurs que l'utilisateur vient de régler.
        foreach (var (id, lire) in _recettes)
        {
            cible = cible.Replace("{recette:" + id + '}', lire(), StringComparison.OrdinalIgnoreCase);
        }

        foreach (var (id, lire) in _valeurs)
        {
            cible = cible.Replace('{' + id + '}', Segments.Echapper(lire()), StringComparison.OrdinalIgnoreCase);
        }

        return cible;
    }

    /// <summary>Enregistre une valeur, si l'outil a demandé qu'on la garde.</summary>
    /// <remarks>
    /// Seulement ce que <c>remembers</c> nomme. Ce qui n'est pas nommé n'est pas gardé : un outil ne
    /// peut donc pas conserver en douce quelque chose que son manifeste ne montre pas.
    /// </remarks>
    private void Retenir(PluginContentItem item, Func<string> lire)
    {
        if (item.Id.Length > 0)
        {
            _valeurs[item.Id] = lire;
        }
    }

    private void Sauver()
        => PluginMemory.Save(
            _manifeste,
            _valeurs
                .Where(pair => _manifeste.Remembers.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                               && !_jamaisRetenu.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value()),
            _journal);
}
