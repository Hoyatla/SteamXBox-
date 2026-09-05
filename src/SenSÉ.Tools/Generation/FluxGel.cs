using System.Text.Json;
using SenSÉ.Plugins;

namespace SenSÉ.Tools.Generation;

/// <summary>Un nom de fichier gelé dans un flux, que rien ne recouvre.</summary>
/// <param name="Outil">L'outil en cause.</param>
/// <param name="Flux">Le fichier de flux, sans son chemin.</param>
/// <param name="Noeud">Le nœud qui porte l'entrée.</param>
/// <param name="Entree">L'entrée gelée.</param>
/// <param name="Valeur">Le nom de fichier qui y est écrit.</param>
/// <param name="Conseil">Ce qu'il faut faire pour que cela cesse.</param>
public readonly record struct GelTrouve(
    string Outil, string Flux, string Noeud, string Entree, string Valeur, string Conseil)
{
    /// <summary>La phrase à montrer, journal ou épreuve.</summary>
    public override string ToString()
        => $"{Outil} : {Noeud}.{Entree} vaut « {Valeur} » dans {Flux} — {Conseil}";
}

/// <summary>
/// Repère les noms de fichiers qu'un flux fige et que son manifeste ne recouvre pas.
/// </summary>
/// <remarks>
/// <b>Ce que cette règle empêche.</b> Un flux qui nomme <c>wan2.1_i2v_480p_14B.safetensors</c>
/// fonctionne jusqu'au jour où ce fichier n'est plus là, et ce jour-là il ne se plaint pas : il
/// échoue au premier clic, des mois plus tard, sur un nom que plus personne ne reconnaît. Deux flux
/// de cette machine ont pourri exactement ainsi, en silence, et rien ne l'avait signalé.
///
/// <para>
/// <b>Recouvrir ne suffit pas.</b> Le champ qui recouvre doit lui-même tirer ses valeurs de la
/// machine — un <c>from</c> — ou être un fichier que l'utilisateur désigne. Un <c>choice</c> aux
/// options écrites à la main déplacerait simplement le gel du flux vers le manifeste.
/// </para>
///
/// <para>
/// <b>Un fichier se reconnaît à son extension, et non en interrogeant le générateur.</b> Demander à
/// ComfyUI quelles entrées sont des choix de fichiers serait plus exact, mais exigerait un serveur
/// démarré — deux minutes d'attente au lancement du produit, et une épreuve inexécutable sur une
/// machine d'intégration. Une vérification qui a besoin d'un serveur finit par être désactivée, et
/// une vérification désactivée ne vérifie rien.
/// </para>
///
/// <para>
/// <b>Cette règle n'a qu'une écriture.</b> Elle sert à l'épreuve qui garde les outils livrés et à
/// l'avertissement qui accueille les outils tiers ; deux implémentations auraient fini par ne plus
/// dire la même chose, et c'est celle qui ment qu'on aurait crue.
/// </para>
/// </remarks>
public static class FluxGel
{
    /// <summary>Les extensions qui trahissent un nom de fichier.</summary>
    private static readonly string[] Extensions =
    [
        ".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf", ".sft",
        ".png", ".jpg", ".jpeg", ".webp",
    ];

    /// <summary>
    /// Examine un outil et rend ce qu'il fige.
    /// </summary>
    /// <param name="outil">Le manifeste, tel qu'il a été lu.</param>
    /// <param name="resoudre">
    /// Traduit une cible de manifeste en chemins réels — c'est l'hôte qui sait ce que valent
    /// <c>{tools}</c> et les autres jetons, et il n'y a pas de raison de le redire ici.
    /// </param>
    public static IReadOnlyList<GelTrouve> Examiner(
        PluginManifest outil, Func<string, string> resoudre)
    {
        var trouves = new List<GelTrouve>();

        foreach (var action in outil.Content)
        {
            if (!action.Kind.Equals("action", StringComparison.OrdinalIgnoreCase)
                || !action.Does.Equals(PluginActions.Flux, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Examiner(outil, resoudre(action.Target), trouves);
        }

        return trouves;
    }

    private static void Examiner(PluginManifest outil, string cible, List<GelTrouve> trouves)
    {
        var lecture = FluxTravail.Lire(cible);

        // Un flux illisible ou absent n'est pas l'affaire de cette règle : l'arbitre le dira au
        // lancement, en termes bien plus précis, et un avertissement de plus au démarrage ne ferait
        // que noyer celui qui compte.
        if (lecture.Faute.Length > 0 || !File.Exists(lecture.Chemin))
        {
            return;
        }

        JsonDocument flux;

        try
        {
            flux = JsonDocument.Parse(File.ReadAllText(lecture.Chemin));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return;
        }

        using (flux)
        {
            if (flux.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var nom = Path.GetFileName(lecture.Chemin);

            foreach (var noeud in flux.RootElement.EnumerateObject())
            {
                if (noeud.Value.ValueKind != JsonValueKind.Object
                    || !noeud.Value.TryGetProperty("inputs", out var entrees)
                    || entrees.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var entree in entrees.EnumerateObject())
                {
                    if (entree.Value.ValueKind != JsonValueKind.String
                        || entree.Value.GetString() is not { } ecrit
                        || !Extensions.Any(e => ecrit.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (Conseil(outil, lecture.Reglages, noeud.Name, entree.Name) is { } manque)
                    {
                        trouves.Add(new GelTrouve(
                            outil.Id, nom, noeud.Name, entree.Name, ecrit, manque));
                    }
                }
            }
        }
    }

    /// <summary>Ce qu'il manque pour que cette entrée cesse d'être gelée, ou null.</summary>
    private static string? Conseil(
        PluginManifest outil, IReadOnlyList<ReglageFlux> reglages, string noeud, string entree)
    {
        var reglage = reglages.FirstOrDefault(r =>
            r.Noeud == noeud && r.Entree.Equals(entree, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(reglage.Entree))
        {
            return $"ajoute « {noeud}.{entree}={{un_champ}} » à la cible, et le champ qui va avec.";
        }

        // La cible écrit {identifiant} ; c'est ce champ-là qu'il faut retrouver dans le manifeste.
        var nomme = reglage.Valeur.Trim();

        if (!nomme.StartsWith('{') || !nomme.EndsWith('}'))
        {
            return $"la cible y écrit « {nomme} » en dur : ce doit être un champ, écrit {{identifiant}}.";
        }

        var champ = outil.Content.Find(c =>
            c.Id.Equals(nomme[1..^1], StringComparison.OrdinalIgnoreCase));

        if (champ is null)
        {
            return $"la cible nomme « {nomme} », qui n'est pas un champ de ce manifeste.";
        }

        if (champ.Kind.Equals("file", StringComparison.OrdinalIgnoreCase) || champ.From.Length > 0)
        {
            return null;
        }

        return $"le champ « {champ.Id} » fige ses valeurs : donne-lui un « from » "
            + $"(par exemple \"from\": \"UnNoeud.{entree}\") pour qu'il les lise sur la machine.";
    }
}
