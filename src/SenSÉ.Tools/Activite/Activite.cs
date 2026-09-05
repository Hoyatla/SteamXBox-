using System.Diagnostics;
using System.Globalization;

namespace SenSÉ.Tools.Activite;

/// <summary>Ce à quoi un événement appartient.</summary>
public enum Famille
{
    /// <summary>Le produit lui-même : son environnement, son cœur, ses claviers.</summary>
    Natif,

    /// <summary>Un programme qu'un outil a lancé : moteur, serveur, interpréteur.</summary>
    Outil,

    /// <summary>Un programme extérieur déclaré comme dépendance.</summary>
    Dependance,
}

/// <summary>Un programme en cours qui appartient à SenSÉ.</summary>
/// <param name="Nom">Ce qu'on montre.</param>
/// <param name="Famille">À quoi il appartient.</param>
/// <param name="Pid">Son identifiant système.</param>
/// <param name="Chemin">L'exécutable, tel qu'il est sur le disque.</param>
/// <param name="Memoire">Mémoire de travail, en mégaoctets.</param>
/// <param name="Depuis">Depuis combien de temps il tourne.</param>
public sealed record Evenement(
    string Nom,
    Famille Famille,
    int Pid,
    string Chemin,
    long Memoire,
    TimeSpan Depuis)
{
    /// <summary>
    /// La ligne de détail montrée sous le nom.
    /// </summary>
    /// <remarks>
    /// Le chemin y figure, et ce n'est pas du remplissage : un « python » qui tourne ne dit rien,
    /// <c>Outils\Python\python.exe</c> dit lequel et pour le compte de quoi. C'est la seule
    /// information qui n'exige aucune table à entretenir.
    /// </remarks>
    public string Detail
        => string.Join("  ·  ", new[]
        {
            Relatif(),
            Duree(),
            Memoire.ToString(CultureInfo.CurrentCulture) + " Mo",
            "pid " + Pid.ToString(CultureInfo.InvariantCulture),
        });

    private string Relatif()
    {
        var racine = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        return Chemin.StartsWith(racine, StringComparison.OrdinalIgnoreCase)
            ? Chemin[(racine.Length + 1)..]
            : Chemin;
    }

    private string Duree()
        => Depuis.TotalHours >= 1
            ? $"depuis {Depuis.TotalHours:F0} h {Depuis.Minutes} min"
            : Depuis.TotalMinutes >= 1
                ? $"depuis {Depuis.TotalMinutes:F0} min"
                : $"depuis {Depuis.TotalSeconds:F0} s";
}

/// <summary>
/// Ce qui tourne au nom de SenSÉ, à un instant donné.
/// </summary>
/// <remarks>
/// <b>Pourquoi ce recensement existe.</b> Le produit lance des programmes qui lui survivent : un
/// serveur de génération garde vingt gigaoctets de mémoire vidéo, un modèle de langage cinq, et
/// tous deux héritent des droits élevés de SenSÉ — donc ni le gestionnaire de tâches ordinaire
/// ni une console normale ne peuvent les arrêter. Sans cette fenêtre, la seule issue était de
/// fermer le produit, ou d'ouvrir un gestionnaire en administrateur pour deviner lequel de ces
/// « python.exe » lui appartenait.
///
/// <para>
/// <b>Rien n'est deviné par le nom.</b> Un processus n'entre dans la liste que si son exécutable
/// se trouve dans le dossier du produit, ou qu'il correspond au chemin qu'une dépendance déclare.
/// Un <c>python.exe</c> quelconque de la machine n'a rien à faire ici, et un bouton « arrêter »
/// qui viserait le mauvais serait pire que pas de bouton du tout.
/// </para>
/// </remarks>
public static class Recensement
{
    private static string Racine => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    private static string Outils => Path.Combine(Racine, "Outils");

    /// <summary>
    /// Ce que tout cela tourne, classé.
    /// </summary>
    /// <param name="dependances">
    /// Les chemins déclarés par les manifestes de dépendance, déjà résolus par l'hôte.
    /// </param>
    public static IReadOnlyList<Evenement> Lister(IReadOnlyList<string> dependances)
        => Lister(dependances, out _);

    /// <summary>
    /// La meme chose, en disant combien de processus ont echappe a l examen.
    /// </summary>
    /// <remarks>
    /// Un processus plus privilegie que nous refuse de dire son chemin. Le taire donnerait une
    /// liste incomplete presentee comme complete — et c est precisement le cas ou l utilisateur
    /// cherche un programme qui ne veut pas mourir.
    /// </remarks>
    public static IReadOnlyList<Evenement> Lister(IReadOnlyList<string> dependances, out int inaccessibles)
    {
        inaccessibles = 0;
        var attendus = Noms(dependances);
        var evenements = new List<Evenement>();

        foreach (var processus in Process.GetProcesses())
        {
            try
            {
                if (!attendus.Contains(processus.ProcessName))
                {
                    continue;
                }

                if (processus.MainModule?.FileName is not { Length: > 0 } chemin)
                {
                    continue;
                }

                if (Classer(chemin, dependances) is not { } famille)
                {
                    continue;
                }

                evenements.Add(new Evenement(
                    Nommer(chemin),
                    famille,
                    processus.Id,
                    chemin,
                    processus.WorkingSet64 / (1024 * 1024),
                    DateTime.Now - processus.StartTime));
            }
            catch (InvalidOperationException)
            {
                // Terminé entre l'énumération et l'examen : il n'est plus un événement.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Refuse de se laisser inspecter : plus privilegie que nous. Compte a part, pour
                // que la fenetre puisse le dire au lieu de faire comme s il n existait pas.
                inaccessibles++;
            }
            finally
            {
                processus.Dispose();
            }
        }

        return evenements
            .OrderBy(e => e.Famille)
            .ThenBy(e => e.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Combien tournent, pour l'afficher sous une icône.</summary>
    public static int Combien(IReadOnlyList<string> dependances) => Lister(dependances).Count;

    /// <summary>
    /// Arrête un événement, et sa descendance.
    /// </summary>
    /// <remarks>
    /// Avec la descendance, sans quoi arrêter le shell d'un serveur laisserait l'interpréteur
    /// vivant — et l'utilisateur verrait la ligne disparaître pendant que la mémoire vidéo reste
    /// prise.
    /// </remarks>
    public static string Arreter(int pid, IReadOnlyList<string> dependances)
    {
        // Revérifié plutôt que cru sur parole : entre l'affichage et le clic, le système a pu
        // réattribuer cet identifiant à un processus qui ne nous appartient pas.
        if (!Lister(dependances).Any(e => e.Pid == pid))
        {
            return "Cet événement n'est plus là.";
        }

        try
        {
            using var processus = Process.GetProcessById(pid);
            var nom = Nommer(processus.MainModule?.FileName ?? "");

            processus.Kill(entireProcessTree: true);

            return $"{nom} arrêté.";
        }
        catch (ArgumentException)
        {
            return "Cet événement n'est plus là.";
        }
        catch (InvalidOperationException)
        {
            return "Cet événement n'est plus là.";
        }
        catch (System.ComponentModel.Win32Exception erreur)
        {
            return $"Arrêt refusé : {erreur.Message}";
        }
    }

    /// <summary>Les noms de processus qui méritent un examen.</summary>
    /// <remarks>
    /// Filtrer d'abord par le nom évite de demander son chemin à trois cents processus, dont la
    /// plupart refuseraient. La liste vient du disque — les exécutables du produit et ceux que les
    /// dépendances déclarent — jamais d'une liste écrite à la main qui vieillirait.
    /// </remarks>
    private static HashSet<string> Noms(IReadOnlyList<string> dependances)
    {
        var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chemin in dependances)
        {
            noms.Add(Path.GetFileNameWithoutExtension(chemin));
        }

        Ajouter(noms, Racine);

        if (Directory.Exists(Outils))
        {
            foreach (var dossier in Directory.GetDirectories(Outils))
            {
                Ajouter(noms, dossier);
                Ajouter(noms, Path.Combine(dossier, "bin"));
            }
        }

        return noms;
    }

    private static void Ajouter(HashSet<string> noms, string dossier)
    {
        if (!Directory.Exists(dossier))
        {
            return;
        }

        foreach (var fichier in Directory.GetFiles(dossier, "*.exe"))
        {
            noms.Add(Path.GetFileNameWithoutExtension(fichier));
        }
    }

    private static Famille? Classer(string chemin, IReadOnlyList<string> dependances)
    {
        // Les dépendances d'abord : ffmpeg posé dans Outils reste une dépendance, pas un outil.
        foreach (var declare in dependances)
        {
            if (string.Equals(chemin, declare, StringComparison.OrdinalIgnoreCase))
            {
                return Famille.Dependance;
            }
        }

        if (chemin.StartsWith(Outils + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Famille.Outil;
        }

        if (chemin.StartsWith(Racine + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Famille.Natif;
        }

        return null;
    }

    /// <summary>Un nom lisible, tiré du chemin plutôt que d'une table à entretenir.</summary>
    private static string Nommer(string chemin)
    {
        if (chemin.Length == 0)
        {
            return "programme";
        }

        var fichier = Path.GetFileNameWithoutExtension(chemin);
        var parent = Path.GetDirectoryName(chemin) ?? "";

        // A la racine du produit, le dossier ne dit rien de plus que le nom du produit lui-meme.
        if (string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), Racine, StringComparison.OrdinalIgnoreCase))
        {
            return fichier;
        }

        var dossier = Path.GetFileName(parent);

        // « python » seul ne dit rien ; « ComfyUI · python » dit lequel et pour quoi.
        if (dossier.Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            dossier = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(chemin)) ?? "");
        }

        return dossier.Length > 0 && !dossier.Equals(fichier, StringComparison.OrdinalIgnoreCase)
            ? $"{dossier} · {fichier}"
            : fichier;
    }
}
