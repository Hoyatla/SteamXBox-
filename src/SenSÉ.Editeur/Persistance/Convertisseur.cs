using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SenSÉ.Plugins;
using SenSÉ.Tools.Documents;

namespace SenSÉ.Editeur.Persistance;

/// <summary>
/// Wrapper out-of-process-friendly des convertisseurs exposes par SenSÉ.Tools.
/// L'editeur reference directement SenSÉ.Tools (meme solution, pas de binaire
/// externe requis) et appelle LibreOffice.Convert et voisins.
/// </summary>
/// <remarks>
/// Le plugin declaratif <c>Plugins/convertir-document/plugin.json</c> expose la
/// meme API via le verbe <c>convert</c> in-process dans SenSÉ.Desktop. Ici on
/// l'appelle directement parce que l'editeur est un process separe et
/// SenSÉ.Desktop n'expose pas (encore) d'endpoint HTTP pour la conversion.
/// </remarks>
public static class Convertisseur
{
    /// <summary>Liste des formats cibles supportes, alignee sur le manifeste
    /// <c>Plugins/convertir-document/plugin.json</c> (slot format).</summary>
    public static readonly IReadOnlyList<(string Extension, string Id)> FormatsCibles =
        new List<(string, string)>
        {
            (".md",   "txt"),
            (".txt",  "txt"),
            (".docx", "docx"),
            (".docx-image", "docx-image"),
            (".odt",  "odt"),
            (".rtf",  "rtf"),
            (".html", "html"),
            (".pptx", "pptx"),
            (".odp",  "odp"),
            (".xlsx", "xlsx"),
            (".ods",  "ods"),
            (".csv",  "csv"),
            (".pdf",  "pdf"),
            (".ocr",  "ocr"),
        };

    /// <summary>Convertit un fichier vers le format demande. Le resultat
    /// atterrit dans le meme dossier que l'input, avec l'extension cible.
    /// Renvoie le chemin complet du fichier produit, ou leve une exception.</summary>
    public static string Convertir(string cheminEntree, string formatCible,
        Action<string>? journal = null, CancellationToken ct = default)
    {
        if (!File.Exists(cheminEntree))
            throw new FileNotFoundException("entree introuvable", cheminEntree);

        var outputDir = Path.GetDirectoryName(cheminEntree) ?? ".";
        journal?.Invoke("convertir-document : " + Path.GetFileName(cheminEntree) + " -> " + formatCible);

        if (!LibreOffice.IsInstalled)
            throw new InvalidOperationException(
                "LibreOffice n'est pas installe sur cette machine. " +
                "Installe-le depuis https://www.libreoffice.org/ pour activer la conversion " +
                "vers DOCX/ODT/RTF/PDF/HTML/PPTX/ODP/XLSX/ODS/CSV.");

        var result = LibreOffice.Convert(cheminEntree, formatCible, outputDir, journal);
        if (!result.Worked)
            throw new InvalidOperationException(
                "conversion echouee : " + result.Problem);

        if (string.IsNullOrEmpty(result.Produced) || !File.Exists(result.Produced))
            throw new InvalidOperationException(
                "conversion OK mais fichier produit introuvable");

        return result.Produced;
    }
}
