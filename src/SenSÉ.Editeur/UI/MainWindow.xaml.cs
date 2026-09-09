using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SenSÉ.Editeur.Edition;
using SenSÉ.Editeur.Format;
using SenSÉ.Editeur.Persistance;

namespace SenSÉ.Editeur.UI;

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

    // Phase B-prime : md, txt (natif TextePlain/Markdown) et docx (natif DocumentFormat.OpenXml via Format.Docx).
    // odt arrive en Phase C-prime (ZIP+XML maison).
    private static readonly (string Extension, string Id)[] _formatsSauvegarde =
        new (string, string)[] { (".md", "md"), (".txt", "txt"), (".docx", "docx") };

    private static string ConstruireFilterSauvegarde()
    {
        // Phase B-prime : md, txt, docx (writer natif). odt en Phase C-prime.
        return "Markdown (*.md)|*.md|Texte (*.txt)|*.txt|Word (*.docx)|*.docx|Tous les fichiers (*.*)|*.*";
    }

    private static int IndexExtension(string ext)
    {
        var low = ext.ToLowerInvariant();
        for (int i = 0; i < _formatsSauvegarde.Length; i++)
        {
            if (_formatsSauvegarde[i].Extension == low) return i + 2; // +1 (Tous) +1 (1-based)
        }
        return 1; // defaut : "Tous"
    }

    private void Sauver(string chemin)
    {
        // Phase B-prime : md / txt / docx (natif) sont supportes. odt en Phase C-prime.
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        if (ext != ".md" && ext != ".txt" && ext != ".docx")
        {
            MessageBox.Show(this,
                "Format " + ext + " non encore supporte en ecriture native. Formats disponibles : .md, .txt, .docx (.odt en Phase C-prime).",
                "Format non supporte", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Sauvegardeur.Sauvegarder(Rtb.Document, chemin);
            _cheminActuel = chemin;
            Title = $"Éditeur — SenSÉ — {Path.GetFileName(chemin)}";
            Statut.Text = ext == ".docx" ? "Enregistré en Word (.docx)." : "Enregistré.";
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
    private void BtnListePuces_Click(object sender, RoutedEventArgs e) => AppliquerListe(false);
    private void BtnListeNum_Click(object sender, RoutedEventArgs e)   => AppliquerListe(true);

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

    private void Rtb_TextChanged(object sender, TextChangedEventArgs e)
    {
        _historique.Push(Rtb.Document);
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

        // Premier fichier texte -> on charge, comme avant.
        // Tous les fichiers image -> on insere au point d'insertion, dans l'ordre.
        // Autres (PDF, Office, etc.) -> ignores silencieusement.
        foreach (var fichier in fichiers)
        {
            var ext = Path.GetExtension(fichier);
            if (_extensionsImage.Contains(ext))
            {
                InsererImage(fichier);
            }
            else if (fichiers.Length == 1 && !_extensionsImage.Contains(ext))
            {
                // Drop d'un seul fichier non-image : on tente de charger comme doc.
                Charger(fichier);
                return;
            }
            // Sinon, on ignore : drop multi-fichiers heterogene, on prend que les images.
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

    /// <summary>
    /// Insere une image au point d'insertion du RichTextBox, avec une legende
    /// optionnelle en italique gris juste apres.
    /// </summary>
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

            // Selection.Start peut etre null si le document est vide et non focalise.
            // On prend CaretPosition en fallback.
            var pos = Rtb.Selection.Start ?? Rtb.CaretPosition;
            if (pos is null || pos.Paragraph is null) return;

            var container = new InlineUIContainer(img, pos);

            if (!string.IsNullOrEmpty(legende))
            {
                // Avance la position apres l'image, insere un Run italique gris.
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

    private void DemanderIa_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FenetrePromptIA(Rtb.Selection.IsEmpty
            ? new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd).Text
            : Rtb.Selection.Text);
        if (dlg.ShowDialog() != true) return;

        var prompt = dlg.PromptSaisi;
        var contexte = new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd).Text;
        var nbCaracteres = contexte.Length;

        // [TODO Phase D2] Remplacer par appel Atelier.EnvoyerPromptAsync(prompt, contexte)
        // quand l'endpoint LLM stable de l'Atelier sera expose.
        MessageBox.Show(this,
            $"Stub : envoyerais a l'Atelier le prompt « {prompt} » avec contexte de {nbCaracteres} caracteres.",
            "Demander a l'IA (stub)",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Dicter_Click(object sender, RoutedEventArgs e)
    {
        // [TODO Phase D2] Remplacer par enregistrement micro + envoi Whisper + insertion
        // de la transcription dans le FlowDocument au CaretPosition.
        MessageBox.Show(this,
            "Stub : demarrerait l'enregistrement micro, enverrait a Whisper, insererait la transcription.",
            "Dicter (stub)",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>Fenetre modale simple pour saisir un prompt IA. Renvoie DialogResult=true
/// si OK, false si Annuler. La prompt est accessible via <see cref="PromptSaisi"/>.</summary>
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

        var btnOk = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        btnOk.Click += (_, _) =>
        {
            PromptSaisi = prompt.Text ?? "";
            DialogResult = true;
            Close();
        };
        var btnAnnuler = new Button
        {
            Content = "Annuler",
            Width = 80,
            Height = 28,
            IsCancel = true,
        };
        btnAnnuler.Click += (_, _) => { DialogResult = false; Close(); };

        boutons.Children.Add(btnAnnuler);
        boutons.Children.Add(btnOk);
        racine.Children.Add(boutons);

        // Zone qui prend le reste (vide, pour pousser les controles en haut).
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
