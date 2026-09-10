using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SenSÉ.EditeurTexte.Edition;
using SenSÉ.EditeurTexte.Format;
using SenSÉ.EditeurTexte.Persistance;

namespace SenSÉ.EditeurTexte.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<OngletDocument> _onglets = new();
    private readonly AutoSave _autoSave = new();
    private readonly RelayCommand _cmdNouveau;
    private readonly RelayCommand _cmdOuvrir;
    private readonly RelayCommand _cmdEnregistrer;
    private readonly RelayCommand _cmdAnnuler;
    private readonly RelayCommand _cmdRetablir;
    private readonly RelayCommand _cmdGras;
    private readonly RelayCommand _cmdItalique;
    private readonly RelayCommand _cmdSouligne;
    private readonly RelayCommand _cmdFermerOnglet;
    private FenetreRecherche? _fenetreRecherche;

    public MainWindow()
    {
        InitializeComponent();
        Onglets.ItemsSource = _onglets;

        _cmdNouveau       = new RelayCommand(_ => CreerNouvelOnglet());
        _cmdOuvrir        = new RelayCommand(_ => OuvrirDansNouvelOnglet());
        _cmdEnregistrer   = new RelayCommand(_ => EnregistrerOngletActif(), () => OngletActif() is not null);
        _cmdAnnuler       = new RelayCommand(_ => AnnulerOngletActif(),   () => OngletActif()?.Historique.PeutAnnuler ?? false);
        _cmdRetablir      = new RelayCommand(_ => RetablirOngletActif(),  () => OngletActif()?.Historique.PeutRetablir ?? false);
        _cmdGras          = new RelayCommand(_ => Toggle(Inline.FontWeightProperty, FontWeights.Bold));
        _cmdItalique      = new RelayCommand(_ => Toggle(Inline.FontStyleProperty, FontStyles.Italic));
        _cmdSouligne      = new RelayCommand(_ => Toggle(Inline.TextDecorationsProperty, TextDecorations.Underline));
        _cmdFermerOnglet  = new RelayCommand(_ => FermerOngletActif());

        DataContext = this;

        TailleCombo.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(TailleCombo_TextChanged));

        _autoSave.Demarrer();
        this.Closed += (_, _) => _autoSave.Arreter();

        // Phase I.2 : charger le theme depuis settings.json et l'appliquer.
        var theme = ThemeManager.Charger();
        ThemeManager.Appliquer(this, theme);
        SyncRadioTheme(theme);

        CreerNouvelOnglet();
    }

    public ICommand MnuNouveau       => _cmdNouveau;
    public ICommand MnuOuvrir        => _cmdOuvrir;
    public ICommand MnuEnregistrer   => _cmdEnregistrer;
    public ICommand MnuAnnuler       => _cmdAnnuler;
    public ICommand MnuRetablir      => _cmdRetablir;
    public ICommand MnuGras          => _cmdGras;
    public ICommand MnuItalique      => _cmdItalique;
    public ICommand MnuSouligne      => _cmdSouligne;
    public ICommand MnuFermerOnglet  => _cmdFermerOnglet;

    // ============== Gestion des onglets ==============

    public OngletDocument? OngletActif() => Onglets.SelectedItem as OngletDocument;

    public System.Windows.Controls.RichTextBox? RtbActif() => OngletActif()?.RtbInterne;

    private void CreerNouvelOnglet()
    {
        var onglet = new OngletDocument();
        _onglets.Add(onglet);
        Onglets.SelectedItem = onglet;
        onglet.Focus();
    }

    private void OuvrirDansNouvelOnglet()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|Texte (*.txt)|*.txt|Word (*.docx)|*.docx|OpenDocument (*.odt)|*.odt|Tous les fichiers (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        ChargerDansNouvelOnglet(dlg.FileName);
    }

    private void ChargerDansNouvelOnglet(string chemin)
    {
        try
        {
            var onglet = new OngletDocument();
            Chargeur.Charger(chemin, onglet.RtbInterne.Document);
            onglet.Chemin = chemin;
            onglet.MarquerSauvegarde();
            onglet.Historique.Reset();
            _onglets.Add(onglet);
            Onglets.SelectedItem = onglet;
            Statut.Text = "Ouvert : " + Path.GetFileName(chemin);
            // Phase H : proposer restauration si autosave plus recent.
            ProposerRestaurationAutoSave(onglet, chemin);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'ouverture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool FermerOnglet(OngletDocument onglet)
    {
        if (!onglet.EstSauvegarde)
        {
            var name = onglet.Titre;
            var r = MessageBox.Show(this,
                name + " a des modifications non sauvegardees. Sauver avant de fermer ?",
                "Fermer l'onglet",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
            {
                // Selectionner l'onglet et sauver.
                Onglets.SelectedItem = onglet;
                if (!SauverOnglet(onglet)) return false;
            }
        }
        _onglets.Remove(onglet);
        if (_onglets.Count == 0) CreerNouvelOnglet();
        return true;
    }

    private void FermerOngletActif()
    {
        if (OngletActif() is { } o) FermerOnglet(o);
    }

    private void Onglets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Desabonne les anciens onglets.
        foreach (var ancien in e.RemovedItems.OfType<OngletDocument>())
        {
            ancien.SelectionChangee -= Onglet_SelectionChangee;
            ancien.EstModifie -= Onglet_EstModifie;
        }
        // Phase H : la AutoSave suit l'onglet actif.
        if (OngletActif() is { } o)
        {
            o.SelectionChangee += Onglet_SelectionChangee;
            o.EstModifie += Onglet_EstModifie;
            _autoSave.CheminCourant = o.Chemin;
            _autoSave.Document = o.RtbInterne.Document;
            Title = o.Chemin is null ? "Éditeur — SenSÉ" : "Éditeur — SenSÉ — " + Path.GetFileName(o.Chemin);
            o.Historique.Push(o.RtbInterne.Document); // etat initial
        }
        else
        {
            _autoSave.CheminCourant = null;
            _autoSave.Document = null;
            Title = "Éditeur — SenSÉ";
        }
        // Phase I.1 bugfix : syncs combobox + comptage sur changement d'onglet.
        SyncCombosAvecSelection();
        MettreAJourComptage();
    }

    private void Onglet_SelectionChangee(object? sender, EventArgs e)
    {
        SyncCombosAvecSelection();
        MettreAJourComptage();
    }

    private void Onglet_EstModifie(object? sender, EventArgs e)
    {
        if (sender is OngletDocument o) o.Historique.Push(o.RtbInterne.Document);
        MettreAJourComptage();
    }

    private void BtnFermerOnglet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is OngletDocument o) FermerOnglet(o);
    }

    // ============== Sauvegarde ==============

    private void EnregistrerOngletActif()
    {
        if (OngletActif() is { } o) SauverOnglet(o);
    }

    private bool SauverOnglet(OngletDocument onglet)
    {
        if (string.IsNullOrEmpty(onglet.Chemin)) return SauverOngletSous(onglet);
        return SauverOngletA(onglet, onglet.Chemin!);
    }

    private void MnuEnregistrerSous_Click(object sender, RoutedEventArgs e)
    {
        if (OngletActif() is { } o) SauverOngletSous(o);
    }

    private bool SauverOngletSous(OngletDocument onglet)
    {
        var dlg = new SaveFileDialog
        {
            Filter = ConstruireFilterSauvegarde(),
            FilterIndex = onglet.Chemin is null ? 1 : IndexExtension(Path.GetExtension(onglet.Chemin)),
            FileName = onglet.Chemin is null ? "sans-titre.md" : Path.GetFileName(onglet.Chemin),
        };
        if (dlg.ShowDialog(this) != true) return false;
        return SauverOngletA(onglet, dlg.FileName);
    }

    private bool SauverOngletA(OngletDocument onglet, string chemin)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        if (ext != ".md" && ext != ".txt" && ext != ".docx" && ext != ".odt")
        {
            MessageBox.Show(this, "Format " + ext + " non supporte.", "Format non supporte",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try
        {
            // Archive l'ancien fichier si on ecrase un fichier deja existant.
            if (File.Exists(chemin)) Persistance.Historique.Archiver(chemin);
            Sauvegardeur.Sauvegarder(onglet.RtbInterne.Document, chemin);
            onglet.Chemin = chemin;
            onglet.MarquerSauvegarde();
            AutoSave.Supprimer(chemin);
            if (OngletActif() == onglet) Title = "Éditeur — SenSÉ — " + Path.GetFileName(chemin);
            Statut.Text = ext switch
            {
                ".docx" => "Enregistré en Word (.docx).",
                ".odt"  => "Enregistré en OpenDocument (.odt).",
                _       => "Enregistré.",
            };
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'enregistrement", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string ConstruireFilterSauvegarde()
    {
        return "Markdown (*.md)|*.md|Texte (*.txt)|*.txt|Word (*.docx)|*.docx|OpenDocument (*.odt)|*.odt|Tous les fichiers (*.*)|*.*";
    }

    private static int IndexExtension(string ext)
    {
        var low = ext.ToLowerInvariant();
        string[] exts = { ".md", ".txt", ".docx", ".odt" };
        for (int i = 0; i < exts.Length; i++) if (exts[i] == low) return i + 2;
        return 1;
    }

    // ============== Handlers toolbar / edition (operent sur l'onglet actif) ==============

    private void MnuQuitter_Click(object sender, RoutedEventArgs e) => Close();

    private void MnuNouveau_Click(object sender, RoutedEventArgs e) => CreerNouvelOnglet();
    private void MnuOuvrir_Click(object sender, RoutedEventArgs e) => OuvrirDansNouvelOnglet();
    private void MnuEnregistrer_Click(object sender, RoutedEventArgs e) => EnregistrerOngletActif();
    private void MnuFermerOnglet_Click(object sender, RoutedEventArgs e) => FermerOngletActif();
    private void MnuAnnuler_Click(object sender, RoutedEventArgs e) => AnnulerOngletActif();
    private void MnuRetablir_Click(object sender, RoutedEventArgs e) => RetablirOngletActif();

    private void AnnulerOngletActif() { if (OngletActif() is { } o) o.Historique.Undo(o.RtbInterne.Document); }
    private void RetablirOngletActif() { if (OngletActif() is { } o) o.Historique.Redo(o.RtbInterne.Document); }

    private void BtnGras_Click(object sender, RoutedEventArgs e)        => Toggle(Inline.FontWeightProperty, FontWeights.Bold);
    private void BtnItalique_Click(object sender, RoutedEventArgs e)    => Toggle(Inline.FontStyleProperty, FontStyles.Italic);
    private void BtnSouligne_Click(object sender, RoutedEventArgs e)    => Toggle(Inline.TextDecorationsProperty, TextDecorations.Underline);
    private void BtnH1_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(1);
    private void BtnH2_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(2);
    private void BtnH3_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(3);
    private void BtnListePuces_Click(object sender, RoutedEventArgs e) { if (RtbActif() is { } r) { EditingCommands.ToggleBullets.Execute(null, r); r.Focus(); } }
    private void BtnListeNum_Click(object sender, RoutedEventArgs e)   { if (RtbActif() is { } r) { EditingCommands.ToggleNumbering.Execute(null, r); r.Focus(); } }

    private void BtnAlignerGauche_Click(object sender, RoutedEventArgs e) { if (RtbActif() is { } r) { EditingCommands.AlignLeft.Execute(null, r);    r.Focus(); } }
    private void BtnCentrer_Click(object sender, RoutedEventArgs e)       { if (RtbActif() is { } r) { EditingCommands.AlignCenter.Execute(null, r);  r.Focus(); } }
    private void BtnAlignerDroite_Click(object sender, RoutedEventArgs e) { if (RtbActif() is { } r) { EditingCommands.AlignRight.Execute(null, r);   r.Focus(); } }
    private void BtnJustifier_Click(object sender, RoutedEventArgs e)     { if (RtbActif() is { } r) { EditingCommands.AlignJustify.Execute(null, r); r.Focus(); } }

    private void PoliceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _ignoreComboboxEvents) return;
        if (PoliceCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var name = item.Content?.ToString();
        if (string.IsNullOrEmpty(name) || RtbActif() is not { } rtb) return;
        try
        {
            var ff = new FontFamily(name);
            if (rtb.Selection.IsEmpty) rtb.FontFamily = ff;
            else rtb.Selection.ApplyPropertyValue(Inline.FontFamilyProperty, ff);
            rtb.Focus();
        }
        catch (Exception ex) { Statut.Text = "Police : " + ex.Message; }
    }

    private void TailleCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _ignoreComboboxEvents) return;
        if (!double.TryParse(TailleCombo.Text, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var size)) return;
        if (size <= 0 || size > 999) return;
        if (RtbActif() is not { } rtb) return;
        if (rtb.Selection.IsEmpty) rtb.Selection.Start?.Paragraph?.SetCurrentValue(Paragraph.FontSizeProperty, size);
        else rtb.Selection.ApplyPropertyValue(Inline.FontSizeProperty, size);
    }

    private void BtnRechercher_Click(object sender, RoutedEventArgs e) => OuvrirFenetreRecherche(false);
    private void BtnRemplacer_Click(object sender, RoutedEventArgs e)  => OuvrirFenetreRecherche(true);

    private void OuvrirFenetreRecherche(bool modeRemplacement)
    {
        if (RtbActif() is not { } rtb) return;
        if (_fenetreRecherche is null)
        {
            _fenetreRecherche = new FenetreRecherche(rtb) { Owner = this };
            _fenetreRecherche.Closed += (_, _) => _fenetreRecherche = null;
        }
        _fenetreRecherche.ModeRemplacement = modeRemplacement;
        if (!_fenetreRecherche.IsVisible) _fenetreRecherche.Show();
        _fenetreRecherche.Activate();
        _fenetreRecherche.Focus();
    }

    private void Toggle(DependencyProperty prop, object value)
    {
        if (RtbActif() is not { } rtb || rtb.Selection.IsEmpty) return;
        var current = rtb.Selection.GetPropertyValue(prop);
        object? newValue = DependencyProperty.UnsetValue;
        if (current == DependencyProperty.UnsetValue || !Equals(current, value)) newValue = value;
        rtb.Selection.ApplyPropertyValue(prop, newValue);
    }

    private void AppliquerTitre(int niveau)
    {
        if (OngletActif() is not { } o) return;
        var p = o.RtbInterne.Selection.Start?.Paragraph;
        if (p is null) return;
        p.FontSize = niveau switch { 1 => 24.0, 2 => 18.0, 3 => 14.0, _ => 12.0 };
        p.FontWeight = niveau == 1 ? FontWeights.Bold : FontWeights.Normal;
    }

    // Phase I.1 bugfix : flag pour eviter la boucle SyncCombos <-> ComboBox.SelectionChanged.
    private bool _ignoreComboboxEvents;

    private void SyncCombosAvecSelection()
    {
        if (OngletActif() is not { } o) return;
        var rtb = o.RtbInterne;
        _ignoreComboboxEvents = true;
        try
        {
            // Police.
            var ffObj = rtb.Selection.GetPropertyValue(Inline.FontFamilyProperty);
            if (ffObj is FontFamily ff)
            {
                var source = ff.Source ?? ff.ToString();
                foreach (var it in PoliceCombo.Items)
                {
                    if (it is System.Windows.Controls.ComboBoxItem ci && string.Equals(ci.Content?.ToString(), source, StringComparison.OrdinalIgnoreCase))
                    {
                        PoliceCombo.SelectedItem = ci;
                        break;
                    }
                }
            }
            // Taille.
            var fsObj = rtb.Selection.GetPropertyValue(Inline.FontSizeProperty);
            if (fsObj is double size && size > 0)
                TailleCombo.Text = size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _ignoreComboboxEvents = false;
        }
    }

    private void MettreAJourComptage()
    {
        if (OngletActif() is not { } o)
        {
            Comptage.Text = "0 mots | 0 caractères";
            return;
        }
        var texte = new TextRange(o.RtbInterne.Document.ContentStart, o.RtbInterne.Document.ContentEnd).Text;
        var mots = string.IsNullOrWhiteSpace(texte)
            ? 0
            : texte.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var caracteres = texte.Length;
        Comptage.Text = mots + " mots | " + caracteres + " caractères";
    }

    // ============== Phase I.2 : Themes ==============

    private void MnuThemeClair_Click(object sender, RoutedEventArgs e) => ChangerTheme(ThemeApp.Light);
    private void MnuThemeSombre_Click(object sender, RoutedEventArgs e) => ChangerTheme(ThemeApp.Dark);
    private void MnuThemeSysteme_Click(object sender, RoutedEventArgs e) => ChangerTheme(ThemeApp.SystemDefault);

    private void ChangerTheme(ThemeApp theme)
    {
        ThemeManager.Appliquer(this, theme);
        ThemeManager.Sauvegarder(theme);
        SyncRadioTheme(theme);
        Statut.Text = "Theme : " + theme;
    }

    private void SyncRadioTheme(ThemeApp theme)
    {
        MnuThemeClair.IsChecked = theme == ThemeApp.Light;
        MnuThemeSombre.IsChecked = theme == ThemeApp.Dark;
        MnuThemeSysteme.IsChecked = theme == ThemeApp.SystemDefault;
    }

    // ============== Drag & drop ==============

    private void Fenetre_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static readonly HashSet<string> _extensionsImage =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private void Fenetre_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var fichiers = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (fichiers is null || fichiers.Length == 0) return;
        foreach (var fichier in fichiers)
        {
            var ext = Path.GetExtension(fichier);
            if (_extensionsImage.Contains(ext)) InsererImage(fichier);
            else if (fichiers.Length == 1 && !_extensionsImage.Contains(ext))
            {
                ChargerDansNouvelOnglet(fichier);
                return;
            }
        }
    }

    private void InsererImageLocale_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Tous les fichiers (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        InsererImage(dlg.FileName);
    }

    private void InsererImage(string cheminImage, string? legende = null)
    {
        if (RtbActif() is not { } rtb) return;
        try
        {
            if (!File.Exists(cheminImage))
            {
                MessageBox.Show(this, "Image introuvable : " + cheminImage,
                    "Insertion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var img = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri(cheminImage, UriKind.Absolute)),
                MaxWidth = 600,
                Stretch = System.Windows.Media.Stretch.Uniform,
            };
            var pos = rtb.Selection.Start ?? rtb.CaretPosition;
            if (pos is null || pos.Paragraph is null) return;
            new InlineUIContainer(img, pos);
            Statut.Text = "Image inseree : " + Path.GetFileName(cheminImage);
            rtb.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'insertion d'image",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ============== IA / Dicter ==============

    private async void DemanderIa_Click(object sender, RoutedEventArgs e)
    {
        if (RtbActif() is not { } rtb) return;
        var selection = rtb.Selection.IsEmpty
            ? new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text
            : rtb.Selection.Text;
        var dlg = new FenetrePromptIA(selection);
        if (dlg.ShowDialog() != true) return;
        Statut.Text = "IA : envoi a l'Atelier...";
        try
        {
            using var cli = new Integration.ClientAtelier();
            var reponse = await cli.CompleterAsync(dlg.PromptSaisi + "\n\nContexte :\n" + selection, 2048);
            rtb.CaretPosition.InsertTextInRun(reponse + "\n");
            Statut.Text = "IA : reponse inseree.";
        }
        catch (Exception ex)
        {
            Statut.Text = "IA : erreur.";
            MessageBox.Show(this, ex.Message, "Demander a l'IA", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Dicter_Click(object sender, RoutedEventArgs e)
    {
        Statut.Text = "Dicter : envoi a l'Atelier...";
        try
        {
            using var cli = new Integration.ClientAtelier();
            await cli.TranscrireAsync(System.Array.Empty<byte>(), "fr");
            Statut.Text = "Dicter : transcription inseree.";
        }
        catch (NotImplementedException ex)
        {
            Statut.Text = "Dicter : pas encore branche.";
            MessageBox.Show(this, ex.Message, "Dicter", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Statut.Text = "Dicter : erreur.";
            MessageBox.Show(this, ex.Message, "Dicter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ============== Historique / AutoSave ==============

    private void MnuVoirHistorique_Click(object sender, RoutedEventArgs e)
    {
        if (OngletActif()?.Chemin is not { } chemin)
        {
            MessageBox.Show(this, "Aucun fichier associe a cet onglet. Sauvegardez d'abord.",
                "Historique", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var fen = new FenetreHistorique(chemin) { Owner = this };
        fen.ShowDialog();
    }

    private void ProposerRestaurationAutoSave(OngletDocument onglet, string chemin)
    {
        if (!AutoSave.ExisteRestauration(chemin, out var modifieLe, out var autosavePath)) return;
        var dateFichierDisque = File.Exists(chemin) ? File.GetLastWriteTime(chemin) : DateTime.MinValue;
        if (modifieLe <= dateFichierDisque) return;
        var age = DateTime.Now - modifieLe;
        var minutes = (int)Math.Round(age.TotalMinutes);
        var label = minutes < 1 ? "moins d'1 min" : minutes + " min";
        var result = MessageBox.Show(this,
            "Une version non sauvegardee existe pour " + Path.GetFileName(chemin) +
            " (modifiee il y a " + label + ")." + Environment.NewLine + Environment.NewLine + "Restaurer ?",
            "Restauration auto-save",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var contenu = AutoSave.Lire(autosavePath);
            onglet.RtbInterne.Document = new FlowDocument();
            Format.Markdown.DepuisMarkdown(onglet.RtbInterne.Document, contenu);
            onglet.Historique.Reset();
            onglet.EstSauvegarde = false;
            Statut.Text = "Restauration auto-save appliquee (non encore sauvegarde).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur de restauration",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Verifier tous les onglets modifies avant de quitter.
        foreach (var o in _onglets.ToList())
        {
            if (!o.EstSauvegarde)
            {
                var r = MessageBox.Show(this,
                    o.Titre + " a des modifications non sauvegardees. Sauver avant de quitter ?",
                    "Quitter",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes)
                {
                    Onglets.SelectedItem = o;
                    if (!SauverOnglet(o)) { e.Cancel = true; return; }
                }
            }
        }
        base.OnClosing(e);
    }
}

/// <summary>Fenetre modale pour saisir un prompt IA.</summary>
internal sealed class FenetrePromptIA : Window
{
    public string PromptSaisi { get; private set; } = "";
    public FenetrePromptIA(string contexte)
    {
        Title = "Demander a l'IA"; Width = 640; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var racine = new DockPanel { Margin = new Thickness(12) };
        var label = new TextBlock { Text = "Que voulez-vous demander a l'IA ?", Margin = new Thickness(0,0,0,6) };
        DockPanel.SetDock(label, Dock.Top); racine.Children.Add(label);
        var prompt = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, AcceptsTab = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 160 };
        DockPanel.SetDock(prompt, Dock.Top); racine.Children.Add(prompt);
        var ctx = new TextBlock { Text = "Contexte : " + (string.IsNullOrEmpty(contexte) ? "(document vide)" : contexte.Length + " caracteres"), Margin = new Thickness(0,6,0,0), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        DockPanel.SetDock(ctx, Dock.Top); racine.Children.Add(ctx);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        DockPanel.SetDock(boutons, Dock.Bottom);
        var btnOk = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(0,0,8,0), IsDefault = true };
        btnOk.Click += (_, _) => { PromptSaisi = prompt.Text ?? ""; DialogResult = true; Close(); };
        var btnAnn = new Button { Content = "Annuler", Width = 80, Height = 28, IsCancel = true };
        btnAnn.Click += (_, _) => { DialogResult = false; Close(); };
        boutons.Children.Add(btnAnn); boutons.Children.Add(btnOk);
        racine.Children.Add(boutons);
        Content = racine; Loaded += (_, _) => prompt.Focus();
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Func<bool>? _canExec;
    public RelayCommand(Action<object?> exec, Func<bool>? canExec = null) { _exec = exec; _canExec = canExec; }
    public bool CanExecute(object? parameter) => _canExec?.Invoke() ?? true;
    public void Execute(object? parameter) => _exec(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
