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
        var contenu = ext == ".md" ? Format.Markdown.VersMarkdown(doc) : Format.TextePlain.VersPlainText(doc);
        File.WriteAllText(chemin, contenu);
    }
}
