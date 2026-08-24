using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sc2Xboxed.Core.Diagnostics;
using SteamXBox.Desktop.Tools;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Settings;

/// <summary>
/// Where a tool is switched off, packed away or thrown out.
/// </summary>
/// <remarks>
/// "A tool is a folder" makes installing obvious and uninstalling a file-manager job. This is the
/// same three operations given names and a place, so that removing a tool does not require knowing
/// where SteamXBox keeps things.
///
/// <para>
/// The three are deliberately different, and the screen says which is which: switching off keeps
/// everything and is instant to undo, archiving compresses and frees the space but can be unpacked,
/// and ejecting removes the folder while leaving any archive alone. Only deleting an archive is
/// final.
/// </para>
/// </remarks>
public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private static string Root => PluginTools.Folder;

    /// <summary>
    /// Every tool, compiled into the product or loaded from a folder, in one list.
    /// </summary>
    /// <remarks>
    /// The calculator, the capture and the clipboard belong here as much as anything dropped into
    /// the plugin folder: from where the user stands they are all tools of the control centre, and a
    /// screen that listed only half of them would look broken rather than principled.
    ///
    /// <para>
    /// They differ in what can be done to them, not in whether they appear. A compiled tool has no
    /// folder, so it cannot be archived or removed — only switched off.
    /// </para>
    /// </remarks>
    private void Refresh()
    {
        

        var builtin = ToolRegistry.Builtin.Select(tool => new Row(
            new PluginEntry(
                tool.Id,
                tool.Label,
                "",
                PluginLifecycle.IsEnabled(tool.Id, byDefault: true) ? PluginState.Enabled : PluginState.Disabled,
                0),
            Archived: false,
            Compiled: true,
            Protected: true));

        var loaded = PluginLifecycle.List(Root).Select(entry => new Row(
            entry,
            PluginLifecycle.HasArchive(Root, entry.Id),
            Compiled: false,
            Protected: PluginTools.Shipped.Contains(entry.Id)));

        var rows = builtin.Concat(loaded).ToArray();

        Tools.ItemsSource = rows;

        // Le bloc reste invisible tant qu'aucune dépendance n'est déclarée : un titre suivi du vide
        // fait croire à une panne.
        var dependances = Settings.Dependances.Lister(Root);

        Dependances.ItemsSource = dependances;
        BlocDependances.Visibility = dependances.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        PaquetInfo.Text = Pesee();

        Status.Text = $"{rows.Length} outil(s). Les changements prennent effet au prochain démarrage "
            + "de l'environnement. Les outils livrés avec SteamXBox ne s'effacent pas d'ici : "
            + "supprimez leur dossier dans Plugins si vous y tenez."
            + WindowsLeftBehind();
    }

    private CancellationTokenSource? _paquetArret;

    /// <summary>Ce que pèserait le paquet, dit avant qu'on le demande.</summary>
    /// <remarks>
    /// Cinq gigaoctets ne se compressent pas en une seconde. Annoncer le poids et le nombre de
    /// fichiers avant le clic évite qu'on lance l'opération en croyant qu'elle sera instantanée,
    /// puis qu'on la prenne pour un blocage.
    /// </remarks>
    private static string Pesee()
    {
        try
        {
            var fichiers = SteamXBox.Tools.Generation.PaquetGenerateur.Contenu(AppContext.BaseDirectory);

            if (fichiers.Count == 0)
            {
                return "Aucun générateur installé : il n'y a rien à empaqueter pour l'instant.";
            }

            var octets = fichiers.Sum(f => new FileInfo(f).Length);

            return $"Le programme et son interpréteur — {fichiers.Count} fichiers, "
                + $"{octets / 1024 / 1024 / 1024.0:0.0} Go — tiennent dans une archive qui se "
                + "remet en place sur une autre machine sans rien télécharger. Les modèles restent "
                + "dehors : ils ne se compressent pas, et les jeux de modèles savent déjà les "
                + "réclamer.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Le générateur n'a pas pu être mesuré : {exception.Message}";
        }
    }

    private void CompresserClic(object sender, RoutedEventArgs e)
    {
        var boite = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Où écrire le paquet du générateur",
            FileName = "generateur-steamxbox.zip",
            Filter = "Archive (*.zip)|*.zip",
        };

        if (boite.ShowDialog() != true)
        {
            return;
        }

        Travailler(
            "Empaquetage",
            arret => SteamXBox.Tools.Generation.PaquetGenerateur.Compresser(
                AppContext.BaseDirectory, boite.FileName, Avancer, arret),
            $"Paquet écrit : {boite.FileName}");
    }

    private void DecompresserClic(object sender, RoutedEventArgs e)
    {
        var boite = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Le paquet du générateur à remettre en place",
            Filter = "Archive (*.zip)|*.zip",
        };

        if (boite.ShowDialog() != true)
        {
            return;
        }

        Travailler(
            "Déballage",
            arret => SteamXBox.Tools.Generation.PaquetGenerateur.Decompresser(
                AppContext.BaseDirectory, boite.FileName, Avancer, arret),
            "Générateur remis en place. Il sera pris au prochain démarrage.");
    }

    private void ArreterPaquetClic(object sender, RoutedEventArgs e) => _paquetArret?.Cancel();

    /// <summary>
    /// Le corps d'un outil : le dossier que son manifeste déclare, s'il en déclare un.
    /// </summary>
    /// <remarks>
    /// <b>Ce que ceci répare.</b> Archiver compressait <c>Plugins\&lt;id&gt;</c> — le manifeste, un
    /// kilooctet — et laissait les cinq cents mégaoctets du programme intacts dans <c>Outils</c>. Le
    /// produit savait donc ranger l'étiquette et oublier le corps ; la place annoncée comme libérée
    /// ne l'était pas.
    ///
    /// <para>
    /// Un outil livré n'a pas de corps déclaré et rend une chaîne vide : pour lui rien ne change,
    /// ce qui est voulu — son manifeste <i>est</i> tout ce qu'il est.
    /// </para>
    /// </remarks>
    private static string Corps(string id)
    {
        try
        {
            var declare = PluginCatalog.Scan(Root).Loaded
                .FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?.Environnement?.Dossier;

            return declare is { Length: > 0 } ou ? PluginTools.Resoudre(ou) : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Sans manifeste lisible, on ne sait pas quel corps désigner : mieux vaut n'en toucher
            // aucun que d'en deviner un.
            return "";
        }
    }

    /// <summary>
    /// La ligne de désinstallation que l'outil a déclarée, s'il en a une.
    /// </summary>
    /// <remarks>
    /// Sans elle, éjecter efface les fichiers dans le dos de l'installeur : la base de Windows garde
    /// le produit enregistré et la réinstallation suivante ne réinstalle rien, puisqu'elle voit un
    /// produit déjà présent. Constaté sur LibreOffice, avec en prime un effacement arrêté en chemin
    /// sur un fichier verrouillé — huit cents mégaoctets sans exécutable principal.
    /// </remarks>
    private static string Desinstallation(string id)
    {
        try
        {
            return PluginCatalog.Scan(Root).Loaded
                .FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?.Desinstallation ?? "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>
    /// Fait tourner l'installeur d'un programme extérieur dans un dossier qui n'est qu'à lui.
    /// </summary>
    /// <remarks>
    /// Le dossier porte le nom de l'installeur : c'est ce que l'utilisateur reconnaîtra plus tard
    /// dans <c>Outils</c>, et c'est le seul endroit à supprimer pour que l'outil s'en aille en
    /// entier — ce que sa propre désinstallation ne fait pas.
    /// </remarks>
    private void AccueillirClic(object sender, RoutedEventArgs e)
    {
        var boite = new Microsoft.Win32.OpenFileDialog
        {
            Title = "L'installeur du programme à accueillir",
            Filter = "Installeur (*.exe;*.msi)|*.exe;*.msi",
        };

        if (boite.ShowDialog() != true)
        {
            return;
        }

        var nom = Nommer(boite.FileName);
        var dossier = Path.Combine(AppContext.BaseDirectory, "Outils", nom);

        // Les dossiers témoins sont ceux où un programme se répand quand personne ne l'en empêche.
        // Les surveiller pendant l'accueil est la seule façon de dire si l'isolement a tenu au lieu
        // de l'affirmer.
        string[] temoins =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];

        AccueilEtat.Text = $"Accueil de {nom} dans Outils\\{nom}…";

        Travailler(
            "Accueil",
            arret => Raconter(
                SteamXBox.Plugins.AccueilOutil.Installer(boite.FileName, dossier, null, temoins, PluginTools.Folder, arret)),
            "",
            AccueilEtat,
            () => Designer(dossier));
    }

    /// <summary>
    /// Demande une fois quel programme ouvre l'outil, quand rien ne permet de le deviner.
    /// </summary>
    /// <remarks>
    /// <b>L'aveu plutôt que la devinette.</b> LibreOffice, mesuré : rien à la racine de son dossier,
    /// seize programmes à fenêtre dans <c>program\</c>, et ni raccourci ni clé de registre pour dire
    /// lequel est le principal. Choisir <c>soffice</c> plutôt que <c>swriter</c> demanderait de
    /// connaître LibreOffice, et une règle fondée sur ce qu'on croit savoir d'un programme se
    /// trompera sur le suivant.
    ///
    /// <para>
    /// La question est posée une seule fois : la réponse part dans le manifeste, et l'outil est
    /// ensuite un outil comme les autres. Le plus gros exécutable est proposé en premier — ce n'est
    /// pas une certitude, mais sur une suite le programme principal porte ce que les autres
    /// appellent, et sur LibreOffice cela tombe juste.
    /// </para>
    /// </remarks>
    private void Designer(string dossier)
    {
        if (SteamXBox.Plugins.Reconnaissance.Regarder(dossier) is not
            { Forme: SteamXBox.Plugins.FormeProgramme.PorteInconnue } programme)
        {
            return;
        }

        var portes = SteamXBox.Plugins.Reconnaissance.Portes(dossier);

        var boite = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Lequel de ces programmes ouvre {programme.Nom} ?",
            Filter = "Programme (*.exe)|*.exe",
            InitialDirectory = portes.Count > 0 ? Path.GetDirectoryName(portes[0]) : dossier,
            FileName = portes.Count > 0 ? Path.GetFileName(portes[0]) : "",
        };

        AccueilEtat.Text += Environment.NewLine
            + $"{programme.Nom} est installé, mais rien ne dit lequel de ses "
            + $"{portes.Count} programmes l'ouvre. Désignez-le une fois.";

        if (boite.ShowDialog() != true)
        {
            AccueilEtat.Text += Environment.NewLine
                + "Pas de tuile pour l'instant : relancez l'accueil pour la poser plus tard.";

            return;
        }

        AccueilEtat.Text += Environment.NewLine
            + SteamXBox.Plugins.AccueilOutil.Declarer(programme, Root, boite.FileName);

        Refresh();
    }

    /// <summary>Le nom de dossier tiré de celui de l'installeur.</summary>
    /// <remarks>
    /// Les installeurs téléchargés portent des noms qui ne sont pas des noms de dossiers —
    /// « Comfy-Desktop-Setup-phid1_019ff829-… ». On garde ce qui précède le premier mot de
    /// remplissage, faute de quoi le dossier d'accueil est illisible.
    /// </remarks>
    private static string Nommer(string installeur)
    {
        var brut = Path.GetFileNameWithoutExtension(installeur);
        var coupe = brut.Split(["-Setup", "_Setup", " Setup", "-setup"], StringSplitOptions.None)[0];
        // Pas d'espace dans le nom : les installeurs coupent leur directive au premier, et rien ne
        // garantit que Windows donnera une forme courte à un dossier créé aujourd'hui.
        var propre = new string([.. coupe.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : ' ')]).Trim().Replace(' ', '-');

        return propre.Length == 0 ? "outil-accueilli" : propre;
    }

    /// <summary>Rend le rapport d'accueil lisible dans la zone d'état.</summary>
    private static string Raconter(SteamXBox.Plugins.AccueilRapport rapport)
    {
        var lignes = new List<string>(rapport.Dits)
        {
            rapport.Octets == 0
                ? "Rien n'a été écrit dans le dossier d'accueil."
                : $"{rapport.Octets / 1024d / 1024d:0.#} Mio dans {rapport.Dossier}.",
        };

        return string.Join(Environment.NewLine, lignes);
    }

    /// <summary>
    /// Fait le travail hors du fil d'affichage, et tient l'écran pendant ce temps.
    /// </summary>
    /// <remarks>
    /// Cinq gigaoctets sur le fil d'affichage, c'est la fenêtre figée le temps de l'opération — et
    /// l'utilisateur qui conclut à un plantage au bout de vingt secondes. Le même raisonnement que
    /// pour les panneaux d'outils, et la même solution.
    /// </remarks>
    private async void Travailler(
        string quoi,
        Func<CancellationToken, string> travail,
        string succes,
        TextBlock? ou = null,
        Action? apres = null)
    {
        _paquetArret?.Dispose();
        _paquetArret = new CancellationTokenSource();

        Compresser.IsEnabled = false;
        Decompresser.IsEnabled = false;
        ArreterPaquet.Visibility = Visibility.Visible;
        PaquetBarre.Visibility = Visibility.Visible;
        PaquetBarre.Value = 0;
        (ou ?? PaquetEtat).Text = $"{quoi} en cours…";

        try
        {
            var arret = _paquetArret.Token;
            var dit = await Task.Run(() => travail(arret));

            (ou ?? PaquetEtat).Text = dit.Length == 0 ? succes : dit;

            // Après, et sur le fil d'affichage : ce qui suit un accueil peut avoir à poser une
            // question, et une question ne se pose pas depuis un fil de travail.
            apres?.Invoke();
        }
        catch (Exception exception)
        {
            UiLog.Failure(quoi.ToLowerInvariant() + " du générateur", exception);
            (ou ?? PaquetEtat).Text = exception.Message;
        }
        finally
        {
            Compresser.IsEnabled = true;
            Decompresser.IsEnabled = true;
            ArreterPaquet.Visibility = Visibility.Collapsed;
            PaquetBarre.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Montre l'avancement, sans noyer le fil d'affichage.</summary>
    /// <remarks>
    /// Un paquet porte des dizaines de milliers de fichiers : rendre la main à l'affichage à chacun
    /// coûterait plus que la compression elle-même. Le texte ne change donc qu'au pour-mille près,
    /// ce qui reste imperceptible à l'œil et divise par mille le nombre de bascules de fil.
    /// </remarks>
    private void Avancer(SteamXBox.Tools.Generation.AvanceePaquet ou)
    {
        if (ou.Total <= 0)
        {
            return;
        }

        var pour = (int)(ou.Fait * 1000 / ou.Total);

        if (pour == (int)PaquetBarre.Value)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            PaquetBarre.Value = pour;
            PaquetEtat.Text = $"{pour / 10.0:0.0} % — {ou.Quoi}";
        });
    }

    /// <summary>
    /// Says what would stay on the machine after SteamXBox is removed.
    /// </summary>
    /// <remarks>
    /// <b>Said on the screen people go to in order to remove things.</b> Applying a cursor theme
    /// writes into <c>HKCU\Control Panel\Cursors</c>, and that is meant to persist — a theme that
    /// undid itself on exit would be no theme at all. But it also persists through <i>uninstalling
    /// the product</i>, and the snapshot of what Windows had before lives in SteamXBox's own state
    /// folder, which an uninstall takes with it.
    ///
    /// <para>
    /// Measured on the development machine on 11 August 2026: sixteen of the nineteen recorded
    /// values differ from the snapshot — the machine is wearing SteamXBox's cursors and the only
    /// copy of the originals is a file that would go in the bin alongside them.
    /// </para>
    ///
    /// <para>
    /// A warning rather than an automatic restore. The theme was chosen on purpose and taking it
    /// away would be answering a question nobody asked; what was missing was the chance to answer it.
    /// </para>
    /// </remarks>
    private static string WindowsLeftBehind()
        => new SteamXBox.Shell.Theming.Windows.WindowsStateBackup().Exists
            ? "\n\nAttention : les curseurs de Windows ont été remplacés par SteamXBox. Ils le "
              + "resteront après une désinstallation, et l'enregistrement de vos curseurs d'origine "
              + "disparaîtrait avec le produit. Rendez-les depuis Paramètres › Thème Windows avant "
              + "de désinstaller si vous les voulez."
            : "";

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } box)
        {
            return;
        }

        // Refused rather than merely absent from the list. Switching off what SteamXBox is made of
        // would remove the tile that opens the screen it would be switched back on from.
        if (ToolRegistry.IsSystem(id))
        {
            Status.Text = $"« {id} » fait partie de SteamXBox et ne se désactive pas.";
            Refresh();
            return;
        }

        PluginLifecycle.SetEnabled(id, box.IsChecked == true, UiLog.Info);
        Refresh();
    }

    /// <summary>Packs a tool away, or unpacks one that already is.</summary>
    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        var done = PluginLifecycle.HasArchive(Root, id) && !Directory.Exists(Path.Combine(Root, id))
            ? PluginLifecycle.Restore(Root, id, UiLog.Info, Corps(id))
            : PluginLifecycle.Archive(Root, id, UiLog.Info, Corps(id));

        Status.Text = done ? "" : "L'opération n'a pas abouti ; le journal en dit la raison.";
        Refresh();
    }

    /// <summary>
    /// Removes a tool, after asking.
    /// </summary>
    /// <remarks>
    /// The only one of the three that destroys anything, so it is the only one that asks. The
    /// question says whether an archive survives, because that is what decides whether the answer
    /// is reversible.
    /// </remarks>
    private void Eject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        // Checked here as well as hidden in the list. A hidden button is a presentation choice; the
        // rule is that SteamXBox does not delete what it shipped, and a rule enforced only by what
        // is drawn survives exactly until the drawing changes.
        if (PluginTools.Shipped.Contains(id)
            || ToolRegistry.IsSystem(id)
            || ToolRegistry.Builtin.Any(tool => tool.Id == id))
        {
            Status.Text = $"« {id} » est livré avec SteamXBox : il peut être désactivé ou archivé, "
                + "mais son dossier se supprime depuis l'explorateur, pas d'ici.";
            return;
        }

        var archived = PluginLifecycle.HasArchive(Root, id);
        var installed = Directory.Exists(Path.Combine(Root, id));

        var question = installed
            ? archived
                ? $"Supprimer le dossier de « {id} » ? Son archive est conservée."
                : $"Supprimer « {id} » ? Il n'en existe aucune archive : l'opération est définitive."
            : $"Supprimer définitivement l'archive de « {id} » ?";

        if (MessageBox.Show(question, "SteamXBox", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK)
        {
            return;
        }

        if (installed)
        {
            PluginLifecycle.Delete(Root, id, UiLog.Info, Corps(id), Desinstallation(id));
        }
        else
        {
            PluginLifecycle.Forget(Root, id, UiLog.Info);
        }

        Refresh();
    }

    /// <summary>One line of the list.</summary>
    /// <param name="Compiled">Built into the product: no folder, so nothing to archive or remove.</param>
    /// <param name="Protected">Shipped with SteamXBox: reversible operations only.</param>
    private sealed record Row(PluginEntry Entry, bool Archived, bool Compiled, bool Protected)
    {
        public string Id => Entry.Id;

        public string Name => Entry.Name;

        /// <summary>
        /// The same wording for everything SteamXBox ships, whether compiled in or loaded.
        /// </summary>
        /// <remarks>
        /// One status, because from where the user stands there is one category: tools that came
        /// with the product. Two labels for the same thing invited the question of what the
        /// difference was, and the honest answer — whether the code happens to sit in the executable
        /// — is none of their business.
        ///
        /// <para>
        /// The one difference that is real is said outright: a compiled tool has no folder, so there
        /// is nothing to compress. Saying it beats hiding a button and letting them wonder.
        /// </para>
        /// </remarks>
        public string Detail => string.Join("  ·  ", new[]
        {
            Protected ? "outil fourni par défaut" : "",
            Compiled ? "sans dossier, non archivable" : "",
            Entry.Version.Length > 0 ? "v" + Entry.Version : "",
            Entry.State switch
            {
                PluginState.Archived => "archivé",
                PluginState.Disabled => "désactivé",
                _ => "actif",
            },
            Size(Entry.Bytes),
        }.Where(part => part.Length > 0));

        /// <summary>A box for an archive: packing away. An open box: bringing it back.</summary>
        public string ArchiveGlyph => Entry.State == PluginState.Archived ? "" : "";

        public string ArchiveHint => Entry.State == PluginState.Archived
            ? "Restaurer l'outil depuis son archive"
            : "Compresser l'outil et libérer la place";

        /// <summary>A compiled tool has no folder to compress.</summary>
        public Visibility ArchiveVisibility => Compiled ? Visibility.Hidden : Visibility.Visible;

        /// <summary>
        /// Hidden for anything that ships with the product.
        /// </summary>
        /// <remarks>
        /// Hidden rather than greyed out. A disabled button invites the question "why not", and the
        /// answer would have to be given again on every default tool; what the screen will not do,
        /// it does not offer. The line under the list says where the door is for somebody who means
        /// it.
        /// </remarks>
        public Visibility EjectVisibility => Compiled || Protected ? Visibility.Hidden : Visibility.Visible;

        public bool IsEnabled => Entry.State == PluginState.Enabled;

        /// <summary>An archived tool has nothing to switch: it is not there to run.</summary>
        public bool CanSwitch => Entry.State != PluginState.Archived;

        public string SwitchLabel => Entry.State == PluginState.Archived ? "archivé" : "activé";

        private static string Size(long bytes) => bytes switch
        {
            0 => "",
            < 1024 => $"{bytes} o",
            < 1024 * 1024 => $"{bytes / 1024} Ko",
            _ => $"{bytes / 1024 / 1024} Mo",
        };
    }
}
