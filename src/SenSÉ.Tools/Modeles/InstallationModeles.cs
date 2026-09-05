using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SenSÉ.Tools.Modeles;

/// <summary>
/// Télécharge un jeu de modèles, en le disant et en pouvant reprendre.
/// </summary>
/// <remarks>
/// <b>Trois exigences, et chacune vient d'un défaut constaté.</b>
///
/// <list type="bullet">
/// <item>
/// <b>Reprendre.</b> Treize gigaoctets sur une connexion domestique, c'est une heure pendant
/// laquelle une coupure est plausible. Recommencer depuis zéro à chaque incident rendrait
/// l'installation impossible sur une mauvaise ligne — celle, précisément, des machines qu'on ne
/// choisit pas.
/// </item>
/// <item>
/// <b>Constater la taille exacte.</b> Un fichier tronqué existe et ment. Découvert plus tard, il se
/// présente comme une erreur de format au premier usage, et l'on cherche partout sauf dans le
/// réseau.
/// </item>
/// <item>
/// <b>Ne renommer qu'à la fin.</b> Le téléchargement va dans un <c>.part</c> et ne prend son vrai
/// nom qu'une fois la taille vérifiée. Sans cela, un fichier à moitié écrit apparaîtrait dans la
/// liste des modèles installés, et le générateur le proposerait.
/// </item>
/// </list>
/// </remarks>
public static class InstallationModeles
{
    /// <summary>Ce qu'on annonce pendant qu'un jeu s'installe.</summary>
    /// <param name="Fichier">Le fichier en cours, tel qu'il s'appellera.</param>
    /// <param name="Recus">Octets déjà là, reprise comprise.</param>
    /// <param name="Total">Octets attendus pour ce fichier.</param>
    /// <param name="Rang">Le rang du fichier dans le jeu, à partir de 1.</param>
    /// <param name="Combien">Combien de fichiers compte le jeu.</param>
    public readonly record struct Avancement(
        string Fichier, long Recus, long Total, int Rang, int Combien)
    {
        /// <summary>La part faite de ce fichier, de 0 à 1.</summary>
        public double Part => Total > 0 ? Math.Min(1d, (double)Recus / Total) : 0d;
    }

    /// <summary>
    /// Assez gros pour ne pas multiplier les allers-retours, assez petit pour que l'avancement
    /// bouge et qu'une annulation soit ressentie tout de suite.
    /// </summary>
    private const int Tampon = 1 << 20;

    private static readonly HttpClient Client = new()
    {
        // Le temps d'établir la réponse, pas celui de la recevoir : le corps est lu en continu, et
        // un délai global couperait un gros fichier en cours de route.
        Timeout = TimeSpan.FromMinutes(2),
        DefaultRequestHeaders = { { "User-Agent", "SenSÉ" } },
    };

    /// <summary>Installe un jeu, fichier par fichier.</summary>
    /// <returns>Une phrase à montrer : vide si tout est en place, la raison sinon.</returns>
    public static string Installer(
        JeuModeles jeu,
        string racine,
        Action<Avancement>? avancement,
        Action<string>? journal,
        CancellationToken annulation = default)
    {
        var rang = 0;

        foreach (var fichier in jeu.Fichiers)
        {
            rang++;

            var ou = Path.Combine(racine, fichier.Vers.Replace('/', Path.DirectorySeparatorChar));
            var nom = Path.GetFileName(ou);

            if (File.Exists(ou) && new FileInfo(ou).Length == fichier.Octets)
            {
                journal?.Invoke($"{nom} est déjà là.");
                avancement?.Invoke(new Avancement(nom, fichier.Octets, fichier.Octets, rang, jeu.Fichiers.Count));

                continue;
            }

            journal?.Invoke($"{nom} — {JeuxModeles.Poids(fichier.Octets)}");

            if (Un(fichier, ou, nom, rang, jeu.Fichiers.Count, avancement, journal, annulation) is { } refus)
            {
                return refus;
            }
        }

        journal?.Invoke($"« {jeu.Nom} » est installé.");

        return "";
    }

    /// <summary>Un fichier : reprise, réception, vérification, renommage.</summary>
    private static string? Un(
        FichierModele fichier,
        string ou,
        string nom,
        int rang,
        int combien,
        Action<Avancement>? avancement,
        Action<string>? journal,
        CancellationToken annulation)
    {
        var partiel = ou + ".part";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ou)!);

            var deja = File.Exists(partiel) ? new FileInfo(partiel).Length : 0;

            // Un .part plus gros que la cible n'est pas une reprise possible : c'est un reste d'une
            // autre version du fichier. On repart de zéro plutôt que de coller deux moitiés
            // étrangères l'une à l'autre.
            if (deja > fichier.Octets)
            {
                File.Delete(partiel);
                deja = 0;
            }

            using var demande = new HttpRequestMessage(HttpMethod.Get, fichier.Url);

            if (deja > 0)
            {
                demande.Headers.Range = new RangeHeaderValue(deja, null);
                journal?.Invoke($"reprise de {nom} à {JeuxModeles.Poids(deja)}");
            }

            using var reponse = Client
                .Send(demande, HttpCompletionOption.ResponseHeadersRead, annulation);

            // Le serveur ignore la reprise et renvoie tout : il faut alors réécrire depuis le début,
            // sans quoi on collerait le fichier entier derrière ce qu'on avait déjà.
            if (deja > 0 && reponse.StatusCode != HttpStatusCode.PartialContent)
            {
                journal?.Invoke($"{nom} : la reprise a été refusée, on recommence.");
                deja = 0;
            }

            if (!reponse.IsSuccessStatusCode)
            {
                return $"{nom} : le serveur a répondu {(int)reponse.StatusCode}.";
            }

            Recevoir(reponse, partiel, deja, fichier, nom, rang, combien, avancement, annulation);

            var recu = new FileInfo(partiel).Length;

            if (recu != fichier.Octets)
            {
                return $"{nom} : {JeuxModeles.Poids(recu)} reçus au lieu de "
                    + $"{JeuxModeles.Poids(fichier.Octets)}. Le fichier partiel est conservé, "
                    + "relancer reprendra où il s'est arrêté.";
            }

            File.Move(partiel, ou, overwrite: true);

            return null;
        }
        catch (OperationCanceledException)
        {
            return $"{nom} : interrompu. Le fichier partiel est conservé.";
        }
        catch (HttpRequestException exception)
        {
            return $"{nom} : {exception.Message}";
        }
        catch (IOException exception)
        {
            return $"{nom} : {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            return $"{nom} : écriture refusée. {exception.Message}";
        }
    }

    private static void Recevoir(
        HttpResponseMessage reponse,
        string partiel,
        long deja,
        FichierModele fichier,
        string nom,
        int rang,
        int combien,
        Action<Avancement>? avancement,
        CancellationToken annulation)
    {
        using var source = reponse.Content.ReadAsStream(annulation);
        using var sortie = new FileStream(
            partiel,
            deja > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            Tampon);

        var tampon = new byte[Tampon];
        var recus = deja;
        var dernier = 0L;

        while (true)
        {
            var lu = source.Read(tampon, 0, tampon.Length);

            if (lu <= 0)
            {
                break;
            }

            sortie.Write(tampon, 0, lu);
            recus += lu;

            // Annoncé tous les huit mégaoctets : à chaque tampon, l'affichage passerait son temps à
            // se redessiner pour un fichier qui en compte huit mille.
            if (recus - dernier >= 8L * Tampon)
            {
                dernier = recus;
                avancement?.Invoke(new Avancement(nom, recus, fichier.Octets, rang, combien));
            }
        }

        avancement?.Invoke(new Avancement(nom, recus, fichier.Octets, rang, combien));
    }

    /// <summary>La mémoire vidéo libre, en mégaoctets, ou zéro si on ne sait pas.</summary>
    /// <remarks>
    /// Ce qui reste après le bureau, et non ce que la carte annonce : Windows en garde une part qui
    /// ne se rend pas — un gestionnaire de fenêtres, un navigateur, un logiciel de clavier. Mesuré
    /// sur la machine de développement : 1 404 Mio sur 12 282 avant qu'un seul modèle ne soit
    /// chargé. C'est cette différence qui décide si un jeu tient.
    /// </remarks>
    public static int MemoireLibreMo()
    {
        var (utilise, total) = Serveurs.Ressources.MemoireVideo();

        return total > 0 ? Math.Max(0, total - utilise) : 0;
    }

    /// <summary>Une phrase disant ce que la carte permet, ou qu'on ne sait pas.</summary>
    public static string Carte()
    {
        var (utilise, total) = Serveurs.Ressources.MemoireVideo();

        if (total <= 0)
        {
            return "Mémoire vidéo inconnue : les jeux sont tous proposés, à vous de juger.";
        }

        var libre = Math.Max(0, total - utilise).ToString(CultureInfo.InvariantCulture);

        return $"Carte : {total.ToString(CultureInfo.InvariantCulture)} Mio au total, "
            + $"{libre} Mio libres.";
    }
}
