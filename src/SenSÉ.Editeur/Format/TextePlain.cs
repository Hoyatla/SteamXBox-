using System.Windows.Documents;

namespace SenSÉ.Editeur.Format;

/// <summary>Lecture/ecriture d'un <see cref="FlowDocument"/> en texte pur, sans formatage.</summary>
public static class TextePlain
{
    public static string VersPlainText(FlowDocument doc)
    {
        return new TextRange(doc.ContentStart, doc.ContentEnd).Text;
    }

    public static void DepuisPlainText(FlowDocument doc, string text)
    {
        doc.Blocks.Clear();
        var paragraph = new Paragraph(new Run(text ?? ""));
        doc.Blocks.Add(paragraph);
    }
}
