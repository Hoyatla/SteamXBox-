using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Generation;

/// <summary>
/// Met les flux livrés là où le générateur va les chercher, dans la forme qu'il sait ouvrir.
/// </summary>
/// <remarks>
/// <b>Pourquoi une conversion et non une copie.</b> ComfyUI manipule deux formes du même graphe.
/// Celle que le produit exécute est dite « API » : un dictionnaire plat de nœuds, sans position ni
/// câblage explicite, parce que le moteur n'a besoin que des valeurs. Celle que l'interface ouvre
/// porte en plus des coordonnées, une liste de liens numérotés et l'ordre positionnel des réglages.
///
/// <para>
/// Le panneau des flux appelle <c>loadGraphData</c> directement, sans passer par la détection de
/// format que fait le glisser-déposer. Un fichier API posé dans le dossier apparaîtrait donc dans
/// la liste et s'ouvrirait vide — le pire des deux mondes, puisque rien ne signale l'erreur.
/// Vérifié dans le frontend installé : <c>openWorkflow</c> ne teste jamais <c>isApiJson</c>.
/// </para>
///
/// <para>
/// <b>La règle qui évite de deviner.</b> En forme API, une entrée dont la valeur est un tableau
/// <c>[nœud, rang]</c> est un câble ; tout le reste est une valeur écrite. C'est la convention de
/// ComfyUI lui-même, pas une heuristique : elle sépare câbles et réglages sans consulter aucun
/// catalogue, donc la conversion marche même générateur éteint.
/// </para>
/// </remarks>
public static class WorkflowsLivres
{
    /// <summary>Le dossier où ComfyUI range les flux de l'utilisateur.</summary>
    /// <remarks>
    /// Chemin imposé par ComfyUI, pas par nous : son panneau latéral ne lit que celui-là.
    /// </remarks>
    public const string DossierComfy = @"user\default\workflows";

    /// <summary>Le sous-dossier qui regroupe ce que SenSÉ livre.</summary>
    /// <remarks>
    /// Les flux du produit ne se mélangent pas à ceux de l'utilisateur : il doit pouvoir les
    /// reconnaître d'un coup d'œil, et une republication ne doit jamais écraser son travail.
    /// </remarks>
    public const string Marque = "SenSÉ";

    /// <summary>
    /// Publie tous les flux livrés dans le dossier des flux du générateur.
    /// </summary>
    /// <param name="racine">Le dossier du produit.</param>
    /// <param name="comfy">Le dossier de ComfyUI.</param>
    /// <param name="catalogue">Ce que le générateur déclare, s'il est joignable.</param>
    /// <returns>Ce qui a été publié, et ce qui a échoué.</returns>
    public static IReadOnlyList<string> Publier(string racine, string comfy, Catalogue? catalogue = null)
    {
        var depuis = Path.Combine(racine, "Flux");
        var vers = Path.Combine(comfy, DossierComfy, Marque);
        var dit = new List<string>();

        if (!Directory.Exists(depuis))
        {
            return [$"Aucun flux livré : {depuis} n'existe pas."];
        }

        Directory.CreateDirectory(vers);

        foreach (var fichier in Directory.EnumerateFiles(depuis, "*.json").OrderBy(f => f))
        {
            var nom = Path.GetFileName(fichier);

            try
            {
                var interfaceur = Convertir(File.ReadAllText(fichier), catalogue);

                File.WriteAllText(Path.Combine(vers, nom), interfaceur);
                dit.Add($"{nom} publié.");
            }
            catch (Exception exception)
                when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                dit.Add($"{nom} non publié : {exception.Message}");
            }
        }

        return dit;
    }

    /// <summary>
    /// Traduit un graphe de la forme exécutée vers la forme que l'interface ouvre.
    /// </summary>
    /// <param name="api">Le graphe en forme API.</param>
    /// <param name="catalogue">
    /// Ce que le générateur déclare. Facultatif : sans lui la conversion reste juste, mais un
    /// réglage laissé à sa valeur par défaut dans le graphe API n'aura pas de case à l'écran.
    /// </param>
    public static string Convertir(string api, Catalogue? catalogue = null)
    {
        var source = JsonNode.Parse(api)?.AsObject()
            ?? throw new JsonException("Le graphe n'est pas un objet.");

        var noeuds = new JsonArray();
        var liens = new JsonArray();
        var lien = 0;

        // Les rangs d'entrée sont attribués avant d'écrire quoi que ce soit : un câble doit
        // connaître la place qu'il occupera chez celui qui le reçoit, et cette place est celle
        // que nous lui donnons ici.
        var rangs = Rangs(source);
        var colonnes = Colonnes(source);
        var ordre = 0;

        // Tous les liens d'abord. Les sorties d'un nœud sont faites des câbles que ses
        // consommateurs demandent, et ceux-là sont plus loin dans le fichier : les écrire au fil de
        // l'eau laisserait débranché tout ce qui est chargé en premier.
        var branches = new Dictionary<string, List<(string Nom, int Numero)>>();

        foreach (var (cle, valeur) in source)
        {
            var schema = catalogue?.Noeud(valeur?["class_type"]?.GetValue<string>() ?? "");
            var entrees = valeur?["inputs"]?.AsObject() ?? [];
            var poses = new List<(string, int)>();
            var rang = 0;

            foreach (var (nom, _) in rangs[cle])
            {
                var cible = Cable(entrees[nom])!.Value;
                var type = schema?.Entree(nom)?.Type ?? "*";

                poses.Add((nom, ++lien));
                liens.Add(new JsonArray
                {
                    lien, Numero(cible.Noeud), cible.Rang, Numero(cle), rang++, type,
                });
            }

            branches[cle] = poses;
        }

        foreach (var (cle, valeur) in source)
        {
            var noeud = valeur?.AsObject();
            var classe = noeud?["class_type"]?.GetValue<string>();

            if (noeud is null || classe is null)
            {
                throw new JsonException($"Le nœud {cle} n'annonce pas de class_type.");
            }

            var schema = catalogue?.Noeud(classe);
            var entrees = noeud["inputs"]?.AsObject() ?? [];
            var cables = new JsonArray();

            foreach (var (nom, numero) in branches[cle])
            {
                cables.Add(new JsonObject
                {
                    ["name"] = nom,
                    ["type"] = schema?.Entree(nom)?.Type ?? "*",
                    ["link"] = numero,
                });
            }

            noeuds.Add(new JsonObject
            {
                ["id"] = Numero(cle),
                ["type"] = classe,
                ["pos"] = new JsonArray { colonnes[cle].X, colonnes[cle].Y },
                ["size"] = new JsonArray { 320, 100 },
                ["flags"] = new JsonObject(),
                ["order"] = ordre++,
                ["mode"] = 0,
                ["inputs"] = cables,
                ["outputs"] = Sorties(cle, source, schema, liens),
                ["properties"] = new JsonObject { ["Node name for S&R"] = classe },
                ["widgets_values"] = Reglages(entrees, schema),
            });
        }

        var graphe = new JsonObject
        {
            ["last_node_id"] = source.Count == 0 ? 0 : source.Select(p => Numero(p.Key)).Max(),
            ["last_link_id"] = lien,
            ["nodes"] = noeuds,
            ["links"] = liens,
            ["groups"] = new JsonArray(),
            ["config"] = new JsonObject(),
            ["extra"] = new JsonObject(),
            ["version"] = 0.4,
        };

        return graphe.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Une entrée câblée, c'est-à-dire un tableau <c>[nœud, rang]</c>.</summary>
    private static (string Noeud, int Rang)? Cable(JsonNode? valeur)
    {
        if (valeur is not JsonArray tableau || tableau.Count != 2)
        {
            return null;
        }

        var origine = tableau[0];
        var rang = tableau[1];

        return origine is null || rang is null
            ? null
            : (origine.ToString(), rang.GetValue<int>());
    }

    /// <summary>Les entrées câblées de chaque nœud, dans l'ordre où elles sont déclarées.</summary>
    private static Dictionary<string, List<(string Nom, string Vers)>> Rangs(JsonObject source)
    {
        var rangs = new Dictionary<string, List<(string, string)>>();

        foreach (var (cle, valeur) in source)
        {
            var entrees = valeur?["inputs"]?.AsObject();
            var liste = new List<(string, string)>();

            foreach (var (nom, contenu) in entrees ?? [])
            {
                if (Cable(contenu) is { } cable)
                {
                    liste.Add((nom, cable.Noeud));
                }
            }

            rangs[cle] = liste;
        }

        return rangs;
    }

    /// <summary>
    /// Les sorties d'un nœud, avec les câbles qui en partent.
    /// </summary>
    /// <remarks>
    /// L'interface recrée les sorties depuis la définition du nœud au chargement ; ce qui doit être
    /// écrit ici, c'est quels liens partent de quel rang, sans quoi le graphe s'ouvre débranché.
    /// </remarks>
    private static JsonArray Sorties(string cle, JsonObject source, NoeudSchema? schema, JsonArray liens)
    {
        var partants = new Dictionary<int, JsonArray>();

        foreach (var trait in liens)
        {
            var tableau = trait!.AsArray();

            if (tableau[1]!.GetValue<int>() != Numero(cle))
            {
                continue;
            }

            var rang = tableau[2]!.GetValue<int>();

            (partants.TryGetValue(rang, out var deja) ? deja : partants[rang] = [])
                .Add(tableau[0]!.GetValue<int>());
        }

        // Combien de sorties : ce que le nœud déclare, ou à défaut le rang le plus haut employé.
        var combien = schema?.Sorties.Count
            ?? (partants.Count == 0 ? 0 : partants.Keys.Max() + 1);

        var sorties = new JsonArray();

        for (var rang = 0; rang < combien; rang++)
        {
            var type = schema is not null && rang < schema.Sorties.Count ? schema.Sorties[rang] : "*";

            sorties.Add(new JsonObject
            {
                ["name"] = type,
                ["type"] = type,
                ["links"] = partants.TryGetValue(rang, out var vers) ? vers : new JsonArray(),
            });
        }

        return sorties;
    }

    /// <summary>
    /// Les valeurs écrites du nœud, dans l'ordre positionnel qu'attend l'interface.
    /// </summary>
    /// <remarks>
    /// <b>Cet ordre n'est pas décoratif.</b> L'interface n'associe pas les valeurs à des noms : elle
    /// les distribue dans l'ordre où le nœud déclare ses réglages. Une valeur manquante ne laisse
    /// pas un trou, elle décale toutes les suivantes — la graine devient le nombre d'étapes, et le
    /// graphe s'ouvre faux sans rien signaler. Quand le catalogue est là, on suit donc sa
    /// déclaration et l'on comble les absents ; sans lui, on garde l'ordre du fichier.
    ///
    /// <para>
    /// Le cas de la graine est à part : l'interface ajoute d'elle-même, juste après tout réglage
    /// nommé <c>seed</c> ou <c>noise_seed</c>, une case « après génération » qui n'existe nulle part
    /// dans la déclaration du nœud. L'oublier décale tout ce qui suit la graine.
    /// </para>
    /// </remarks>
    private static JsonArray Reglages(JsonObject entrees, NoeudSchema? schema)
    {
        var valeurs = new JsonArray();

        foreach (var nom in Ordre(entrees, schema))
        {
            var ecrit = entrees[nom];

            valeurs.Add(ecrit is null ? Defaut(schema?.Entree(nom)) : ecrit.DeepClone());

            if (nom is "seed" or "noise_seed")
            {
                valeurs.Add("fixed");
            }
        }

        return valeurs;
    }

    /// <summary>Les noms des réglages, dans l'ordre où le nœud les déclare.</summary>
    private static IEnumerable<string> Ordre(JsonObject entrees, NoeudSchema? schema)
    {
        if (schema is null)
        {
            return entrees
                .Where(p => Cable(p.Value) is null)
                .Select(p => p.Key);
        }

        return schema.Requises.Concat(schema.Optionnelles)
            .Where(p => !p.Value.Cable)
            .Select(p => p.Key);
    }

    /// <summary>De quoi remplir une case que le graphe exécuté ne mentionne pas.</summary>
    private static JsonNode? Defaut(EntreeSchema? entree)
        => entree switch
        {
            null => null,
            { Valeurs.Count: > 0 } choix => JsonValue.Create(choix.Valeurs[0]),
            { Type: "INT" or "FLOAT" } nombre => JsonValue.Create(nombre.Min ?? 0),
            { Type: "BOOLEAN" } => JsonValue.Create(false),
            _ => JsonValue.Create(""),
        };

    /// <summary>
    /// Où poser chaque nœud, pour que le graphe s'ouvre lisible plutôt qu'en tas.
    /// </summary>
    /// <remarks>
    /// La forme API ne porte aucune coordonnée : elle n'en a pas besoin. Il faut donc en inventer,
    /// et la seule disposition qui ait un sens est celle du flux lui-même — la profondeur dans le
    /// graphe donne la colonne, de sorte que ce qui charge est à gauche et ce qui enregistre à
    /// droite.
    /// </remarks>
    private static Dictionary<string, (int X, int Y)> Colonnes(JsonObject source)
    {
        var profondeurs = new Dictionary<string, int>();

        foreach (var (cle, _) in source)
        {
            Profondeur(cle, source, profondeurs, []);
        }

        var places = new Dictionary<string, (int, int)>();
        var occupees = new Dictionary<int, int>();

        foreach (var (cle, _) in source)
        {
            var colonne = profondeurs[cle];
            var ligne = occupees.TryGetValue(colonne, out var deja) ? deja : 0;

            occupees[colonne] = ligne + 1;
            places[cle] = (colonne * 400, ligne * 220);
        }

        return places;
    }

    /// <summary>À quelle distance ce nœud est-il du premier chargement ?</summary>
    /// <remarks>
    /// Le passage par <paramref name="enCours"/> n'est pas une précaution de style : un graphe
    /// bouclé ferait descendre la récursion sans fin, et un graphe peut être bouclé simplement
    /// parce qu'il a été écrit à la main.
    /// </remarks>
    private static int Profondeur(
        string cle,
        JsonObject source,
        Dictionary<string, int> connues,
        HashSet<string> enCours)
    {
        if (connues.TryGetValue(cle, out var deja))
        {
            return deja;
        }

        if (!enCours.Add(cle))
        {
            return 0;
        }

        var loin = 0;

        foreach (var (_, contenu) in source[cle]?["inputs"]?.AsObject() ?? [])
        {
            if (Cable(contenu) is { } cable && source.ContainsKey(cable.Noeud))
            {
                loin = Math.Max(loin, Profondeur(cable.Noeud, source, connues, enCours) + 1);
            }
        }

        enCours.Remove(cle);

        return connues[cle] = loin;
    }

    /// <summary>L'identifiant d'un nœud tel que l'interface le veut : un nombre.</summary>
    private static int Numero(string cle)
        => int.TryParse(cle, out var nombre) ? nombre : Math.Abs(cle.GetHashCode()) % 100000;
}
