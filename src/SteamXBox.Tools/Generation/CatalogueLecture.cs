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
