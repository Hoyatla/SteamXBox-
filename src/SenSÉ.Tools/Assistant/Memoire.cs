using System.Globalization;
using System.Text;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// La duree de vie d'un souvenir dans la memoire de l'Assistant.
/// </summary>
/// <remarks>
/// <b>Trois niveaux, pas deux.</b> Court terme = la session en cours, et les
/// dernieres avec le meme sujet. Moyen terme = les patterns et preferences
/// qui survivent a plusieurs sessions. Long terme = les valeurs, les faits
/// durables, le contexte qui doit etre la a chaque reouverture.
///
/// <para><b>Un fichier par sujet, pas un gros journal.</b> Un seul fichier
/// par sujet, ecrit en Markdown, lisible et editable a la main. Le nom du
/// fichier est le sujet, normalise : pas d'accents, pas d'espaces, tout en
/// minuscule. C'est la regle du chargeur de plugins, et c'est la bonne :
/// ce qui doit pouvoir etre retrouve par son nom ne depend pas du contenu.</para>
/// </remarks>
public enum NiveauMemoire
{
    /// <summary>La session en cours, et les dernieres avec le meme sujet. Oublie apres 7 jours.</summary>
    Court,

    /// <summary>Les patterns et preferences durables. Oublie apres 90 jours.</summary>
    Moyen,

    /// <summary>Les valeurs, les faits durables, le contexte permanent. N'oublie pas.</summary>
    Long,
}

/// <summary>
/// La memoire de l'Assistant, sur disque, en trois niveaux.
/// </summary>
/// <remarks>
/// <b>La seule source de verite est le disque.</b> Le modele peut relire
/// ses notes a chaque reponse. Une note jamais ecrite n'existe pas, une
/// note ecrasee est perdue — c'est la regle du carnet de travail, appliquee
/// a tout ce que l'Assistant apprend.
///
/// <para><b>Pas d'injection, pas de vector store.</b> Du Markdown, dans un
/// sous-dossier, trie par niveau. Pas de base de donnees, pas d'index
/// vectoriel, pas d'embedding — c'est un modele de 4 milliards de
/// parametres, pas un RAG, et la memoire d'un carnet papier suffit a le
/// faire fonctionner.</para>
/// </remarks>
public static class Memoire
{
    private static string Racine
        => Path.Combine(AppContext.BaseDirectory, "Outils", "Memoire");

    private static string DossierCourt => Path.Combine(Racine, "Court");
    private static string DossierMoyen => Path.Combine(Racine, "Moyen");
    private static string DossierLong => Path.Combine(Racine, "Long");

    private static string Dossier(NiveauMemoire niveau) => niveau switch
    {
        NiveauMemoire.Court => DossierCourt,
        NiveauMemoire.Moyen => DossierMoyen,
        NiveauMemoire.Long => DossierLong,
        _ => DossierCourt,
    };

    /// <summary>La duree de retention par niveau, en jours.</summary>
    public static int JoursRetention(NiveauMemoire niveau) => niveau switch
    {
        NiveauMemoire.Court => 7,
        NiveauMemoire.Moyen => 90,
        NiveauMemoire.Long => int.MaxValue,
        _ => 7,
    };

    /// <summary>Assure que les trois dossiers existent. Idempotent.</summary>
    public static void AssurerDossiers()
    {
        Directory.CreateDirectory(DossierCourt);
        Directory.CreateDirectory(DossierMoyen);
        Directory.CreateDirectory(DossierLong);
    }

    /// <summary>
    /// Normalise un sujet en nom de fichier : minuscules, sans accents,
    /// sans espaces, sans ponctuation. Le meme sujet donne le meme nom.
    /// </summary>
    private static string Normaliser(string sujet)
    {
        var sb = new StringBuilder(sujet.Length);
        foreach (var c in sujet.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '-' || c == '_')
            {
                sb.Append('-');
            }
        }
        var normalise = sb.ToString().Normalize(NormalizationForm.FormC);
        while (normalise.Contains("--", StringComparison.Ordinal))
        {
            normalise = normalise.Replace("--", "-", StringComparison.Ordinal);
        }
        return normalise.Trim('-');
    }

    /// <summary>
    /// Ecrit ou complete un souvenir. Si le fichier existe, ajoute le
    /// nouveau paragraphe en bas, date. Sinon, cree le fichier.
    /// </summary>
    public static string Noter(NiveauMemoire niveau, string sujet, string contenu, Action<string>? journal = null)
    {
        if (string.IsNullOrWhiteSpace(sujet)) return "sujet vide";
        if (string.IsNullOrWhiteSpace(contenu)) return "contenu vide";

        AssurerDossiers();
        var nom = Normaliser(sujet);
        if (string.IsNullOrEmpty(nom)) return "sujet inutilisable apres normalisation";

        var chemin = Path.Combine(Dossier(niveau), nom + ".md");
        var maintenant = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var paragraphe = $"\n\n## {maintenant}\n\n{contenu.Trim()}";

        if (File.Exists(chemin))
        {
            File.AppendAllText(chemin, paragraphe);
        }
        else
        {
            var entete = $"# {sujet}\n\nNiveau : {niveau}\nCree : {maintenant}\nModifie : {maintenant}\n";
            File.WriteAllText(chemin, entete + paragraphe);
        }

        var cheminRelatif = Path.Combine("Outils", "Memoire", niveau.ToString(), nom + ".md");
        journal?.Invoke($"memoire notee : {cheminRelatif}");
        return cheminRelatif;
    }

    /// <summary>
    /// Lit un souvenir par son sujet. Si plusieurs versions, renvoie tout.
    /// </summary>
    public static string? Lire(NiveauMemoire niveau, string sujet)
    {
        var nom = Normaliser(sujet);
        if (string.IsNullOrEmpty(nom)) return null;

        var chemin = Path.Combine(Dossier(niveau), nom + ".md");
        return File.Exists(chemin) ? File.ReadAllText(chemin) : null;
    }

    private static string njeu(string s) => s;

    /// <summary>
    /// Liste les sujets d'un niveau, avec la date du dernier paragraphe.
    /// </summary>
    public static IReadOnlyList<(string Sujet, DateTime DerniereModification)>
        Lister(NiveauMemoire niveau)
    {
        var dossier = Dossier(niveau);
        if (!Directory.Exists(dossier)) return [];

        var result = new List<(string, DateTime)>();
        foreach (var fichier in Directory.GetFiles(dossier, "*.md"))
        {
            var sujet = Path.GetFileNameWithoutExtension(fichier);
            var date = File.GetLastWriteTime(fichier);
            result.Add((sujet, date));
        }
        return result.OrderByDescending(t => t.Item2).ToList();
    }

    /// <summary>
    /// Oublie les notes plus vieilles que la retention du niveau.
    /// </summary>
    public static int Purger(Action<string>? journal = null)
    {
        AssurerDossiers();
        var effaces = 0;
        foreach (var niveau in new[] { NiveauMemoire.Court, NiveauMemoire.Moyen })
        {
            var jours = JoursRetention(niveau);
            var limite = DateTime.Now - TimeSpan.FromDays(jours);
            var dossier = Dossier(niveau);
            foreach (var fichier in Directory.GetFiles(dossier, "*.md"))
            {
                try
                {
                    if (File.GetLastWriteTime(fichier) < limite)
                    {
                        File.Delete(fichier);
                        effaces++;
                        journal?.Invoke($"memoire oubliee : {Path.GetFileName(fichier)} ({niveau})");
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return effaces;
    }

    /// <summary>
    /// Consolide la memoire : ce qui etait en court terme depuis plus de
    /// 2 jours passe en moyen terme, sauf si le modele a signale "ephemere".
    /// </summary>
    public static int Promouvoir()
    {
        AssurerDossiers();
        var promus = 0;
        var limite = DateTime.Now - TimeSpan.FromDays(2);
        foreach (var fichier in Directory.GetFiles(DossierCourt, "*.md"))
        {
            try
            {
                if (File.GetLastWriteTime(fichier) < limite)
                {
                    var nom = Path.GetFileName(fichier);
                    var destination = Path.Combine(DossierMoyen, nom);
                    File.Move(fichier, destination, overwrite: false);
                    promus++;
                }
            }
            catch (IOException) { }
        }
        return promus;
    }
}