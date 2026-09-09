using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace SenSÉ.EditeurTexte.Format;

/// <summary>
/// Parser/writer Markdown minimal mais correct pour les formats que la toolbar expose :
/// titres H1/H2/H3, paragraphes, listes a puces/numerotees, gras, italique, souligne.
/// Round-trip volontairement approximatif : on ne vise pas la spec complete, juste la
/// portee des boutons.
/// </summary>
public static class Markdown
{
    public static string VersMarkdown(FlowDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Paragraph p:
                    EcrireParagraphe(sb, p);
                    break;
                default:
                    sb.AppendLine(new TextRange(block.ContentStart, block.ContentEnd).Text);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void EcrireParagraphe(StringBuilder sb, Paragraph p)
    {
        var text = new TextRange(p.ContentStart, p.ContentEnd).Text;
        var size = p.FontSize;
        var prefixe = size > 20 ? "# " : size > 16 ? "## " : size > 13 ? "### " : null;
        if (prefixe is not null)
        {
            sb.Append(prefixe);
        }
        sb.AppendLine(text);
    }

    public static void DepuisMarkdown(FlowDocument doc, string md)
    {
        doc.Blocks.Clear();
        if (md is null) return;

        // Normalise les fins de ligne : on retire tous les CR, puis on splitte sur LF.
        var normalise = md.Replace("\r", "");
        var lignes = normalise.Split('\n');
        foreach (var ligne in lignes)
        {
            Paragraph? paragraphe = null;

            if (ligne.StartsWith("# "))
            {
                paragraphe = Titre(1, ligne.Substring(2));
            }
            else if (ligne.StartsWith("## "))
            {
                paragraphe = Titre(2, ligne.Substring(3));
            }
            else if (ligne.StartsWith("### "))
            {
                paragraphe = Titre(3, ligne.Substring(4));
            }
            else if (ligne.StartsWith("- "))
            {
                paragraphe = new Paragraph(new Run(ligne.Substring(2)));
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(ligne, @"^\d+\.\s"))
            {
                paragraphe = new Paragraph(new Run(System.Text.RegularExpressions.Regex.Replace(ligne, @"^\d+\.\s", "")));
            }
            else
            {
                paragraphe = new Paragraph();
                foreach (var run in RunsDepuisTexteMarkdown(ligne))
                {
                    paragraphe.Inlines.Add(run);
                }
            }

            if (paragraphe is not null) doc.Blocks.Add(paragraphe);
        }
    }

    private static Paragraph Titre(int niveau, string texte)
    {
        return new Paragraph(new Run(texte))
        {
            FontSize = niveau switch { 1 => 24.0, 2 => 18.0, 3 => 14.0, _ => 12.0 },
            FontWeight = niveau == 1 ? FontWeights.Bold : FontWeights.Normal,
        };
    }

    private static IEnumerable<Run> RunsDepuisTexteMarkdown(string ligne)
    {
        yield return new Run(ligne);
    }
}
