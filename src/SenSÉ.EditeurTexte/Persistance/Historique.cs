using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SenSÉ.EditeurTexte.Persistance;

/// <summary>Une entree d'historique affichee dans FenetreHistorique.</summary>
public sealed class VersionHistorique
{
    public string Chemin { get; set; } = "";
    public string Nom { get; set; } = "";
    public DateTime Date { get; set; }
    public long Taille { get; set; }
}

/// <summary>
/// Archive les N dernieres versions d'un fichier edite. A chaque sauvegarde
/// manuelle reussie, le fichier AVANT ecriture est copie dans
/// <c>%LOCALAPPDATA%\SenSÉ\Éditeur\history\&lt;hash&gt;\v&lt;timestamp&gt;.&lt;ext&gt;</c>.
/// On garde au plus <see cref="MaxVersions"/> versions par fichier.
/// </summary>
public static class Historique
{
    public const int MaxVersions = 10;
    private static readonly string Racine = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ", "Éditeur", "history");

    /// <summary>Copie le fichier donne vers le dossier d'archive avec un timestamp.
    /// A appeler AVANT l'ecriture du nouveau contenu, sinon on archive le nouveau fichier.
    /// Si le fichier n'existe pas (premiere sauvegarde d'un fichier jamais ecrit), on ne fait rien.</summary>
    public static void Archiver(string cheminFichier)
    {
        if (string.IsNullOrEmpty(cheminFichier) || !File.Exists(cheminFichier)) return;
        var hash = HashPath(cheminFichier);
        var dossier = Path.Combine(Racine, hash);
        Directory.CreateDirectory(dossier);

        var ext = Path.GetExtension(cheminFichier);
        if (string.IsNullOrEmpty(ext)) ext = ".md";
        // Timestamp ISO court, safe pour nom de fichier Windows.
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var archivePath = Path.Combine(dossier, $"v{ts}{ext}");
        try
        {
            File.Copy(cheminFichier, archivePath, overwrite: false);
        }
        catch
        {
            // Si le meme timestamp existe deja (clics rapides), on ignore.
            return;
        }

        // Purge les versions au-dela de MaxVersions, en gardant les plus recentes.
        try
        {
            var fichiers = Directory.GetFiles(dossier)
                .OrderByDescending(File.GetLastWriteTime)
                .Skip(MaxVersions)
                .ToList();
            foreach (var f in fichiers)
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Liste les versions archivees pour un fichier, les plus recentes en premier.</summary>
    public static IReadOnlyList<VersionHistorique> Lister(string cheminFichier)
    {
        if (string.IsNullOrEmpty(cheminFichier)) return Array.Empty<VersionHistorique>();
        var hash = HashPath(cheminFichier);
        var dossier = Path.Combine(Racine, hash);
        if (!Directory.Exists(dossier)) return Array.Empty<VersionHistorique>();
        return Directory.GetFiles(dossier)
            .Select(f => new VersionHistorique
            {
                Chemin = f,
                Nom = Path.GetFileName(f),
                Date = File.GetLastWriteTime(f),
                Taille = new FileInfo(f).Length,
            })
            .OrderByDescending(v => v.Date)
            .ToList();
    }

    /// <summary>Hash SHA-256 du chemin absolu en lowercase (meme fichier = meme hash).
    /// Meme methode que AutoSave.HashPath - duplication intentionnelle pour eviter une
    /// dependance cyclique entre les deux classes.</summary>
    public static string HashPath(string chemin)
    {
        var normalized = Path.GetFullPath(chemin).ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
