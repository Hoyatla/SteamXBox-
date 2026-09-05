using System.Diagnostics;

namespace SenSÉ.Tools.Serveurs;

/// <summary>Ce qu'une ressource pèse sur la carte graphique.</summary>
public enum Poids
{
    /// <summary>Négligeable : plusieurs peuvent coexister sans se gêner.</summary>
    Leger,

    /// <summary>Charge des modèles en mémoire vidéo. Une seule à la fois.</summary>
    Lourd,
}

/// <summary>Ce qui doit lui arriver quand SenSÉ se ferme.</summary>
public enum ALaFermeture
{
    /// <summary>Arrêtée avec le produit : elle n'a de sens que pendant qu'il tourne.</summary>
    Tuer,

    /// <summary>Laissée en vie : un travail long qu'on ne veut pas perdre.</summary>
    Survivre,
}

/// <summary>Quelque chose de lourd que le produit a lancé, et qui lui appartient.</summary>
/// <param name="Id">Clé stable, pour l'arrêter ou la remplacer.</param>
/// <param name="Nom">Ce qu'on montre à l'utilisateur.</param>
/// <param name="Proprietaire">L'outil qui l'a demandée.</param>
/// <param name="Poids">Ce qu'elle pèse sur la carte.</param>
/// <param name="CoutVideoMo">Estimation déclarée, en mégaoctets. Zéro si elle n'en prend pas.</param>
/// <param name="Politique">Son sort à la fermeture du produit.</param>
/// <param name="Processus">Le processus qui la porte.</param>
/// <param name="Depuis">Quand elle a démarré.</param>
public sealed record Ressource(
    string Id,
    string Nom,
    string Proprietaire,
    Poids Poids,
    int CoutVideoMo,
    ALaFermeture Politique,
    Process Processus,
    DateTime Depuis)
{
    /// <summary>Tourne-t-elle encore ?</summary>
    public bool Vivante
    {
        get
        {
            try
            {
                return !Processus.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}

/// <summary>
/// Le registre de ce que SenSÉ a lancé et de ce que ça coûte.
/// </summary>
/// <remarks>
/// <b>Pourquoi un registre plutôt qu'un balayage.</b> Avant lui, rien ne savait ce qui appartenait
/// au produit : il fallait parcourir les processus de la machine et deviner à partir des chemins.
/// Deux serveurs ont ainsi survécu à une fermeture en gardant dix gigaoctets et demi de mémoire
/// vidéo, sans que personne — ni le produit, ni l'utilisateur, ni le gestionnaire de tâches — sache
/// à qui les rattacher.
///
/// <para>
/// <b>Le poids est déclaré, pas mesuré, et ce n'est pas un raccourci.</b> Sur une carte GeForce en
/// WDDM, NVIDIA ne rapporte pas la mémoire vidéo par processus : <c>nvidia-smi</c> rend
/// <c>[N/A]</c>. Vérifié sur la machine de développement. La seule information disponible est le
/// total de la carte ; le reste doit venir de celui qui lance.
/// </para>
///
/// <para>
/// <b>Un seul lourd à la fois.</b> Ce n'est pas une politique d'ordonnancement, c'est une
/// constatation : le générateur annonce près de vingt gigaoctets pour une carte qui en a douze, et
/// le modèle de langage cinq de plus. Ils ne peuvent pas coexister. Faire de la place explicitement
/// vaut mieux que les laisser se disputer la carte et ralentir tous les deux.
/// </para>
/// </remarks>
public static class Ressources
{
    private static readonly object Verrou = new();
    private static readonly List<Ressource> Inscrites = [];

    /// <summary>
    /// Les programmes qui, laissés par une session précédente, occupent réellement la carte.
    /// </summary>
    /// <remarks>
    /// <b>Le modèle de langage n'y est pas, et son absence est le correctif.</b> Il y figurait ; le
    /// 22 août à 23:52:37, il tournait, l'assistant s'en servait, et l'ouverture du générateur l'a
    /// tué comme « serveur d'une session précédente » — cinq secondes après qu'il eut demandé un
    /// chemin d'accès à l'utilisateur, qui n'a jamais eu de réponse et a cru l'assistant planté.
    ///
    /// <para>
    /// Il n'aurait jamais dû y figurer. Ce balayage ne sert qu'à libérer la carte, et le modèle est
    /// chargé en <c>-ngl 0</c>, sur le processeur, sans exception possible — c'est une invariante
    /// de <c>ServeurModele</c>, pas un réglage. Le tuer ne rendait pas un mégaoctet de mémoire
    /// vidéo ; cela ne coûtait que l'assistant.
    /// </para>
    ///
    /// <para>
    /// Chemins <b>exacts</b> sous <c>Outils</c>, jamais des noms : un <c>python.exe</c> quelconque
    /// de la machine ne nous appartient pas, et se tromper de victime serait bien pire que deux
    /// serveurs qui se gênent.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Gourmands
        => [Path.Combine(Outils, "Python", "python.exe")];

    private static string Outils
        => Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Outils");

    /// <summary>Inscrit une ressource qui vient de démarrer.</summary>
    public static void Inscrire(Ressource ressource)
    {
        lock (Verrou)
        {
            Inscrites.RemoveAll(r => r.Id == ressource.Id || !r.Vivante);
            Inscrites.Add(ressource);
        }
    }

    /// <summary>Ce qui tourne, les morts retirés au passage.</summary>
    public static IReadOnlyList<Ressource> Lister()
    {
        lock (Verrou)
        {
            Inscrites.RemoveAll(r => !r.Vivante);

            return [.. Inscrites];
        }
    }

    /// <summary>La mémoire vidéo engagée d'après ce que les ressources déclarent.</summary>
    public static int CoutVideoMo() => Lister().Sum(r => r.CoutVideoMo);

    /// <summary>
    /// Fait de la place avant de lancer une ressource lourde.
    /// </summary>
    /// <returns>Ce qui a été arrêté, ou une chaîne vide si rien n'a bougé.</returns>
    public static string FairePlace(Poids poids, string sauf, Action<string>? journal)
    {
        if (poids != Poids.Lourd)
        {
            return "";
        }

        var arretees = new List<string>();

        foreach (var lourde in Lister().Where(r => r.Poids == Poids.Lourd && r.Id != sauf))
        {
            journal?.Invoke($"« {lourde.Nom} » est déchargé pour laisser la place.");

            if (Arreter(lourde.Id))
            {
                arretees.Add(lourde.Nom);
            }
        }

        var orphelins = Orphelins(journal);

        if (orphelins > 0)
        {
            arretees.Add($"{orphelins} serveur(s) d'une session précédente");
        }

        return arretees.Count == 0
            ? ""
            : $"{string.Join(" et ", arretees)} déchargé pour libérer la carte.";
    }

    /// <summary>
    /// Les gros consommateurs laissés par une session précédente.
    /// </summary>
    /// <remarks>
    /// <b>Le registre vit dans ce processus, pas sur la machine.</b> Un serveur survivant à une
    /// fermeture — ou lancé par une session de développement — lui est parfaitement inconnu :
    /// l'éviction ne le voyait pas, et les deux se disputaient la carte sans que rien ne l'explique.
    /// Ils sont donc aussi cherchés par leur exécutable.
    ///
    /// <para>
    /// Reconnus par le <b>chemin exact</b> de leur programme sous <c>Outils</c>, jamais par leur
    /// nom : un <c>python.exe</c> quelconque de la machine ne nous appartient pas, et une éviction
    /// qui tuerait le mauvais serait bien pire que deux serveurs qui se gênent.
    /// </para>
    ///
    /// <para>
    /// Sans danger pour un serveur encore utilisable : l'appelant a déjà vérifié qu'aucun ne
    /// répondait avant d'en vouloir un nouveau. Ce qui reste ici ne sert plus personne.
    /// </para>
    /// </remarks>
    private static int Orphelins(Action<string>? journal)
    {
        var arretes = 0;

        foreach (var processus in TrouverOrphelins())
        {
            try
            {
                journal?.Invoke(
                    "Un serveur d'une session précédente occupait la carte : "
                    + Path.GetFileName(processus.MainModule?.FileName ?? "") + ".");

                processus.Kill(entireProcessTree: true);
                arretes++;
            }
            catch (InvalidOperationException)
            {
                // Terminé entre l'énumération et l'examen : plus rien à évincer.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Plus privilégié que nous : rien à faire, et le dire serait du bruit — la fenêtre
                // Activité, elle, le montrera.
            }
            finally
            {
                processus.Dispose();
            }
        }

        return arretes;
    }

    /// <summary>
    /// La carte est-elle déjà prise par un gros consommateur ?
    /// </summary>
    /// <remarks>
    /// Registre <b>et</b> orphelins : décider en ne regardant que le registre revenait à charger
    /// l'assistant sur la carte alors qu'un générateur d'une session précédente l'occupait déjà.
    /// </remarks>
    public static bool CarteOccupee()
    {
        if (Lister().Any(r => r.Poids == Poids.Lourd))
        {
            return true;
        }

        var orphelins = TrouverOrphelins();

        foreach (var processus in orphelins)
        {
            processus.Dispose();
        }

        return orphelins.Count > 0;
    }

    /// <summary>Les processus lourds qui nous appartiennent sans être au registre.</summary>
    /// <remarks>
    /// Reconnus par le <b>chemin exact</b> de leur programme sous <c>Outils</c>, jamais par leur
    /// nom : un <c>python.exe</c> quelconque de la machine ne nous appartient pas, et une éviction
    /// qui tuerait le mauvais serait bien pire que deux serveurs qui se gênent.
    /// </remarks>
    private static IReadOnlyList<Process> TrouverOrphelins()
    {
        var lourds = Gourmands;

        var connus = Lister().Select(r => r.Processus.Id).ToHashSet();
        var trouves = new List<Process>();

        foreach (var processus in Process.GetProcesses())
        {
            var garde = false;

            try
            {
                garde = !connus.Contains(processus.Id)
                        && processus.MainModule?.FileName is { Length: > 0 } chemin
                        && lourds.Any(l => string.Equals(l, chemin, StringComparison.OrdinalIgnoreCase));
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            if (garde)
            {
                trouves.Add(processus);
            }
            else
            {
                processus.Dispose();
            }
        }

        return trouves;
    }

    /// <summary>Arrête une ressource et toute sa descendance.</summary>
    /// <remarks>
    /// Avec la descendance : nos serveurs sont lancés par un shell, et tuer le shell seul
    /// laisserait l'interpréteur vivant avec la mémoire vidéo prise.
    /// </remarks>
    public static bool Arreter(string id)
    {
        Ressource? cible;

        lock (Verrou)
        {
            cible = Inscrites.FirstOrDefault(r => r.Id == id);

            if (cible is not null)
            {
                Inscrites.Remove(cible);
            }
        }

        if (cible is null)
        {
            return false;
        }

        try
        {
            if (!cible.Processus.HasExited)
            {
                cible.Processus.Kill(entireProcessTree: true);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Arrête tout ce qui est inscrit.</summary>
    public static int ArreterTout()
    {
        var arretees = 0;

        foreach (var ressource in Lister())
        {
            if (Arreter(ressource.Id))
            {
                arretees++;
            }
        }

        return arretees;
    }

    /// <summary>
    /// Applique la politique de sortie : ce qui doit mourir avec le produit meurt.
    /// </summary>
    /// <remarks>
    /// Appelé à la fermeture de SenSÉ. Sans cela, un serveur garde la carte occupée pendant que
    /// son propriétaire n'existe plus, et l'utilisateur n'a aucun moyen ordinaire de l'arrêter :
    /// ces processus héritent des droits élevés du produit.
    /// </remarks>
    public static int AuRevoir(Action<string>? journal)
    {
        var arretees = 0;

        foreach (var ressource in Lister().Where(r => r.Politique == ALaFermeture.Tuer))
        {
            journal?.Invoke($"Fermeture : arrêt de « {ressource.Nom} ».");

            if (Arreter(ressource.Id))
            {
                arretees++;
            }
        }

        return arretees;
    }

    /// <summary>La mémoire vidéo occupée sur la carte, toutes applications confondues.</summary>
    /// <remarks>
    /// Le total, faute de mieux : la carte ne dit pas qui consomme quoi. À montrer à côté du coût
    /// déclaré, pour que l'écart se voie plutôt que de se deviner.
    /// </remarks>
    public static (int Utilise, int Total) MemoireVideo()
    {
        try
        {
            var depart = new ProcessStartInfo("nvidia-smi")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            depart.ArgumentList.Add("--query-gpu=memory.used,memory.total");
            depart.ArgumentList.Add("--format=csv,noheader,nounits");

            using var processus = Process.Start(depart);

            if (processus is null)
            {
                return (0, 0);
            }

            // Les deux sorties se vident ensemble : voir Tuyaux.Vider. Lire l'une jusqu'au bout
            // pendant que l'autre se remplit bloque le programme lance, qui ne se termine donc
            // jamais — et l'attente bornee placee apres n'est alors jamais atteinte. nvidia-smi
            // ecrit peu, mais un avertissement de pilote suffit a remplir le tampon d'erreur, et
            // cet appel est sur le chemin d'ouverture du generateur : le gel se verrait a l'ecran.
            var (lu, _) = Tuyaux.Vider(processus, TimeSpan.FromSeconds(4));

            var morceaux = lu.Split(',', StringSplitOptions.TrimEntries);

            return morceaux.Length >= 2
                   && int.TryParse(morceaux[0], out var utilise)
                   && int.TryParse(morceaux[1], out var total)
                ? (utilise, total)
                : (0, 0);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Pas de carte NVIDIA, ou pas d'outil : le produit doit fonctionner quand même.
            return (0, 0);
        }
    }
}
