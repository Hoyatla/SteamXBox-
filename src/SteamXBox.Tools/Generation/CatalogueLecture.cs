using System.Globalization;
using System.Text;

namespace SteamXBox.Tools.Generation;

/// <summary>
/// Montre au modèle ce que le générateur sait faire, par petits morceaux.
/// </summary>
/// <remarks>
/// <b>Le catalogue ne tient pas dans un contexte, et c'est mesuré</b> : 1084 classes de nœuds pour
/// 1,4 Mo de schéma, de l'ordre de trois cent cinquante mille jetons, quand le modèle en lit huit
/// mille — conversation, déclaration des outils et raisonnement compris. Le montrer en bloc n'est
/// pas coûteux, c'est impossible. Tout ce qui s'appuie sur ce catalogue doit donc le réduire, et
/// c'est ici que la réduction se fait.
///
/// <para>
/// <b>Deux mouvements plutôt qu'un.</b> Chercher rend une liste courte : le nom, la catégorie, une
/// phrase — de quoi choisir, pas de quoi écrire. Le détail d'un nœud ne vient qu'ensuite, et
/// seulement pour ceux qu'on a retenus. C'est la façon dont un humain lit une documentation, et
/// elle tient dans le contexte quand tout déballer d'un coup ne tiendrait jamais.
/// </para>
/// </remarks>
public static class CatalogueLecture
{
    /// <summary>Combien de nœuds une recherche rend au plus.</summary>
    /// <remarks>
    /// Douze lignes de trois cents caractères font environ mille jetons : lisible sans manger la
    /// conversation. Au-delà, le modèle ne choisit plus, il oublie la question.
    /// </remarks>
    public const int Trouves = 12;

    /// <summary>Combien de valeurs admises sont montrées avant de compter le reste.</summary>
    private const int Valeurs = 20;

    /// <summary>Ce qui fait d'une valeur admise un fichier de modèle plutôt qu'un mot.</summary>
    /// <remarks>
    /// Par l'extension, et non par le nom de l'entrée. <c>ckpt_name</c>, <c>unet_name</c>,
    /// <c>clip_name</c>, <c>vae_name</c>, <c>lora_name</c>, <c>model_name</c> — la liste des noms
    /// d'entrées n'a pas de fin, chaque module tiers inventant les siens, et une liste tenue à la
    /// main aurait vieilli au premier ajouté. Une extension de poids de modèle, elle, se reconnaît
    /// partout. <c>.yaml</c> est ainsi écarté sans avoir à le nommer : le <c>config_name</c> de
    /// <c>CheckpointLoader</c> n'est pas un modèle.
    /// </remarks>
    private static readonly string[] Poids =
        [".safetensors", ".gguf", ".ckpt", ".pt", ".pth", ".bin", ".sft", ".onnx"];

    /// <summary>Combien de fichiers d'un même groupe sont nommés avant de compter le reste.</summary>
    private const int Nommes = 12;

    /// <summary>
    /// Les modèles réellement installés, et le nœud qui charge chacun.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Un modèle de langage cherche « load checkpoint », trouve
    /// <c>CheckpointLoader</c>, lit sa liste, et conclut que la machine ne porte qu'un modèle.
    /// Mesuré le 24 août : cette machine en porte cinq, mais un seul est dans
    /// <c>models/checkpoints</c>. MiniMax H3 est dans <c>diffusion_models</c> et se charge par
    /// <c>UNETLoader</c> ; Flux est dans <c>unet</c> et se charge par <c>UnetLoaderGGUF</c>. Trois
    /// dossiers, trois nœuds différents, et rien qui les relie pour qui ne le sait pas déjà.
    ///
    /// <para>
    /// L'assistant a donc tourné une demi-heure sur <c>svd_xt</c>, seul modèle qu'il pouvait voir,
    /// pendant que l'utilisateur lui proposait MiniMax et Wan. Ce n'était pas une faiblesse du
    /// modèle : la question qu'il posait n'avait pas de bonne réponse.
    /// </para>
    ///
    /// <para>
    /// <b>Groupé par liste de fichiers, et non par nœud.</b> Une dizaine de nœuds offrent la même
    /// liste de VAE — celui du produit, celui d'un module, celui d'un autre. Les énumérer tous
    /// répéterait dix fois les mêmes fichiers pour ne rien apprendre ; les fichiers sont donc dits
    /// une fois, et les nœuds qui les chargent nommés à côté.
    /// </para>
    /// </remarks>
    public static string Modeles(Catalogue catalogue)
    {
        // La liste de fichiers sert de clé : deux nœuds qui offrent exactement les mêmes fichiers
        // parlent du même dossier, quel que soit le nom qu'ils portent.
        var fichiers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var chargeurs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var rendus = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var nom in catalogue.Noms.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (catalogue.Noeud(nom) is not { } schema)
            {
                continue;
            }

            foreach (var entree in schema.Requises.Concat(schema.Optionnelles))
            {
                var poids = entree.Value.Valeurs
                    .Where(v => Poids.Any(e => v.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (poids.Count == 0)
                {
                    continue;
                }

                // Séparées par un caractère nul, qu'aucun nom de fichier ne peut porter. Joindre
                // par une virgule confondrait deux listes différentes dont l'assemblage se lit
                // pareil — « a, b » et « a » plus « , b ».
                var cle = string.Join(char.MinValue, poids);

                if (!fichiers.ContainsKey(cle))
                {
                    fichiers[cle] = poids;
                    chargeurs[cle] = [];
                    rendus[cle] = string.Join(", ", schema.Sorties);
                }

                chargeurs[cle].Add($"{nom}.{entree.Key}");
            }
        }

        if (fichiers.Count == 0)
        {
            return "Aucun modèle installé : le générateur n'a aucun poids à charger. "
                + "Ouvre les jeux de modèles pour en installer.";
        }

        var texte = new StringBuilder()
            .AppendLine("MODÈLES INSTALLÉS SUR CETTE MACHINE, et le nœud qui charge chacun.")
            .AppendLine(
                "Ce qui n'est pas dans cette liste n'existe pas ici. Les modèles vivent dans "
                + "plusieurs dossiers et ne se chargent PAS tous par le même nœud : chercher un "
                + "seul chargeur ne montre qu'une partie de ce que la machine porte.")
            .AppendLine();

        // Le modèle d'abord, l'encodeur ensuite, et le reste après.
        //
        // <b>Le défaut que cet ordre corrige.</b> Le tri était alphabétique sur le nom du chargeur,
        // ce qui met « CLIPLoader » avant « CheckpointLoader » et « UnetLoaderGGUF » bon dernier :
        // la liste s'ouvrait sur les encodeurs de texte et enterrait les modèles de diffusion.
        // Mesuré le 24 août — l'assistant a lu cette liste, n'y a pas vu de modèle, est reparti
        // chercher « load checkpoint » de son côté, n'a retrouvé que svd_xt, et a composé un graphe
        // SVD qui a réclamé trente-six gigaoctets. Wan était installé et invisible.
        //
        // « Quel modèle vais-je employer » est la première question ; ce qui rend du MODEL y répond,
        // le reste est de la plomberie qu'on branche ensuite.
        static int Rang(string rendus) => rendus.StartsWith("MODEL", StringComparison.Ordinal) ? 0
            : rendus.StartsWith("CLIP", StringComparison.Ordinal) ? 1
            : rendus.StartsWith("VAE", StringComparison.Ordinal) ? 2
            : 3;

        foreach (var cle in fichiers.Keys
                     .OrderBy(c => Rang(rendus[c]))
                     .ThenBy(c => chargeurs[c][0], StringComparer.Ordinal))
        {
            var quels = chargeurs[cle];
            var rendu = rendus[cle].Length > 0 ? $" → {rendus[cle]}" : "";

            texte.Append("· ").Append(quels[0]).Append(rendu);

            if (quels.Count > 1)
            {
                texte.Append("   (aussi : ")
                    .Append(string.Join(", ", quels.Skip(1).Take(2)))
                    .Append(quels.Count > 3 ? $", et {quels.Count - 3} autre(s)" : "")
                    .Append(')');
            }

            texte.AppendLine();

            foreach (var fichier in fichiers[cle].Take(Nommes))
            {
                texte.Append("    ").AppendLine(fichier);
            }

            if (fichiers[cle].Count > Nommes)
            {
                texte.Append("    … et ")
                    .Append((fichiers[cle].Count - Nommes).ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" autre(s).");
            }
        }

        return texte.ToString();
    }

    /// <summary>Cherche les nœuds qui répondent à un besoin, du plus proche au plus lointain.</summary>
    /// <remarks>
    /// La recherche porte sur le nom, les alias déclarés par l'auteur, la catégorie et la
    /// description, dans cet ordre d'importance. Les alias comptent double parce qu'ils existent
    /// précisément pour cela : <c>KSampler</c> ne contient ni « generate » ni « txt2img », et c'est
    /// pourtant ce qu'on cherche quand on veut fabriquer une image.
    /// </remarks>
    public static IReadOnlyList<NoeudSchema> Chercher(Catalogue catalogue, string besoin, int combien)
    {
        var mots = Mots(besoin);

        if (mots.Count == 0)
        {
            return [];
        }

        var notes = new List<(NoeudSchema Noeud, int Note)>();

        foreach (var nom in catalogue.Noms)
        {
            if (catalogue.Noeud(nom) is not { } schema)
            {
                continue;
            }

            var note = Note(schema, mots);

            if (note > 0)
            {
                notes.Add((schema, note));
            }
        }

        return notes
            .OrderByDescending(n => n.Note)
            .ThenBy(n => n.Noeud.Nom.Length)
            .ThenBy(n => n.Noeud.Nom, StringComparer.Ordinal)
            .Take(combien)
            .Select(n => n.Noeud)
            .ToList();
    }

    private static int Note(NoeudSchema schema, IReadOnlyList<string> mots)
    {
        var nom = schema.Nom.ToLowerInvariant();
        var alias = string.Join(" ", schema.Alias).ToLowerInvariant();
        var reste = (schema.Categorie + " " + schema.Description).ToLowerInvariant();
        var note = 0;

        foreach (var mot in mots)
        {
            if (nom.Contains(mot, StringComparison.Ordinal))
            {
                note += 4;
            }

            if (alias.Contains(mot, StringComparison.Ordinal))
            {
                note += 4;
            }

            if (reste.Contains(mot, StringComparison.Ordinal))
            {
                note += 1;
            }
        }

        return note;
    }

    /// <summary>
    /// Les mots utiles d'une demande.
    /// </summary>
    /// <remarks>
    /// Les mots d'une ou deux lettres sont écartés : « de », « la », « to », « of » sont dans la
    /// description de presque tout et donneraient la même note à mille nœuds.
    /// </remarks>
    private static IReadOnlyList<string> Mots(string besoin)
        => (besoin ?? "")
            .ToLowerInvariant()
            .Split([' ', ',', ';', '.', '\'', '"', '(', ')', '\n', '\r', '\t', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(m => m.Length > 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Une ligne par nœud : de quoi choisir, pas de quoi écrire.</summary>
    public static string Resumer(IReadOnlyList<NoeudSchema> noeuds, string besoin)
    {
        if (noeuds.Count == 0)
        {
            return $"Aucun nœud ne répond à « {besoin} ». Essaie d'autres mots, en anglais de "
                + "préférence : les nœuds sont nommés et décrits en anglais.";
        }

        var texte = new StringBuilder()
            .Append(noeuds.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" nœuds pour « ").Append(besoin).AppendLine(" ». ")
            .AppendLine("Demande le détail d'un nœud avant de t'en servir : les noms d'entrées ne "
                + "se devinent pas.")
            .AppendLine();

        foreach (var noeud in noeuds)
        {
            texte.Append("- ").Append(noeud.Nom);

            if (noeud.Sorties.Count > 0)
            {
                texte.Append(" → ").Append(string.Join(", ", noeud.Sorties));
            }

            if (noeud.EstSortie)
            {
                texte.Append(" [enregistre]");
            }

            texte.AppendLine();

            if (noeud.Description.Length > 0)
            {
                texte.Append("  ").AppendLine(Court(noeud.Description, 160));
            }
        }

        return texte.ToString();
    }

    /// <summary>Tout ce qu'il faut savoir d'un nœud pour l'écrire dans un flux.</summary>
    /// <remarks>
    /// Les valeurs admises sont montrées, et c'est le point : la liste des modèles installés sort
    /// d'ici, et de nulle part ailleurs. Un modèle de langage qui écrit un nom de fichier de
    /// mémoire écrit celui d'une autre machine.
    /// </remarks>
    public static string Detailler(NoeudSchema noeud)
    {
        var texte = new StringBuilder()
            .Append(noeud.Nom);

        if (noeud.Categorie.Length > 0)
        {
            texte.Append("  (").Append(noeud.Categorie).Append(')');
        }

        texte.AppendLine();

        if (noeud.Description.Length > 0)
        {
            texte.AppendLine(Court(noeud.Description, 300));
        }

        texte.AppendLine();
        texte.AppendLine("Entrées obligatoires :");

        if (noeud.Requises.Count == 0)
        {
            texte.AppendLine("  aucune");
        }

        foreach (var (nom, schema) in noeud.Requises)
        {
            texte.AppendLine(Entree(nom, schema));
        }

        if (noeud.Optionnelles.Count > 0)
        {
            texte.AppendLine("Entrées facultatives :");

            foreach (var (nom, schema) in noeud.Optionnelles)
            {
                texte.AppendLine(Entree(nom, schema));
            }
        }

        texte.Append("Sorties : ")
            .AppendLine(noeud.Sorties.Count > 0
                ? string.Join(", ", noeud.Sorties.Select((t, i) =>
                    $"rang {i.ToString(CultureInfo.InvariantCulture)} = {t}"))
                : "aucune");

        if (noeud.EstSortie)
        {
            texte.AppendLine("Ce nœud enregistre le résultat : un flux en a besoin d'au moins un.");
        }

        return texte.ToString();
    }

    private static string Entree(string nom, EntreeSchema schema)
    {
        var ligne = new StringBuilder("  ").Append(nom).Append(" : ");

        if (schema.Cable)
        {
            return ligne.Append("câble de type ").Append(schema.Type)
                .Append(" — s'écrit [\"numéro du nœud\", rang de sortie]").ToString();
        }

        if (schema.Valeurs.Count > 0)
        {
            ligne.Append("un de ").Append(Choix(schema.Valeurs));

            return ligne.ToString();
        }

        ligne.Append(schema.Type);

        if (schema.Min is { } bas)
        {
            ligne.Append(", de ").Append(Ecrire(bas));
        }

        if (schema.Max is { } haut)
        {
            ligne.Append(schema.Min is null ? ", jusqu'à " : " à ").Append(Ecrire(haut));
        }

        return ligne.ToString();
    }

    private static string Choix(IReadOnlyList<string> valeurs)
    {
        var debut = string.Join(", ", valeurs.Take(Valeurs));

        return valeurs.Count > Valeurs
            ? debut + $", … ({valeurs.Count.ToString(CultureInfo.InvariantCulture)} en tout)"
            : debut;
    }

    private static string Ecrire(double nombre)
        => nombre == Math.Floor(nombre) && Math.Abs(nombre) < 1e15
            ? ((long)nombre).ToString(CultureInfo.InvariantCulture)
            : nombre.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Court(string texte, int combien)
    {
        var propre = texte.ReplaceLineEndings(" ").Trim();

        return propre.Length > combien ? string.Concat(propre.AsSpan(0, combien), "…") : propre;
    }
}
