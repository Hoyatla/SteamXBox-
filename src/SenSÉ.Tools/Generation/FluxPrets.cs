using System.Text;
using System.Text.Json;

namespace SenSÉ.Tools.Generation;

/// <summary>
/// Les flux déjà écrits et éprouvés, et ceux d'entre eux que cette machine peut lancer.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci corrige, mesuré le 24 août.</b> L'assistant, à qui l'on demandait une
/// vidéo, a composé un graphe de zéro : dix-sept appels d'outil, dix fautes rattrapées par le
/// vérificateur, un flux enfin valide — et une allocation de trente-six gigaoctets sur une carte
/// qui en a douze. Pendant ce temps un graphe Wan écrit, validé et taillé pour cette carte dormait
/// dans le dossier des flux, et rien ne le lui disait.
///
/// <para>
/// <b>C'est le principe du verbe <c>flux</c>, rendu atteignable.</b> Il est écrit depuis le début
/// qu'un modèle de langage local ne sait pas câbler un graphe et sait très bien en choisir un ;
/// mais l'assistant n'avait que de quoi composer. Choisir demande une liste, et la voici.
/// </para>
///
/// <para>
/// <b>Ce qui est offert est ce qui peut tourner.</b> Un flux dont un modèle manque n'est pas
/// proposé : le nommer reviendrait à envoyer le modèle vers un échec de chargement, plusieurs
/// minutes plus tard, sur un message qui ne renvoie pas à ce choix-ci.
/// </para>
/// </remarks>
public static class FluxPrets
{
    /// <summary>Les extensions d'un poids de modèle, et elles seules.</summary>
    /// <remarks>
    /// Sans les images, contrairement à la liste de <c>FluxGel</c>, qui traque tout nom de fichier
    /// figé. Ici la question est « ce flux peut-il tourner sur cette machine », et l'<c>example.png</c>
    /// que porte un <c>LoadImage</c> n'y répond pas : c'est un exemple, remplacé au lancement par
    /// l'image que l'utilisateur désigne. Le compter comme manquant écarterait des flux parfaitement
    /// lançables.
    /// </remarks>
    private static readonly string[] Poids =
        [".safetensors", ".gguf", ".ckpt", ".pt", ".pth", ".bin", ".sft"];

    private static bool EstUnPoids(string valeur)
        => Poids.Any(e => valeur.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Un flux prêt, tel qu'on le montre au modèle.</summary>
    /// <param name="Nom">Le nom du fichier, sans le chemin : c'est ce que la cible nommera.</param>
    /// <param name="Noeuds">Combien de nœuds il porte.</param>
    /// <param name="Modeles">Les poids qu'il nomme, dans l'ordre où ils apparaissent.</param>
    /// <param name="Manquants">Ceux de ces poids qui ne sont pas sur ce disque.</param>
    public sealed record Pret(
        string Nom,
        int Noeuds,
        IReadOnlyList<string> Modeles,
        IReadOnlyList<string> Manquants)
    {
        public bool Lancable => Manquants.Count == 0;
    }

    /// <summary>Examine un flux : ce qu'il porte, et ce qui lui manque ici.</summary>
    /// <param name="nom">Le nom du fichier.</param>
    /// <param name="json">Son contenu, au format API.</param>
    /// <param name="installe">Rend vrai quand ce nom de poids existe sur la machine.</param>
    public static Pret? Examiner(string nom, string json, Func<string, bool> installe)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var noeuds = 0;
            var modeles = new List<string>();

            foreach (var noeud in document.RootElement.EnumerateObject())
            {
                if (noeud.Value.ValueKind != JsonValueKind.Object
                    || !noeud.Value.TryGetProperty("class_type", out _))
                {
                    // Le format de l'éditeur porte « nodes » et « links » : ce n'est pas un flux
                    // soumettable, et le compter comme tel enverrait le modèle dans le mur.
                    return null;
                }

                noeuds++;

                if (!noeud.Value.TryGetProperty("inputs", out var entrees)
                    || entrees.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var entree in entrees.EnumerateObject())
                {
                    if (entree.Value.ValueKind == JsonValueKind.String
                        && entree.Value.GetString() is { } valeur
                        && EstUnPoids(valeur)
                        && !modeles.Contains(valeur, StringComparer.OrdinalIgnoreCase))
                    {
                        modeles.Add(valeur);
                    }
                }
            }

            return noeuds == 0
                ? null
                : new Pret(nom, noeuds, modeles, [.. modeles.Where(m => !installe(m))]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ce que le modèle lit : les flux lançables ici, et pourquoi les autres ne le sont pas.</summary>
    public static string Resumer(IReadOnlyList<Pret> prets)
    {
        if (prets.Count == 0)
        {
            return "Aucun flux prêt sur cette machine. Il faut en composer un — vérifie-le avec "
                + "flux_verifier avant de le lancer.";
        }

        var texte = new StringBuilder()
            .AppendLine("FLUX DÉJÀ ÉCRITS ET ÉPROUVÉS. Préfère toujours en lancer un plutôt que")
            .AppendLine("d'en composer un : ils sont taillés pour cette carte, un graphe improvisé")
            .AppendLine("ne l'est pas. Lance-le avec flux_lancer en donnant SON NOM DE FICHIER.")
            .AppendLine();

        foreach (var pret in prets.Where(p => p.Lancable))
        {
            texte.Append("· ").Append(pret.Nom)
                .Append("  (").Append(pret.Noeuds).AppendLine(" nœuds)");

            if (pret.Modeles.Count > 0)
            {
                texte.Append("    emploie : ").AppendLine(string.Join(", ", pret.Modeles));
            }
        }

        // Les autres sont dits aussi, et avec leur raison : « il n'y en a pas » ferait chercher
        // ailleurs, alors que le flux existe et qu'il ne manque qu'un fichier.
        var absents = prets.Where(p => !p.Lancable).ToList();

        if (absents.Count > 0)
        {
            texte.AppendLine().AppendLine("Non lançables ici, modèle absent :");

            foreach (var pret in absents)
            {
                texte.Append("· ").Append(pret.Nom)
                    .Append(" — manque ").AppendLine(string.Join(", ", pret.Manquants));
            }
        }

        return texte.ToString();
    }
}
