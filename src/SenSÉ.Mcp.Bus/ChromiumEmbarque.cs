using System.IO.Compression;

namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Le Chromium du projet : téléchargé une fois dans <c>Outils\Chromium</c>, puis épinglé.
/// </summary>
/// <remarks>
/// <b>Pourquoi pas embarqué dans l'installeur.</b> L'archive fait 357 Mo. La mettre dans le produit
/// alourdirait chaque installation et chaque mise à jour de SenSÉ pour une pièce que la plupart des
/// usages ne réclament jamais, et il faudrait la re-livrer entière à chaque révision de Chromium.
/// Téléchargée à la demande, elle coûte une fois ce qu'elle coûte, et le dépôt n'en porte rien.
///
/// <para><b>Pourquoi pas le navigateur de la machine.</b> C'est ce que faisait
/// <see cref="BrowserLauncher"/>, et ça marche — mais on hérite alors de ce que l'utilisateur a
/// installé, de sa version, de ses extensions et de ses mises à jour silencieuses. Un pilotage par
/// CDP repose sur des détails de protocole qui bougent d'une version à l'autre : un navigateur qu'on
/// choisit est un comportement qu'on peut tenir, celui de la machine est une surprise à chaque
/// démarrage.</para>
///
/// <para><b>Épinglé, et pas « le dernier ».</b> La révision est résolue une seule fois — au premier
/// téléchargement — puis écrite dans <c>revision.txt</c> à côté du binaire. Les fois suivantes,
/// c'est ce fichier qui fait foi. Sans quoi « télécharger une fois » deviendrait « télécharger la
/// version du jour », et le comportement changerait sous les pieds de l'utilisateur sans que rien
/// ne l'ait demandé. Mettre à jour reste possible, mais c'est un geste, pas un effet de bord :
/// effacer le dossier et redemander.</para>
///
/// <para><b>Ce qui n'est pas vérifié.</b> Les instantanés Chromium ne publient pas d'empreinte
/// stable par fichier, donc il n'y a pas de somme de contrôle à comparer. Ce sur quoi on s'appuie :
/// HTTPS vers le stockage officiel de Google, et le fait que l'archive s'ouvre et contienne bien
/// l'exécutable attendu. Un téléchargement interrompu laisse un <c>.part</c> qui n'est jamais pris
/// pour une installation valide.</para>
/// </remarks>
public static class ChromiumEmbarque
{
    private const string Instantanes = "https://storage.googleapis.com/chromium-browser-snapshots/Win_x64";

    /// <summary>Le dossier du navigateur, à côté du produit.</summary>
    public static string Dossier(string? racine = null)
        => Path.Combine(racine ?? RacineProduit(), "Outils", "Chromium");

    /// <summary>
    /// La racine du produit, quel que soit l'exécutable qui pose la question.
    /// </summary>
    /// <remarks>
    /// <b>Pas <see cref="AppContext.BaseDirectory"/>.</b> mcp-cdp vit dans <c>Outils\Cdp\</c> et
    /// mcp-saisie dans <c>Outils\McpSaisie\</c> : pour eux, « BaseDirectory + Outils » désigne un
    /// <c>Outils</c> imbriqué dans le premier. Ce n'est pas une hypothèse — le dossier
    /// <c>Outils\Cdp\Outils\CdpUserData</c> existe sur cette machine, laissé par exactement cette
    /// erreur, et il aurait fallu la refaire ici pour ranger un navigateur de 357 Mo au mauvais
    /// endroit.
    ///
    /// <para>La réponse est tenue par <see cref="Emplacements.RacineProduit"/>, depuis qu'un
    /// deuxième appelant en a eu besoin : le dossier des captures se trompait de la même façon.</para>
    /// </remarks>
    public static string RacineProduit() => Emplacements.RacineProduit();

    private static string FichierRevision(string? racine = null)
        => Path.Combine(Dossier(racine), "revision.txt");

    /// <summary>
    /// L'exécutable du Chromium du projet, ou null s'il n'est pas installé.
    /// </summary>
    /// <remarks>
    /// L'archive se déplie en <c>chrome-win\chrome.exe</c>. On cherche aussi à la racine, pour le
    /// cas où quelqu'un aurait déplié à la main sans garder le sous-dossier.
    /// </remarks>
    public static string? Exe(string? racine = null)
    {
        foreach (var candidat in new[]
                 {
                     Path.Combine(Dossier(racine), "chrome-win", "chrome.exe"),
                     Path.Combine(Dossier(racine), "chrome.exe"),
                 })
        {
            if (File.Exists(candidat)) return candidat;
        }

        return null;
    }

    /// <summary>Vrai si le navigateur du projet est installé et prêt.</summary>
    public static bool EstInstalle(string? racine = null) => Exe(racine) is not null;

    /// <summary>
    /// Installe le Chromium du projet s'il manque. Rend le chemin de l'exécutable, ou null.
    /// </summary>
    /// <remarks>
    /// Sans effet s'il est déjà là : c'est ce qui permet de l'appeler sans se demander si quelqu'un
    /// l'a déjà fait. Le téléchargement écrit dans un <c>.part</c> et ne le renomme qu'une fois
    /// complet, pour qu'une coupure de réseau ne laisse pas une archive tronquée que la fois
    /// suivante prendrait pour bonne.
    /// </remarks>
    public static async Task<string?> InstallerAsync(
        Action<string>? journal = null, string? racine = null, CancellationToken arret = default)
    {
        if (Exe(racine) is { } deja)
        {
            journal?.Invoke($"Chromium du projet deja installe : {deja}");
            return deja;
        }

        var dossier = Dossier(racine);
        Directory.CreateDirectory(dossier);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        var revision = await RevisionAsync(http, racine, journal, arret).ConfigureAwait(false);
        if (revision is null) return null;

        var archive = Path.Combine(dossier, "chrome-win.zip");
        var partiel = archive + ".part";

        try
        {
            var url = $"{Instantanes}/{revision}/chrome-win.zip";
            journal?.Invoke($"Chromium : telechargement de la revision {revision} ({url})");

            using (var reponse = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, arret).ConfigureAwait(false))
            {
                reponse.EnsureSuccessStatusCode();

                var attendu = reponse.Content.Headers.ContentLength ?? 0;
                journal?.Invoke($"Chromium : {attendu / (1024 * 1024)} Mo a recevoir");

                await using var source = await reponse.Content.ReadAsStreamAsync(arret).ConfigureAwait(false);
                await using var cible = File.Create(partiel);
                await source.CopyToAsync(cible, arret).ConfigureAwait(false);
            }

            if (File.Exists(archive)) File.Delete(archive);
            File.Move(partiel, archive);

            journal?.Invoke("Chromium : extraction…");
            ZipFile.ExtractToDirectory(archive, dossier, overwriteFiles: true);
            File.Delete(archive);

            var exe = Exe(racine);
            if (exe is null)
            {
                journal?.Invoke("Chromium : l'archive ne contenait pas chrome.exe la ou il etait attendu.");
                return null;
            }

            // La revision n'est ecrite qu'une fois l'executable en place : un marqueur pose avant
            // ferait passer une installation ratee pour une installation epinglee.
            await File.WriteAllTextAsync(FichierRevision(racine), revision, arret).ConfigureAwait(false);

            journal?.Invoke($"Chromium du projet installe : {exe} (revision {revision})");
            return exe;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"Chromium : installation echouee : {ex.GetType().Name}: {ex.Message}");

            try { if (File.Exists(partiel)) File.Delete(partiel); } catch { /* rien de mieux a tenter */ }

            return null;
        }
    }

    /// <summary>La révision épinglée si elle existe, sinon celle que publie le stockage.</summary>
    private static async Task<string?> RevisionAsync(
        HttpClient http, string? racine, Action<string>? journal, CancellationToken arret)
    {
        var marqueur = FichierRevision(racine);

        if (File.Exists(marqueur))
        {
            var epinglee = (await File.ReadAllTextAsync(marqueur, arret).ConfigureAwait(false)).Trim();
            if (epinglee.Length > 0)
            {
                journal?.Invoke($"Chromium : revision epinglee {epinglee}");
                return epinglee;
            }
        }

        try
        {
            var derniere = (await http.GetStringAsync($"{Instantanes}/LAST_CHANGE", arret).ConfigureAwait(false)).Trim();

            if (derniere.Length == 0 || !derniere.All(char.IsDigit))
            {
                journal?.Invoke($"Chromium : revision illisible rendue par le stockage : « {derniere} »");
                return null;
            }

            return derniere;
        }
        catch (Exception ex)
        {
            journal?.Invoke($"Chromium : revision introuvable : {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
