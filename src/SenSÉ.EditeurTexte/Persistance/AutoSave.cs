using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SenSÉ.EditeurTexte.Persistance;

/// <summary>
/// Auto-save periodique du FlowDocument en cours d'edition. Le timer est un
/// <see cref="DispatcherTimer"/> donc il ticke sur le thread UI - securite pour
/// la serialisation (pas de race avec l'editeur). Le contenu est serialise en
/// Markdown (texte + formatage Run) dans <c>%LOCALAPPDATA%\SenSÉ\Éditeur\autosave\&lt;hash&gt;.md</c>.
/// </summary>
public sealed class AutoSave
{
    private static readonly TimeSpan IntervalleDefaut = TimeSpan.FromSeconds(30);
    private static readonly string Racine = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ", "Éditeur", "autosave");

    private DispatcherTimer? _timer;
    public TimeSpan Intervalle { get; set; } = IntervalleDefaut;

    /// <summary>Chemin du fichier en cours d'edition. Si null, l'autosave ne fait rien.</summary>
    public string? CheminCourant { get; set; }

    /// <summary>Reference au FlowDocument en cours. Le timer voit toujours la derniere version</summary>
    /// <summary>parce que c'est la meme instance que celle posee dans RichTextBox.Document.</summary>
    public FlowDocument? Document { get; set; }

    public DateTime DerniereSauvegarde { get; private set; }

    public void Demarrer()
    {
        if (_timer is not null) return;
        Directory.CreateDirectory(Racine);
        _timer = new DispatcherTimer { Interval = Intervalle };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Arreter()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer = null;
    }

    private void Tick()
    {
        if (CheminCourant is null || Document is null) return;
        var hash = HashPath(CheminCourant);
        var autosavePath = Path.Combine(Racine, hash + ".md");
        try
        {
            var contenu = Format.Markdown.VersMarkdown(Document);
            File.WriteAllText(autosavePath, contenu);
            DerniereSauvegarde = DateTime.UtcNow;
        }
        catch
        {
            // L'autosave ne doit JAMAIS crasher l'editeur. On avale.
        }
    }

    /// <summary>Verifie si un autosave existe pour ce fichier. Le caller doit ensuite comparer
    /// la date de l'autosave avec celle du fichier sur disque avant de proposer la restauration.</summary>
    public static bool ExisteRestauration(string cheminFichier, out DateTime modifieLe, out string autosavePath)
    {
        modifieLe = DateTime.MinValue;
        autosavePath = string.Empty;
        var hash = HashPath(cheminFichier);
        var p = Path.Combine(Racine, hash + ".md");
        if (!File.Exists(p)) return false;
        autosavePath = p;
        modifieLe = File.GetLastWriteTime(p);
        return true;
    }

    public static string Lire(string autosavePath) => File.ReadAllText(autosavePath);

    /// <summary>Supprime l'autosave (apres une sauvegarde manuelle reussie).</summary>
    public static void Supprimer(string cheminFichier)
    {
        var hash = HashPath(cheminFichier);
        var p = Path.Combine(Racine, hash + ".md");
        if (File.Exists(p))
        {
            try { File.Delete(p); } catch { /* best effort */ }
        }
    }

    /// <summary>Hash SHA-256 du chemin absolu en lowercase (meme fichier = meme hash).</summary>
    public static string HashPath(string chemin)
    {
        var normalized = Path.GetFullPath(chemin).ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
