using System.IO;
using System.Windows.Documents;

namespace SenSÉ.Editeur.Persistance;

/// <summary>
/// Inverse de <see cref="Chargeur"/>. Meme routage par extension.
/// </summary>
public static class Sauvegardeur
{
    public static void Sauvegarder(FlowDocument doc, string chemin)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        switch (ext)
        {
            case ".md":
                File.WriteAllText(chemin, Format.Markdown.VersMarkdown(doc));
                break;
            case ".docx":
                Format.Docx.VersDocx(doc, chemin);
                break;
            default:
                File.WriteAllText(chemin, Format.TextePlain.VersPlainText(doc));
                break;
        }
    }
}
