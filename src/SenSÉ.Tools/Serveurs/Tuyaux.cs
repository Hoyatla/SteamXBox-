using System.Diagnostics;

namespace SenSÉ.Tools.Serveurs;

/// <summary>
/// Vider les sorties d'un programme lancé, sans se bloquer avec lui.
/// </summary>
/// <remarks>
/// <b>Le 23 août, ce défaut a coûté une heure d'attente à l'utilisateur.</b> Le journal annonce
/// « Montage de 7 clips… » à 17:58:12 et n'écrit plus rien pendant soixante-quatre minutes ; il a
/// fini par fermer le produit. Le fichier produit portait l'horodatage de la fermeture : le montage
/// avait pris une seconde — <c>ffmpeg -c copy</c> ne réencode rien — et c'est notre attente qui
/// avait duré une heure.
///
/// <para>
/// <b>Le mécanisme.</b> Un programme lancé écrit sur deux tuyaux, dont les tampons font quelques
/// kilo-octets. Lire le premier jusqu'au bout avant de toucher au second laisse le second se
/// remplir ; le programme se bloque alors en écrivant, donc ne se termine pas, donc ne ferme jamais
/// le premier tuyau — que nous lisons toujours. Chacun attend l'autre, indéfiniment. Les outils qui
/// annoncent leur progression sur la sortie d'erreur — ffmpeg, les moteurs ncnn, la
/// reconnaissance de texte — remplissent ce tampon en quelques secondes.
/// </para>
///
/// <para>
/// <b>Pourquoi cela ne ressemble pas à une panne.</b> Aucune erreur, aucun plantage, un travail
/// déjà terminé sur le disque, et une interface qui affiche honnêtement la dernière étape connue.
/// C'est le pire des symptômes : il n'accuse rien ni personne, et le seul geste qu'il inspire —
/// attendre encore — est le seul qui ne mène nulle part.
/// </para>
/// </remarks>
public static class Tuyaux
{
    /// <summary>Les deux sorties d'un programme, lues ensemble, une fois qu'il a fini.</summary>
    /// <param name="processus">Le programme lancé, ses deux sorties redirigées.</param>
    /// <param name="patience">Au-delà, on cesse d'attendre. Sans borne si absent.</param>
    /// <returns>Ce qu'il a écrit sur la sortie standard, puis sur la sortie d'erreur.</returns>
    /// <remarks>
    /// Les deux lectures partent <b>avant</b> l'attente, et c'est tout le correctif : aucun des deux
    /// tampons ne peut plus se remplir sans être vidé.
    /// </remarks>
    public static (string Sortie, string Erreur) Vider(Process processus, TimeSpan? patience = null)
    {
        var sortie = processus.StandardOutput.ReadToEndAsync();
        var erreur = processus.StandardError.ReadToEndAsync();

        if (patience is { } borne)
        {
            // Le programme n'a pas rendu la main à temps. Ses tuyaux sont vidés depuis le début,
            // donc ce qu'il a écrit jusque-là est lisible : on le rend plutôt que de perdre la
            // seule trace de ce qui s'est passé.
            if (!processus.WaitForExit((int)borne.TotalMilliseconds))
            {
                return (Deja(sortie), Deja(erreur));
            }
        }
        else
        {
            processus.WaitForExit();
        }

        return (sortie.GetAwaiter().GetResult(), erreur.GetAwaiter().GetResult());
    }

    /// <summary>Ce qu'une lecture a déjà rendu, ou rien si elle est encore en cours.</summary>
    private static string Deja(Task<string> lecture)
        => lecture.IsCompletedSuccessfully ? lecture.GetAwaiter().GetResult() : "";
}
