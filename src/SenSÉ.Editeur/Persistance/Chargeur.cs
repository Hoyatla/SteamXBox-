using System.IO;
using System.Windows.Documents;

namespace SenSÉ.Editeur.Persistance;

/// <summary>
/// Charge un fichier dans un <see cref="FlowDocument"/>. Routage par extension :
/// .md -> <see cref="Format.Markdown"/>, defaut -> <see cref="Format.TextePlain"/>.
/// </summary>
public static class Chargeur
{
    public static void Charger(string chemin, FlowDocument cible)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        var contenu = File.ReadAllText(chemin);
        switch (ext)
        {
            case ".md":
                Format.Markdown.DepuisMarkdown(cible, contenu);
                break;
            case ".docx":
                Format.Docx.DepuisDocx(cible, chemin); // .md / .docx ecrits sur disque
                break;
            default:
                Format.TextePlain.DepuisPlainText(cible, contenu);
                break;
        }
    }
}
