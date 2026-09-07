using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Télécharge, extrait et déclare un moteur de langage qui n est pas encore
/// sur la machine. Écrit le moteur.json dans Outils/Langages/&lt;id&gt;/, puis
/// déclenche <see cref="Detecteur.Recharger"/>.
/// </summary>
/// <remarks>
/// Squelette : les vraies méthodes de téléchargement (<see cref="TelechargerAsync"/>,
/// <see cref="ExtraireAsync"/>) sont à implémenter par langage. La machine de
/// référence a déjà les six langages du Catalogue, donc ce code ne sert que
/// sur une machine où java/csharp/c/rust manquent.
/// </remarks>
public static class Installateur
{
    public sealed record ResultatInstallation(bool Ok, string Message, string? CheminMoteurJson = null);

    /// <summary>
    /// Orchestration : si le langage est déjà là, no-op silencieux. Sinon,
    /// télécharge dans un dossier temporaire, extrait dans Outils/Langages/&lt;id&gt;,
    /// écrit le moteur.json, force la re-détection.
    /// </summary>
    public static async Task<ResultatInstallation> DemarrerInstallationAsync(
        string id, IProgresInstallation? progres = null, CancellationToken ct = default)
    {
        var entree = Catalogue.Trouver(id);
        if (entree is null) return new(false, "langage inconnu du catalogue : " + id);

        // Deja la : rien a faire (cas frequent sur la machine de reference).
        if (Catalogue.EstInstalle(id))
            return new(true, "deja installe : " + id);

        progres?.Etape("preparation", 0);
        var dossierCible = Path.Combine(Detecteur.Racine, entree.Id);
        Directory.CreateDirectory(dossierCible);

        // Telechargement dans un sous-dossier temp à cote du dossier final.
        var tempDir = Path.Combine(dossierCible, "_download");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        progres?.Etape("telechargement", 10);
        var archive = await TelechargerAsync(entree, tempDir, progres, ct).ConfigureAwait(false);
        if (archive is null) return new(false, "telechargement echoue : " + entree.UrlTelechargement);

        progres?.Etape("extraction", 60);
        var exe = await ExtraireAsync(entree, archive, dossierCible, progres, ct).ConfigureAwait(false);

        // Nettoyage du temp
        try { Directory.Delete(tempDir, true); } catch { /* best effort */ }

        if (exe is null) return new(false, "extraction echouee, executable introuvable : " + entree.Id);

        progres?.Etape("manifeste", 90);
        EcrireManifesteParDefaut(entree, dossierCible, exe);

        progres?.Etape("detection", 99);
        Detecteur.Recharger();

        progres?.Etape("termine", 100);
        return new(true, "installe : " + entree.Id, Path.Combine(dossierCible, "moteur.json"));
    }

    /// <summary>
    /// Télécharge l'archive depuis l'URL du catalogue vers tempDir. Retourne
    /// le chemin de l'archive, ou null en cas d'échec.
    /// </summary>
    /// <remarks>À implémenter. TODO : HttpClient + barre de progression.</remarks>
    private static Task<string?> TelechargerAsync(
        Catalogue.Entree e, string tempDir, IProgresInstallation? progres, CancellationToken ct)
    {
        // Squelette : le téléchargement réel dépend du langage (zip, exe, msi, etc.)
        // et du Catalogue.UrlTelechargement. Pour l instant on declare non
        // implementé, ce qui suffit sur la machine de reference.
        progres?.Erreur("telechargement non implemente pour " + e.Id);
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Extrait l'archive et retourne le chemin de l'executable attendu, ou
    /// null si l'extraction a échoué.
    /// </summary>
    /// <remarks>À implémenter par méthode (zip, tar.gz, msi silencieux, etc.).</remarks>
    private static Task<string?> ExtraireAsync(
        Catalogue.Entree e, string archive, string dossierCible,
        IProgresInstallation? progres, CancellationToken ct)
    {
        progres?.Erreur("extraction non implementee pour " + e.Id);
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Écrit un moteur.json générique quand le Catalogue ne donne pas la
    /// commande exacte. Le moteur sera sur-detecté au prochain appel.
    /// </summary>
    private static void EcrireManifesteParDefaut(Catalogue.Entree e, string dossier, string executable)
    {
        // On écrit un manifeste qui demande une vérification manuelle : la
        // commande de detection n est pas connue generiquement pour tous les
        // langages (rustup install produit rustc, mais winget produit un
        // wrapper, etc.). C est mieux que rien.
        var manifest = "{\n" +
            "  \"id\": \"" + e.Id + "\",\n" +
            "  \"nom\": \"" + e.Nom + "\",\n" +
            "  \"espace\": \"Codage\",\n" +
            "  \"rang\": \"demande\",\n" +
            "  \"extensions\": [\"." + e.Id + "\"],\n" +
            "  \"detection\": \"" + executable + " --version\",\n" +
            "  \"executable\": \"" + executable + "\",\n" +
            "  \"commande\": [\"{executable}\", \"{fichier}\"],\n" +
            "  \"compile\": false,\n" +
            "  \"latence_ms\": 1500,\n" +
            "  \"taille_mo\": " + e.TailleMo + "\n" +
            "}\n";
        File.WriteAllText(Path.Combine(dossier, "moteur.json"), manifest);
    }
}

/// <summary>Hook de progression pour l'UI (étape texte + pourcentage 0-100).</summary>
public interface IProgresInstallation
{
    void Etape(string etape, int pourcentage);
    void Erreur(string message);
}