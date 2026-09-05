using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Generation;

/// <summary>
/// L'arbitre : il dit à quoi un flux ne tient pas, avant que le générateur ne s'y attelle.
/// </summary>
/// <remarks>
/// <b>C'est ce qui rend la composition raisonnable.</b> Sans arbitre, demander un graphe à un modèle
/// de langage est un pari : il produit du JSON vraisemblable, on l'envoie, et l'on apprend au bout
/// de plusieurs minutes de chargement que le modèle nommé n'existe pas sur ce disque. Avec arbitre,
/// c'est une boucle — il propose, l'hôte refuse en nommant la faute, il corrige. La différence
/// tient à ce que l'arbitre, lui, ne se trompe pas : il ne raisonne pas, il compare au schéma que
/// le générateur déclare.
///
/// <para>
/// <b>Rien n'est réparé, tout est nommé.</b> Corriger en silence produirait une image qui n'est pas
/// celle demandée, sans que personne sache où le sens a glissé — et priverait le modèle de ce qui
/// lui permet d'apprendre son erreur en un tour.
/// </para>
///
/// <para>
/// <b>Les fautes sont rendues toutes ensemble</b>, pas la première seule. Un tour de modèle coûte
/// une trentaine de secondes sur le processeur ; les livrer une par une ferait cinq tours là où un
/// seul suffit.
/// </para>
/// </remarks>
public static class FluxVerificateur
{
    /// <summary>Au-delà, la liste cesse d'aider et remplit le contexte.</summary>
    /// <remarks>
    /// Un flux entièrement faux produirait une faute par entrée de chaque nœud. Le modèle a huit
    /// mille jetons en tout : cent reproches noieraient la conversation et la question posée.
    /// Douze suffisent à repartir, et le compte des autres est annoncé plutôt que tu.
    /// </remarks>
    public const int FautesMontrees = 12;

    /// <summary>Ce que le générateur accepte comme n'importe quel type.</summary>
    private const string Joker = "*";

    /// <summary>Vérifie un flux au format API contre ce que le générateur sait faire.</summary>
    /// <returns>Les fautes trouvées, vide si le flux est soumettable.</returns>
    public static IReadOnlyList<string> Verifier(string flux, Catalogue catalogue)
    {
        var fautes = new List<string>();

        JsonNode? racine;

        try
        {
            racine = JsonNode.Parse(flux);
        }
        catch (JsonException exception)
        {
            return [$"Le flux n'est pas un JSON valide : {exception.Message}"];
        }

        if (racine is not JsonObject graphe || graphe.Count == 0)
        {
            return ["Le flux doit être un objet de nœuds numérotés, et il en faut au moins un."];
        }

        if (graphe.ContainsKey("nodes") && graphe.ContainsKey("links"))
        {
            return [
                "Ce flux est au format de l'éditeur. Le format attendu est celui de l'API : des "
                + "nœuds numérotés portant chacun un class_type et ses inputs.",
            ];
        }

        var schemas = new Dictionary<string, NoeudSchema>(StringComparer.Ordinal);
        var enregistre = false;

        // Premier passage : les nœuds eux-mêmes. Il faut savoir qui existe avant de pouvoir juger
        // un câble, dont les deux bouts sont des nœuds.
        foreach (var (numero, corps) in graphe)
        {
            if (corps is not JsonObject noeud)
            {
                fautes.Add($"Le nœud {numero} n'est pas un objet.");

                continue;
            }

            var classe = (noeud["class_type"] as JsonValue)?.ToString() ?? "";

            if (classe.Length == 0)
            {
                fautes.Add($"Le nœud {numero} n'a pas de class_type.");

                continue;
            }

            if (catalogue.Noeud(classe) is not { } schema)
            {
                fautes.Add(catalogue.Voisin(classe) is { } voisin
                    ? $"Le nœud {numero} nomme « {classe} », qui n'existe pas ici. "
                      + $"Le nom installé est « {voisin} »."
                    : $"Le nœud {numero} nomme « {classe} », qui n'est pas installé sur cette "
                      + "machine. Cherche dans le catalogue du générateur.");

                continue;
            }

            schemas[numero] = schema;
            enregistre |= schema.EstSortie;
        }

        // Second passage : les entrées, maintenant que l'on sait ce que chaque numéro désigne.
        foreach (var (numero, corps) in graphe)
        {
            if (corps is not JsonObject noeud || !schemas.TryGetValue(numero, out var schema))
            {
                continue;
            }

            var entrees = noeud["inputs"] as JsonObject ?? [];

            Manquantes(numero, schema, entrees, fautes);

            foreach (var (nom, valeur) in entrees)
            {
                Entree(numero, schema, nom, valeur, graphe, schemas, fautes);
            }
        }

        // Un flux sans nœud d'enregistrement tourne, consomme la carte, et ne laisse rien. La panne
        // la plus déroutante qui soit : tout s'est bien passé, et il n'y a pas de fichier.
        if (!enregistre && fautes.Count == 0)
        {
            fautes.Add(
                "Aucun nœud n'enregistre le résultat : ce flux tournerait sans rien produire. "
                + "Il en faut un, par exemple SaveImage ou SaveVideo.");
        }

        if (Boucle(graphe, schemas) is { } cycle)
        {
            fautes.Add($"Les nœuds {cycle} se référencent en rond : le graphe ne peut pas s'exécuter.");
        }

        return fautes;
    }

    /// <summary>Une phrase à rendre au modèle, ou vide si le flux passe.</summary>
    /// <remarks>
    /// Le verdict est explicite dans les deux sens. Une liste vide rendue telle quelle laisserait le
    /// modèle deviner si le flux a été jugé bon ou si la vérification n'a pas eu lieu.
    /// </remarks>
    public static string Verdict(IReadOnlyList<string> fautes)
    {
        if (fautes.Count == 0)
        {
            return "Le flux est valide : tous les nœuds existent, les entrées obligatoires sont "
                + "là, les câbles s'accordent, et le résultat sera enregistré.";
        }

        var montrees = fautes.Take(FautesMontrees).ToList();

        var texte = $"Le flux n'est pas soumettable, {Compte(fautes.Count)} :\n"
            + string.Join("\n", montrees.Select(f => "- " + f));

        return fautes.Count > montrees.Count
            ? texte + $"\n… et {Compte(fautes.Count - montrees.Count)} de plus, non détaillées."
            : texte;
    }

    private static string Compte(int combien)
        => combien == 1
            ? "1 faute"
            : combien.ToString(CultureInfo.InvariantCulture) + " fautes";

    /// <summary>Les entrées obligatoires que le flux n'a pas données.</summary>
    private static void Manquantes(
        string numero, NoeudSchema schema, JsonObject entrees, List<string> fautes)
    {
        foreach (var (nom, attendue) in schema.Requises)
        {
            if (entrees.ContainsKey(nom))
            {
                continue;
            }

            fautes.Add(attendue.Cable
                ? $"Il manque l'entrée « {nom} » du nœud {numero} ({schema.Nom}) : "
                  + $"elle attend un câble de type {attendue.Type}."
                : $"Il manque l'entrée « {nom} » du nœud {numero} ({schema.Nom}).");
        }
    }

    /// <summary>Une entrée fournie : câble ou valeur, et bonne dans son genre ?</summary>
    private static void Entree(
        string numero,
        NoeudSchema schema,
        string nom,
        JsonNode? valeur,
        JsonObject graphe,
        IReadOnlyDictionary<string, NoeudSchema> schemas,
        List<string> fautes)
    {
        if (schema.Entree(nom) is not { } attendue)
        {
            fautes.Add($"Le nœud {numero} ({schema.Nom}) n'a pas d'entrée « {nom} ».");

            return;
        }

        var ou = $"{nom} du nœud {numero} ({schema.Nom})";

        if (valeur is JsonArray cable)
        {
            Cable(ou, attendue, cable, graphe, schemas, fautes);

            return;
        }

        if (attendue.Cable)
        {
            fautes.Add($"« {ou} » attend un câble de type {attendue.Type}, "
                + "pas une valeur écrite. Un câble s'écrit [\"numéro du nœud\", rang de sortie].");

            return;
        }

        Valeur(ou, attendue, valeur, fautes);
    }

    /// <summary>Un câble part-il d'un nœud qui existe, d'un rang qui existe, du bon type ?</summary>
    private static void Cable(
        string ou,
        EntreeSchema attendue,
        JsonArray cable,
        JsonObject graphe,
        IReadOnlyDictionary<string, NoeudSchema> schemas,
        List<string> fautes)
    {
        if (cable.Count != 2)
        {
            fautes.Add($"Le câble de « {ou} » doit s'écrire [\"numéro du nœud\", rang de sortie].");

            return;
        }

        var source = (cable[0] as JsonValue)?.ToString() ?? "";

        if (!graphe.ContainsKey(source))
        {
            fautes.Add($"« {ou} » est câblé au nœud {source}, qui n'existe pas dans ce flux.");

            return;
        }

        // Le nœud source existe mais son class_type était déjà fautif : inutile d'accuser le câble
        // par-dessus, la vraie faute est déjà dans la liste.
        if (!schemas.TryGetValue(source, out var amont))
        {
            return;
        }

        var rang = cable[1] is JsonValue nombre && nombre.TryGetValue<int>(out var lu) ? lu : -1;

        if (rang < 0 || rang >= amont.Sorties.Count)
        {
            fautes.Add($"« {ou} » demande la sortie {rang.ToString(CultureInfo.InvariantCulture)} "
                + $"du nœud {source} ({amont.Nom}), qui en a "
                + $"{amont.Sorties.Count.ToString(CultureInfo.InvariantCulture)}.");

            return;
        }

        var donne = amont.Sorties[rang];

        if (donne == attendue.Type || donne == Joker || attendue.Type == Joker)
        {
            return;
        }

        fautes.Add($"« {ou} » attend du {attendue.Type}, et le nœud {source} ({amont.Nom}) "
            + $"rend du {donne} sur cette sortie.");
    }

    /// <summary>Une valeur écrite tient-elle dans ce que l'entrée accepte ?</summary>
    private static void Valeur(string ou, EntreeSchema attendue, JsonNode? valeur, List<string> fautes)
    {
        var ecrit = (valeur as JsonValue)?.ToString() ?? "";

        if (attendue.Valeurs.Count > 0)
        {
            if (attendue.Valeurs.Contains(ecrit, StringComparer.Ordinal))
            {
                return;
            }

            // Le cas qui compte : un modèle qui n'est pas sur ce disque. Les valeurs admises sont la
            // liste réelle des fichiers installés — les montrer répond à la faute et à la question
            // suivante d'un seul coup.
            fautes.Add($"« {ou} » ne peut pas valoir « {ecrit} ». "
                + $"Valeurs possibles ici : {Choix(attendue.Valeurs)}");

            return;
        }

        switch (attendue.Type)
        {
            case "INT":
            case "FLOAT":
                if (!double.TryParse(
                        ecrit, NumberStyles.Float, CultureInfo.InvariantCulture, out var nombre))
                {
                    fautes.Add($"« {ou} » attend un nombre, et « {ecrit} » n'en est pas un.");

                    return;
                }

                if (attendue.Type == "INT" && ecrit.Contains('.', StringComparison.Ordinal))
                {
                    fautes.Add($"« {ou} » attend un entier, et « {ecrit} » est un décimal.");

                    return;
                }

                Bornes(ou, attendue, nombre, fautes);

                return;

            case "BOOLEAN":
                if (valeur?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    fautes.Add($"« {ou} » attend true ou false, et non « {ecrit} ».");
                }

                return;

            default:
                return;
        }
    }

    private static void Bornes(string ou, EntreeSchema attendue, double nombre, List<string> fautes)
    {
        if (attendue.Min is { } bas && nombre < bas)
        {
            fautes.Add($"« {ou} » ne descend pas sous {Ecrire(bas)}.");
        }

        if (attendue.Max is { } haut && nombre > haut)
        {
            fautes.Add($"« {ou} » ne monte pas au-dessus de {Ecrire(haut)}.");
        }
    }

    private static string Ecrire(double nombre)
        => nombre == Math.Floor(nombre) && Math.Abs(nombre) < 1e15
            ? ((long)nombre).ToString(CultureInfo.InvariantCulture)
            : nombre.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Les valeurs admises, écourtées quand la liste est longue.</summary>
    /// <remarks>
    /// Certaines listes comptent des dizaines d'entrées — les quarante-quatre échantillonneurs
    /// installés ici, par exemple. Les déverser toutes à chaque faute épuiserait le contexte du
    /// modèle en trois reproches.
    /// </remarks>
    private static string Choix(IReadOnlyList<string> valeurs)
    {
        const int Montrees = 12;

        var debut = string.Join(", ", valeurs.Take(Montrees));

        return valeurs.Count > Montrees
            ? debut + $", … ({valeurs.Count.ToString(CultureInfo.InvariantCulture)} en tout)"
            : debut;
    }

    /// <summary>Le premier cycle trouvé, nommé, ou null.</summary>
    /// <remarks>
    /// Un graphe qui se mord la queue n'échoue pas franchement : le générateur tourne en rond ou
    /// s'arrête sur un message qui parle d'un type absent, très loin de la vraie cause.
    /// </remarks>
    private static string? Boucle(JsonObject graphe, IReadOnlyDictionary<string, NoeudSchema> schemas)
    {
        var visite = new Dictionary<string, int>(StringComparer.Ordinal);
        var chemin = new List<string>();

        foreach (var numero in graphe.Select(p => p.Key))
        {
            if (Descendre(numero) is { } cycle)
            {
                return cycle;
            }
        }

        return null;

        string? Descendre(string numero)
        {
            if (visite.TryGetValue(numero, out var etat))
            {
                // 1 : en cours de descente, donc rencontré une seconde fois sur le même chemin.
                return etat == 1 ? string.Join(" → ", chemin.SkipWhile(n => n != numero)) + $" → {numero}" : null;
            }

            if (!schemas.ContainsKey(numero))
            {
                return null;
            }

            visite[numero] = 1;
            chemin.Add(numero);

            if (graphe[numero] is JsonObject noeud && noeud["inputs"] is JsonObject entrees)
            {
                foreach (var (_, valeur) in entrees)
                {
                    if (valeur is not JsonArray cable || cable.Count != 2)
                    {
                        continue;
                    }

                    var source = (cable[0] as JsonValue)?.ToString() ?? "";

                    if (source.Length > 0 && Descendre(source) is { } trouve)
                    {
                        return trouve;
                    }
                }
            }

            chemin.RemoveAt(chemin.Count - 1);
            visite[numero] = 2;

            return null;
        }
    }
}
