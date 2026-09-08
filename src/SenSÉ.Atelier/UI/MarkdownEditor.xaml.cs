using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace SenSÉ.Atelier.UI;

/// <summary>
/// Petit editeur markdown in-place : toggle Edit/Preview, coloration
/// inline (bold/italic/code), insertion du contenu d'un fichier au curseur.
/// </summary>
/// <remarks>
/// <b>Volontairement limite.</b> Le rendu preview gere : titres (#/##/###),
/// listes a puces (-/*), citations (>), paragraphes, et inline **bold**,
/// *italic*, `code`. Pas de tableaux, pas d'images, pas de liens cliquables.
/// C'est un editeur de description, pas un composeur markdown complet.
/// </remarks>
public partial class MarkdownEditor : UserControl
{
    /// <summary>Declenche a chaque modification du texte (mode Edit).</summary>
    public event EventHandler? TextChanged;
    /// <summary>Le nom du champ (tag du noeud inspecte), pour que
    /// l'Inspecteur puisse retrouver la cle du parametre.</summary>
    public new object? Tag { get; set; }

    public string Text
    {
        get => ZoneEdit.Text;
        set
        {
            if (ZoneEdit.Text != value)
            {
                ZoneEdit.Text = value ?? "";
                MettreAJourStatus();
            }
        }
    }

    public MarkdownEditor()
    {
        InitializeComponent();
        MettreAJourStatus();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        ZoneEdit.Visibility = Visibility.Visible;
        ZonePreview.Visibility = Visibility.Collapsed;
        BtnEdit.Style = (Style)FindResource("BtnMdToolActif");
        BtnPreview.Style = (Style)FindResource("BtnMdTool");
        ZoneEdit.Focus();
    }

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        ZoneEdit.Visibility = Visibility.Collapsed;
        ZonePreview.Visibility = Visibility.Visible;
        BtnEdit.Style = (Style)FindResource("BtnMdTool");
        BtnPreview.Style = (Style)FindResource("BtnMdToolActif");
        RendreApercu();
    }

    private void BtnInserer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Inserer le contenu d'un fichier" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var contenu = File.ReadAllText(dlg.FileName);
            var caret = ZoneEdit.CaretIndex;
            ZoneEdit.Text = ZoneEdit.Text.Insert(caret, contenu);
            ZoneEdit.CaretIndex = caret + contenu.Length;
            ZoneEdit.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show( "Impossible de lire le fichier : " + ex.Message, "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ZoneEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        MettreAJourStatus();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MettreAJourStatus()
    {
        var t = ZoneEdit.Text ?? "";
        var lignes = t.Length == 0 ? 0 : t.Split('\n').Length;
        Status.Text = $"{t.Length} car. / {lignes} lign.";
    }

    private void RendreApercu()
    {
        ApercuStack.Children.Clear();
        var lignes = (ZoneEdit.Text ?? "").Replace("\r\n", "\n").Split('\n');
        foreach (var ligne in lignes) ApercuStack.Children.Add(RendreLigne(ligne));
    }

    private Brush? CouleurCode()
    {
        return new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3A));
    }

    private FrameworkElement RendreLigne(string ligne)
    {
        if (string.IsNullOrWhiteSpace(ligne))
            return new TextBlock { Text = " ", Margin = new Thickness(0, 4, 0, 0) };
        if (ligne.StartsWith("# "))
            return new TextBlock { Text = ligne.Substring(2), FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap };
        if (ligne.StartsWith("## "))
            return new TextBlock { Text = ligne.Substring(3), FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap };
        if (ligne.StartsWith("### "))
            return new TextBlock { Text = ligne.Substring(4), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2), TextWrapping = TextWrapping.Wrap };
        if (ligne.StartsWith("- ") || ligne.StartsWith("* "))
        {
            var tb = new TextBlock { Margin = new Thickness(14, 1, 0, 1), TextWrapping = TextWrapping.Wrap };
            tb.Inlines.Add(new Run("\u2022 ") { FontWeight = FontWeights.Bold });
            AppliquerRunsMarkdown(tb.Inlines, ligne.Substring(2));
            return tb;
        }
        if (ligne.StartsWith("> "))
        {
            var inner = new TextBlock { FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap };
            AppliquerRunsMarkdown(inner.Inlines, ligne.Substring(2));
            return new Border
            {
                BorderBrush = (Brush)TryFindResource("AccentBrush") ?? Brushes.Gray,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 2, 0, 2),
                Margin = new Thickness(0, 2, 0, 2),
                Child = inner,
            };
        }
        var para = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        AppliquerRunsMarkdown(para.Inlines, ligne);
        return para;
    }

    /// <summary>Parse inline : **bold**, *italic*, `code`. Le reste est du texte plat.</summary>
    private void AppliquerRunsMarkdown(InlineCollection inlines, string texte)
    {
        var restant = texte;
        while (restant.Length > 0)
        {
            int idxBold = restant.IndexOf("**", StringComparison.Ordinal);
            int idxCode = restant.IndexOf('`');
            int idxStar = -1;
            for (int i = 0; i < restant.Length; i++)
            {
                if (restant[i] == '*' && !(i + 1 < restant.Length && restant[i + 1] == '*') && !(i > 0 && restant[i - 1] == '*'))
                { idxStar = i; break; }
            }
            int premier = -1;
            string? marqueur = null;
            if (idxBold >= 0 && (premier < 0 || idxBold < premier)) { premier = idxBold; marqueur = "**"; }
            if (idxCode >= 0 && (premier < 0 || idxCode < premier)) { premier = idxCode; marqueur = "`"; }
            if (idxStar >= 0 && (premier < 0 || idxStar < premier)) { premier = idxStar; marqueur = "*"; }
            if (premier < 0)
            {
                inlines.Add(new Run(restant));
                return;
            }
            if (premier > 0) inlines.Add(new Run(restant.Substring(0, premier)));
            int fin = restant.IndexOf(marqueur!, premier + marqueur!.Length, StringComparison.Ordinal);
            if (fin < 0)
            {
                inlines.Add(new Run(restant.Substring(premier)));
                return;
            }
            var inner = restant.Substring(premier + marqueur.Length, fin - premier - marqueur.Length);
            if (marqueur == "**")
                inlines.Add(new Run(inner) { FontWeight = FontWeights.Bold });
            else if (marqueur == "*")
                inlines.Add(new Run(inner) { FontStyle = FontStyles.Italic });
            else if (marqueur == "`")
                inlines.Add(new Run(inner) { FontFamily = new FontFamily("Consolas, Courier New"), Background = CouleurCode() });
            restant = restant.Substring(fin + marqueur.Length);
        }
    }

    /// <summary>Expose la TextBox interne pour que la SearchBar puisse y
    /// chercher / remplacer (meme interface qu'un TextBox standard).</summary>
    public TextBox? TrouverTextBoxInterne() => ZoneEdit;

}
