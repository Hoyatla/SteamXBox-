using System.Diagnostics;
using System.Text;

namespace SteamXBox.Tools.Serveurs;

/// <summary>
/// Lance un serveur local en tâche de fond, et garde la trace de son démarrage.
/// </summary>
/// <remarks>
/// <b>Écrit une fois parce que le copier a coûté deux fois.</b> ComfyUI et le modèle de langage
/// se lancent de la même manière, et la première version passait par <c>cmd.exe</c> avec une
/// redirection <c>&gt; fichier</c>. .NET remet des guillemets autour d'un argument déjà entouré de
/// guillemets, <c>cmd</c> rejette la ligne, et rien ne démarre — sans le moindre message, pas même
/// un fichier journal. Le défaut a été copié dans les deux lanceurs avant d'être vu une seule fois.
///
/// <para>
/// Ce qu'il ne faut surtout pas faire non plus : garder les deux sorties dans des tuyaux que nous
/// lisons. Un serveur vit plus longtemps que la fenêtre qui l'a lancé, et le jour où celle-ci se
/// ferme, le tuyau casse sous lui. Le fichier de journal est donc ouvert par le shell, au nom du
/// serveur.
/// </para>
/// </remarks>
internal static class ServeurLocal
{
    private static readonly object Plume = new();

    /// <summary>Les serveurs lancés, gardés en vie tant que le produit tourne.</summary>
    /// <remarks>
    /// Sans référence, le ramasse-miettes libérerait l'objet <see cref="Process"/> et, avec lui,
    /// les lecteurs de flux attachés.
    /// </remarks>
    private static readonly List<Process> Vivants = [];

    /// <summary>Démarre un programme, sortie et erreur consignées dans un fichier.</summary>
    /// <remarks>
    /// <b>La redirection est faite par le shell, pas par nous.</b> Un tuyau appartient au processus
    /// qui l'ouvre : quand SteamXBox se ferme, ses lecteurs meurent, le tuyau casse, et le serveur
    /// — qui lui survit — se met à échouer sur <i>chaque</i> ligne qu'il écrit. Constaté dans un
    /// rapport d'erreur ComfyUI : des dizaines d'<c>OSError: [Errno 22] Invalid argument</c> autour
    /// de la vraie panne, qu'elles noyaient. Un fichier ouvert par <c>cmd</c> appartient au serveur
    /// et vit aussi longtemps que lui.
    ///
    /// <para>
    /// La forme des guillemets n'est pas négociable : <c>cmd /c "…"</c> demande que la commande
    /// entière soit entourée d'une paire supplémentaire. Et il faut passer par <c>Arguments</c>, pas
    /// par <c>ArgumentList</c> — celui-ci rajoute sa propre couche de guillemets, <c>cmd</c> rejette
    /// la ligne, et rien ne démarre sans le moindre message. C'est le défaut qui a coûté deux
    /// séances.
    /// </para>
    /// </remarks>
    /// <summary>Ce qu'une ressource déclare d'elle-même avant d'exister.</summary>
    /// <remarks>
    /// Déclaré à l'avance parce que le processus, lui, n'existe qu'après le démarrage — et que
    /// c'est justement au démarrage qu'il faut savoir s'il y a la place.
    /// </remarks>
    internal sealed record Identite(
        string Id,
        string Nom,
        string Proprietaire,
        Poids Poids,
        int CoutVideoMo,
        ALaFermeture Politique);

    internal static Process? Demarrer(
        string programme,
        IReadOnlyList<string> arguments,
        string dossier,
        string journal,
        IReadOnlyDictionary<string, string>? environnement = null,
        Identite? identite = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journal) ?? dossier);

        var commande = new StringBuilder("/c \"");
        commande.Append('"').Append(programme).Append('"');

        foreach (var argument in arguments)
        {
            commande.Append(' ');
            commande.Append(argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument);
        }

        commande.Append(" > \"").Append(journal).Append("\" 2>&1\"");

        var depart = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dossier,
            Arguments = commande.ToString(),
        };

        if (environnement is not null)
        {
            foreach (var paire in environnement)
            {
                depart.Environment[paire.Key] = paire.Value;
            }
        }

        var processus = Process.Start(depart);

        if (processus is null)
        {
            return null;
        }

        lock (Plume)
        {
            Vivants.Add(processus);
        }

        // Inscrite au registre : c'est ce qui permet de la retrouver, de connaître son coût, et de
        // l'arrêter à la fermeture. Sans inscription, elle devient un orphelin qu'il faut deviner
        // en parcourant les processus de la machine.
        if (identite is not null)
        {
            Ressources.Inscrire(new Ressource(
                identite.Id,
                identite.Nom,
                identite.Proprietaire,
                identite.Poids,
                identite.CoutVideoMo,
                identite.Politique,
                processus,
                DateTime.Now));
        }

        return processus;
    }

}
