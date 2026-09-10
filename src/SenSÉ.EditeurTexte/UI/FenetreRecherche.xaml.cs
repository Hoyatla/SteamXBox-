using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SenSÉ.EditeurTexte.UI;

/// <summary>
/// Fenetre de recherche/remplacement. Phase G.1a : coquille UI. Phase G.4 :
/// logique complete avec navigation circulaire (Suivant / Precedent) et
/// remplacement unitaire ou total. La fenetre reste ouverte entre deux
/// recherches pour preserver l'historique des champs.
/// </summary>
public partial class FenetreRecherche : Window
{
    private readonly RichTextBox _rtb;
    private bool _modeRemplacement;

    public bool ModeRemplacement
    {
        get => _modeRemplacement;
        set
        {
            _modeRemplacement = value;
            LabelRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ChampRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            BtnRemplacerUn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            BtnToutRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public FenetreRecherche(RichTextBox rtb)
    {
        InitializeComponent();
        _rtb = rtb;
        ModeRemplacement = false;

        BtnSuivant.Click       += (_, _) => Rechercher(avant: false);
        BtnPrecedent.Click     += (_, _) => Rechercher(avant: true);
        BtnRemplacerUn.Click   += (_, _) => RemplacerUn();
        BtnToutRemplacer.Click += (_, _) => RemplacerTout();
        BtnFermer.Click        += (_, _) => Close();

        // Entree = Suivant (ou Remplacer si on est dans le champ Remplacer).
        ChampRecherche.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Rechercher(avant: false); e.Handled = true; }
        };
        ChampRemplacer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { RemplacerUn(); e.Handled = true; }
        };
    }

    /// <summary>StringComparison selon la case.</summary>
    private StringComparison Comparer() =>
        ChkCasse.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private string TexteComplet() =>
        new TextRange(_rtb.Document.ContentStart, _rtb.Document.ContentEnd).Text;

    /// <summary>Offset (en caracteres) de la position dans le texte complet du document.</summary>
    private int SelectionOffset() =>
        new TextRange(_rtb.Document.ContentStart, _rtb.Selection.Start).Text.Length;

    /// <summary>Avance un TextPointer de n caracteres vers l'avant (utilise GetPositionAtOffset(1)).</summary>
    private TextPointer? PointeurAOffset(int offset)
    {
        var ptr = _rtb.Document.ContentStart;
        for (int i = 0; i < offset; i++)
        {
            var next = ptr.GetPositionAtOffset(1);
            if (next is null) return null;
            ptr = next;
        }
        return ptr;
    }

    private void Rechercher(bool avant)
    {
        var cherche = ChampRecherche.Text ?? "";
        if (string.IsNullOrEmpty(cherche))
        {
            Signaler("Recherche : champ vide.");
            return;
        }
        var cmp = Comparer();
        var fullText = TexteComplet();
        int idx;

        if (avant)
        {
            // Recherche en arriere depuis le debut de la selection actuelle.
            int from = SelectionOffset();
            idx = fullText.LastIndexOf(cherche, Math.Max(0, from - 1), cmp);
            if (idx < 0)
            {
                // Wrap vers la fin.
                idx = fullText.LastIndexOf(cherche, fullText.Length, cmp);
            }
        }
        else
        {
            // Recherche en avant depuis la fin de la selection actuelle.
            int from = SelectionOffset();
            if (!_rtb.Selection.IsEmpty) from += cherche.Length; // sauter le match courant
            idx = fullText.IndexOf(cherche, from, cmp);
            if (idx < 0)
            {
                // Wrap vers le debut.
                idx = fullText.IndexOf(cherche, 0, cmp);
            }
        }

        if (idx < 0)
        {
            Signaler($"Recherche : \"{cherche}\" introuvable.");
            return;
        }

        SelectionnerPlage(idx, cherche.Length);
        Signaler($"Trouvé à la position {idx}.");
    }

    private void SelectionnerPlage(int start, int length)
    {
        var s = PointeurAOffset(start);
        var e = PointeurAOffset(start + length);
        if (s is null || e is null) return;
        _rtb.Selection.Select(s, e);
        _rtb.Focus();
        // Scroll vers la selection.
        var rect = s.GetCharacterRect(LogicalDirection.Forward);
        _rtb.ScrollToVerticalOffset(Math.Max(0, rect.Top - 40));
    }

    private void RemplacerUn()
    {
        var cherche = ChampRecherche.Text ?? "";
        var remplace = ChampRemplacer.Text ?? "";
        if (string.IsNullOrEmpty(cherche))
        {
            Signaler("Remplacer : champ Rechercher vide.");
            return;
        }
        var cmp = Comparer();

        // Si la selection courante ne matche pas, on cherche d'abord.
        if (_rtb.Selection.IsEmpty || !string.Equals(_rtb.Selection.Text, cherche, cmp))
        {
            Rechercher(avant: false);
            return;
        }
        // Remplace puis cherche le suivant.
        _rtb.Selection.Text = remplace;
        Signaler("Remplacé 1 occurrence.");
        Rechercher(avant: false);
    }

    private void RemplacerTout()
    {
        var cherche = ChampRecherche.Text ?? "";
        var remplace = ChampRemplacer.Text ?? "";
        if (string.IsNullOrEmpty(cherche))
        {
            Signaler("Remplacer tout : champ Rechercher vide.");
            return;
        }
        var cmp = Comparer();
        var fullText = TexteComplet();

        // Collecter toutes les positions dans le texte ORIGINAL.
        var positions = new System.Collections.Generic.List<int>();
        int idx = 0;
        while ((idx = fullText.IndexOf(cherche, idx, cmp)) >= 0)
        {
            positions.Add(idx);
            idx += cherche.Length;
        }
        if (positions.Count == 0)
        {
            Signaler($"Remplacer tout : \"{cherche}\" introuvable.");
            return;
        }

        // Remplacer depuis la fin vers le debut pour ne pas casser les offsets.
        for (int i = positions.Count - 1; i >= 0; i--)
        {
            int pos = positions[i];
            var s = PointeurAOffset(pos);
            var e = PointeurAOffset(pos + cherche.Length);
            if (s is null || e is null) continue;
            new TextRange(s, e).Text = remplace;
        }
        Signaler($"Remplacé {positions.Count} occurrence(s).");
    }

    private void Signaler(string message)
    {
        if (Owner is MainWindow main) main.Statut.Text = message;
    }
}
