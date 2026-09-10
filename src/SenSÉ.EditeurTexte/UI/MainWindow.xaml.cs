using System;
using System.IO;
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
    private string? _cheminActuel;
    private readonly Historique _historique = new();
    private readonly RelayCommand _cmdNouveau;
    private readonly RelayCommand _cmdOuvrir;
    private readonly RelayCommand _cmdEnregistrer;
    private readonly RelayCommand _cmdAnnuler;
    private readonly RelayCommand _cmdRetablir;
    private readonly RelayCommand _cmdGras;
    private readonly RelayCommand _cmdItalique;
    private readonly RelayCommand _cmdSouligne;

    // Phase G.1a : la fenetre de recherche/remplacement est creee paresseusement
    // pour qu'elle survive entre deux ouvertures (historique des recherches).
    private FenetreRecherche? _fenetreRecherche;

    public MainWindow()
    {
        InitializeComponent();

        _cmdNouveau    = new RelayCommand(_ => Nouveau());
        _cmdOuvrir     = new RelayCommand(_ => Ouvrir());
        _cmdEnregistrer = new RelayCommand(_ => Enregistrer());
        _cmdAnnuler    = new RelayCommand(_ => _historique.Undo(Rtb.Document),    () => _historique.PeutAnnuler);
        _cmdRetablir   = new RelayCommand(_ => _historique.Redo(Rtb.Document),    () => _historique.PeutRetablir);
        _cmdGras       = new RelayCommand(_ => Toggle(Inline.FontWeightProperty, FontWeights.Bold));
        _cmdItalique   = new RelayCommand(_ => Toggle(Inline.FontStyleProperty,  FontStyles.Italic));
        _cmdSouligne   = new RelayCommand(_ => Toggle(Inline.TextDecorationsProperty, TextDecorations.Underline));

        DataContext = this;

        // Phase G.1a : la ComboBox editable ne supporte pas TextChanged en XAML directement.
        // On s'abonne au routed event TextBoxBase.TextChangedEvent qui remonte du TextBox interne.
        TailleCombo.AddHandler(TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(TailleCombo_TextChanged));

        // Phase G.1a : etat initial de la combobox Taille sur la valeur par defaut
        // de l'editeur (12pt).
        TailleCombo.Text = Rtb.FontSize > 0 ? Rtb.FontSize.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "12";
    }

    public ICommand MnuNouveau    => _cmdNouveau;
    public ICommand MnuOuvrir     => _cmdOuvrir;
    public ICommand MnuEnregistrer => _cmdEnregistrer;
    public ICommand MnuAnnuler    => _cmdAnnuler;
    public ICommand MnuRetablir   => _cmdRetablir;
    public ICommand MnuGras       => _cmdGras;
    public ICommand MnuItalique   => _cmdItalique;
    public ICommand MnuSouligne   => _cmdSouligne;

    private void Nouveau()
    {
        _cheminActuel = null;
        Rtb.Document = new FlowDocument(new Paragraph());
        _historique.Reset();
        Title = "Éditeur — SenSÉ";
    }

    private void Ouvrir()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown (*.md)|*.md|Texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        Charger(dlg.FileName);
    }

    private void Charger(string chemin)
    {
        try
        {
            Chargeur.Charger(chemin, Rtb.Document);
            _cheminActuel = chemin;
            _historique.Reset();
            Title = $"Éditeur — SenSÉ — {Path.GetFileName(chemin)}";
            Statut.Text = "Ouvert.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'ouverture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Enregistrer()
    {
        if (_cheminActuel is null) EnregistrerSous();
        else Sauver(_cheminActuel);
    }

    private void EnregistrerSous()
    {
        var dlg = new SaveFileDialog
        {
            Filter = ConstruireFilterSauvegarde(),
            FilterIndex = _cheminActuel is null ? 1 : IndexExtension(Path.GetExtension(_cheminActuel)),
            FileName = _cheminActuel is null ? "sans-titre.md" : Path.GetFileName(_cheminActuel),
        };
        if (dlg.ShowDialog(this) != true) return;
        Sauver(dlg.FileName);
    }

    private static readonly (string Extension, string Id)[] _formatsSauvegarde =
        new (string, string)[] { (".md", "md"), (".txt", "txt"), (".docx", "docx"), (".odt", "odt") };

    private static string ConstruireFilterSauvegarde()
    {
        return "Markdown (*.md)|*.md|Texte (*.txt)|*.txt|Word (*.docx)|*.docx|OpenDocument (*.odt)|*.odt|Tous les fichiers (*.*)|*.*";
    }

    private static int IndexExtension(string ext)
    {
        var low = ext.ToLowerInvariant();
        for (int i = 0; i < _formatsSauvegarde.Length; i++)
        {
            if (_formatsSauvegarde[i].Extension == low) return i + 2;
        }
        return 1;
    }

    private void Sauver(string chemin)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        if (ext != ".md" && ext != ".txt" && ext != ".docx" && ext != ".odt")
        {
            MessageBox.Show(this,
                "Format " + ext + " non encore supporte en ecriture native. Formats disponibles : .md, .txt, .docx, .odt.",
                "Format non supporte", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Sauvegardeur.Sauvegarder(Rtb.Document, chemin);
            _cheminActuel = chemin;
            Title = $"Éditeur — SenSÉ — {Path.GetFileName(chemin)}";
            Statut.Text = ext switch
            {
                ".docx" => "Enregistré en Word (.docx).",
                ".odt"  => "Enregistré en OpenDocument (.odt).",
                _       => "Enregistré."
            };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'enregistrement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MnuQuitter_Click(object sender, RoutedEventArgs e) => Close();

    private void MnuNouveau_Click(object sender, RoutedEventArgs e)         => Nouveau();
    private void MnuOuvrir_Click(object sender, RoutedEventArgs e)          => Ouvrir();
    private void MnuEnregistrer_Click(object sender, RoutedEventArgs e)     => Enregistrer();
    private void MnuEnregistrerSous_Click(object sender, RoutedEventArgs e) => EnregistrerSous();
    private void MnuAnnuler_Click(object sender, RoutedEventArgs e)         => _historique.Undo(Rtb.Document);
    private void MnuRetablir_Click(object sender, RoutedEventArgs e)        => _historique.Redo(Rtb.Document);

    private void BtnGras_Click(object sender, RoutedEventArgs e)        => Toggle(Inline.FontWeightProperty, FontWeights.Bold);
    private void BtnItalique_Click(object sender, RoutedEventArgs e)    => Toggle(Inline.FontStyleProperty, FontStyles.Italic);
    private void BtnSouligne_Click(object sender, RoutedEventArgs e)    => Toggle(Inline.TextDecorationsProperty, TextDecorations.Underline);
    private void BtnH1_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(1);
    private void BtnH2_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(2);
    private void BtnH3_Click(object sender, RoutedEventArgs e)         => AppliquerTitre(3);
    // Phase G.1b : EditingCommands.ToggleBullets/ToggleNumbering creent une vraie List WPF (Block > ListItem).
    // Le writer ecrit le <w:numPr> (docx) ou <text:list> (odt) correspondant.
    private void BtnListePuces_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleBullets.Execute(null, Rtb); Rtb.Focus(); }
    private void BtnListeNum_Click(object sender, RoutedEventArgs e)   { EditingCommands.ToggleNumbering.Execute(null, Rtb); Rtb.Focus(); }

    // Phase G.1a : combobox police/taille. Selection vide -> affecte le paragraphe
    // sous le curseur (ou la propriete par defaut du RichTextBox). Sinon ->
    // ApplyPropertyValue sur la selection.
    private void PoliceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _ignoreSelectionChangedCombos) return;
        if (PoliceCombo.SelectedItem is not ComboBoxItem item) return;
        var name = item.Content?.ToString();
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var ff = new FontFamily(name);
            if (Rtb.Selection.IsEmpty)
                Rtb.FontFamily = ff;
            else
                Rtb.Selection.ApplyPropertyValue(Inline.FontFamilyProperty, ff);
            Rtb.Focus();
        }
        catch (Exception ex)
        {
            Statut.Text = "Police : " + ex.Message;
        }
    }

    private void TailleCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _ignoreSelectionChangedCombos) return;
        if (!double.TryParse(TailleCombo.Text, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var size)) return;
        if (size <= 0 || size > 999) return;
        if (Rtb.Selection.IsEmpty)
        {
            Rtb.Selection.Start?.Paragraph?.SetCurrentValue(Paragraph.FontSizeProperty, size);
        }
        else
        {
            Rtb.Selection.ApplyPropertyValue(Inline.FontSizeProperty, size);
        }
    }

    // Phase G.1a : alignement via les EditingCommands natives WPF.
    private void BtnAlignerGauche_Click(object sender, RoutedEventArgs e) { EditingCommands.AlignLeft.Execute(null, Rtb);    Rtb.Focus(); }
    private void BtnCentrer_Click(object sender, RoutedEventArgs e)       { EditingCommands.AlignCenter.Execute(null, Rtb);  Rtb.Focus(); }
    private void BtnAlignerDroite_Click(object sender, RoutedEventArgs e) { EditingCommands.AlignRight.Execute(null, Rtb);   Rtb.Focus(); }
    private void BtnJustifier_Click(object sender, RoutedEventArgs e)     { EditingCommands.AlignJustify.Execute(null, Rtb); Rtb.Focus(); }

    // Phase G.1a : ouvre (ou remonte) la fenetre de recherche. L'implementation
    // complete (suivant, precedent, remplacer, tout remplacer) est branchee
    // en G.4. Pour l'instant on affiche juste la fenetre.
    private void BtnRechercher_Click(object sender, RoutedEventArgs e) => OuvrirFenetreRecherche(false);
    private void BtnRemplacer_Click(object sender, RoutedEventArgs e)  => OuvrirFenetreRecherche(true);

    private void OuvrirFenetreRecherche(bool modeRemplacement)
    {
        if (_fenetreRecherche is null)
        {
            _fenetreRecherche = new FenetreRecherche(Rtb) { Owner = this };
            _fenetreRecherche.Closed += (_, _) => _fenetreRecherche = null;
        }
        _fenetreRecherche.ModeRemplacement = modeRemplacement;
        if (!_fenetreRecherche.IsVisible) _fenetreRecherche.Show();
        _fenetreRecherche.Activate();
        _fenetreRecherche.Focus();
    }

    private void Toggle(DependencyProperty prop, object value)
    {
        if (Rtb.Selection.IsEmpty) return;
        var current = Rtb.Selection.GetPropertyValue(prop);
        object? newValue = DependencyProperty.UnsetValue;
        if (current == DependencyProperty.UnsetValue || !Equals(current, value))
        {
            newValue = value;
        }
        Rtb.Selection.ApplyPropertyValue(prop, newValue);
    }

    private void AppliquerTitre(int niveau)
    {
        var start = Rtb.Selection.Start;
        var paragraph = start.Paragraph;
        if (paragraph is null) return;
        paragraph.FontSize = niveau switch { 1 => 24.0, 2 => 18.0, 3 => 14.0, _ => 12.0 };
        paragraph.FontWeight = niveau == 1 ? FontWeights.Bold : FontWeights.Normal;
    }

    private void AppliquerListe(bool ordonnee)
    {
        var start = Rtb.Selection.Start;
        var paragraph = start.Paragraph;
        if (paragraph is null) return;
        var prefixe = ordonnee ? "1. " : "• ";
        var first = paragraph.Inlines.FirstInline;
        if (first is null) return;
        if (first is Run r && r.Text.StartsWith(prefixe)) return;
        paragraph.Inlines.InsertBefore(first, new Run(prefixe));
    }

    // Phase G.1a + G.5 : pousser dans l'historique sur modification, mettre a jour
    // le compteur mots/caracteres dans la status bar.
    private void Rtb_TextChanged(object sender, TextChangedEventArgs e)
    {
        _historique.Push(Rtb.Document);
        // Phase G.5 (commit suivant) : MettreAJourComptage();
    }

    // Phase G.5 : compteur mots/caracteres. Appele sur chaque TextChanged.
    private void MettreAJourComptage()
    {
        var texte = new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd).Text;
        var mots = string.IsNullOrWhiteSpace(texte)
            ? 0
            : texte.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var caracteres = texte.Length;
        Comptage.Text = $"{mots} mots | {caracteres} caractères";
    }

    // Phase G.1a : SelectionChanged -> resynchroniser les combobox Police/Taille avec
    // l'etat de la selection. Un flag empeche les handlers de Combobox de repondre
    // quand c'est nous qui les mettons a jour.
    private bool _ignoreSelectionChangedCombos;
    private void Rtb_SelectionChanged(object sender, RoutedEventArgs e)
    {
        SyncCombosAvecSelection();
    }

    private void SyncCombosAvecSelection()
    {
        _ignoreSelectionChangedCombos = true;
        try
        {
            // Police.
            var ffObj = Rtb.Selection.GetPropertyValue(Inline.FontFamilyProperty);
            if (ffObj is FontFamily ff)
            {
                var source = ff.Source ?? ff.ToString();
                foreach (var it in PoliceCombo.Items)
                {
                    if (it is ComboBoxItem ci && string.Equals(ci.Content?.ToString(), source, StringComparison.OrdinalIgnoreCase))
                    {
                        PoliceCombo.SelectedItem = ci;
                        break;
                    }
                }
            }
            // Taille.
            var fsObj = Rtb.Selection.GetPropertyValue(Inline.FontSizeProperty);
            if (fsObj is double size && size > 0)
            {
                TailleCombo.Text = size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _ignoreSelectionChangedCombos = false;
        }
    }

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
            if (_extensionsImage.Contains(ext))
            {
                InsererImage(fichier);
            }
            else if (fichiers.Length == 1 && !_extensionsImage.Contains(ext))
            {
                Charger(fichier);
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

            var pos = Rtb.Selection.Start ?? Rtb.CaretPosition;
            if (pos is null || pos.Paragraph is null) return;

            var container = new InlineUIContainer(img, pos);

            if (!string.IsNullOrEmpty(legende))
            {
                var posApres = pos.GetNextInsertionPosition(LogicalDirection.Forward);
                if (posApres is not null && posApres.Paragraph is not null)
                {
                    posApres.Paragraph.Inlines.Add(new Run(legende)
                    {
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.Gray,
                    });
                }
            }

            Statut.Text = "Image inseree : " + Path.GetFileName(cheminImage);
            Rtb.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur d'insertion d'image",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DemanderIa_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FenetrePromptIA(Rtb.Selection.IsEmpty
            ? new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd).Text
            : Rtb.Selection.Text);
        if (dlg.ShowDialog() != true) return;

        var prompt = dlg.PromptSaisi;
        var contexteComplet =
            (Rtb.Selection.IsEmpty
                ? new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd).Text
                : Rtb.Selection.Text);

        Statut.Text = "IA : envoi a l'Atelier...";
        try
        {
            using var cli = new Integration.ClientAtelier();
            var reponse = await cli.CompleterAsync(prompt + "\n\nContexte :\n" + contexteComplet, 2048);
            Rtb.CaretPosition.InsertTextInRun(reponse + "\n");
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
}

/// <summary>Fenetre modale simple pour saisir un prompt IA.</summary>
internal sealed class FenetrePromptIA : Window
{
    public string PromptSaisi { get; private set; } = "";

    public FenetrePromptIA(string contexte)
    {
        Title = "Demander a l'IA";
        Width = 640;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var racine = new DockPanel { Margin = new Thickness(12) };

        var label = new TextBlock
        {
            Text = "Que voulez-vous demander a l'IA ?",
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(label, Dock.Top);
        racine.Children.Add(label);

        var prompt = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsTab = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        DockPanel.SetDock(prompt, Dock.Top);
        prompt.Height = 160;
        racine.Children.Add(prompt);

        var contexteBloc = new TextBlock
        {
            Text = "Contexte : " + (string.IsNullOrEmpty(contexte) ? "(document vide)" : contexte.Length + " caracteres selectionnes ou document complet"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        };
        DockPanel.SetDock(contexteBloc, Dock.Top);
        racine.Children.Add(contexteBloc);

        var boutons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(boutons, Dock.Bottom);

        var btnOk = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        btnOk.Click += (_, _) => { PromptSaisi = prompt.Text ?? ""; DialogResult = true; Close(); };
        var btnAnnuler = new Button { Content = "Annuler", Width = 80, Height = 28, IsCancel = true };
        btnAnnuler.Click += (_, _) => { DialogResult = false; Close(); };

        boutons.Children.Add(btnAnnuler);
        boutons.Children.Add(btnOk);
        racine.Children.Add(boutons);

        var spacer = new System.Windows.Controls.TextBlock();
        DockPanel.SetDock(spacer, Dock.Top);
        racine.Children.Add(spacer);

        Content = racine;
        Loaded += (_, _) => prompt.Focus();
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
