using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
        // FilterIndex est 1-based et le Filter commence par "(tous)|*.*"
        var idx = dlg.FilterIndex - 2; // 0 = md, 1 = txt, 2.. = autres
        string formatCible = idx switch
        {
            <= 0 => Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".txt" ? "txt" : "txt", // md/txt -> direct
            _ => _formatsSauvegarde[idx].Id,
        };
        Sauver(dlg.FileName, formatCible);
    }

    private static readonly (string Extension, string Id)[] _formatsSauvegarde =
        Convertisseur.FormatsCibles.ToArray();

    private static string ConstruireFilterSauvegarde()
    {
        // Index 1 = "tous" (Windows SaveFileDialog ajoute toujours ca en tete).
        // Index 2..N = un format par ligne, dans le meme ordre que _formatsSauvegarde.
        var lignes = new List<string> { "Tous les formats documents (*.*)|*.*" };
        foreach (var (ext, id) in _formatsSauvegarde)
        {
            var label = id switch
            {
                "docx-image" => "Word docx-image",
                "ocr"        => "OCR (PDF -> texte)",
                _ => id.ToUpperInvariant() switch
                {
                    "MD"   => "Markdown",
                    "TXT"  => "Texte brut",
                    "DOCX" => "Word",
                    "ODT"  => "OpenDocument Text",
                    "RTF"  => "Rich Text Format",
                    "HTML" => "Page web",
                    "PPTX" => "PowerPoint",
                    "ODP"  => "OpenDocument Presentation",
                    "XLSX" => "Excel",
                    "ODS"  => "OpenDocument Sheet",
                    "CSV"  => "CSV",
                    "PDF"  => "PDF",
                    _      => id,
                },
            };
            lignes.Add(label + " (*" + ext + ")|*" + ext);
        }
        return string.Join("|", lignes);
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

    private void Sauver(string chemin, string? formatCible = null)
    {
        // Detection automatique du format si pas precise : .md / .txt -> direct,
        // tout autre extension -> conversion via LibreOffice.
        formatCible ??= Path.GetExtension(chemin).ToLowerInvariant() switch
        {
            ".md"  or ".txt" => "txt",
            _                => Path.GetExtension(chemin).TrimStart('.').ToLowerInvariant(),
        };

        try
        {
            if (formatCible == "txt")
            {
                Sauvegardeur.Sauvegarder(Rtb.Document, chemin);
                _cheminActuel = chemin;
                Title = $"Éditeur — SenSÉ — {Path.GetFileName(chemin)}";
                Statut.Text = "Enregistré.";
            }
            else
            {
                // Conversion : on serialize en .md dans %TEMP%, on convertit, on deplace, on nettoie.
                var tempMd = Path.Combine(Path.GetTempPath(), "editeur_" + Guid.NewGuid().ToString("N") + ".md");
                Sauvegardeur.Sauvegarder(Rtb.Document, tempMd);
                try
                {
                    var produit = Convertisseur.Convertir(tempMd, formatCible,
                        msg => Statut.Text = msg);
                    File.Move(produit, chemin, overwrite: true);
                    _cheminActuel = chemin;
                    Title = $"Éditeur — SenSÉ — {Path.GetFileName(chemin)}";
                    Statut.Text = "Converti en " + formatCible.ToUpperInvariant() + " : " + Path.GetFileName(chemin);
                }
                finally
                {
                    try { if (File.Exists(tempMd)) File.Delete(tempMd); } catch { /* best effort */ }
                }
            }
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

    private void Fenetre_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var fichiers = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (fichiers.Length > 0) Charger(fichiers[0]);
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
