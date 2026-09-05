namespace SenSÉ.Plugins;

/// <summary>Une brique du vocabulaire : ce qu'un manifeste peut déclarer dans son contenu.</summary>
/// <param name="Kind">Le mot écrit dans le manifeste, tel quel.</param>
/// <param name="Label">Le nom montré à l'utilisateur dans la palette de l'éditeur.</param>
/// <param name="Purpose">À quoi elle sert, en une phrase. Lue par l'éditeur et par le modèle.</param>
/// <param name="Fields">Les champs que cette brique attend, pour l'éditeur et pour la validation.</param>
public sealed record PluginBrick(string Kind, string Label, string Purpose, IReadOnlyList<string> Fields);

/// <summary>
/// Le vocabulaire des manifestes, décrit une fois.
/// </summary>
/// <remarks>
/// <b>Décrit une fois, lu trois fois</b> — c'est la règle du contrat MCP, et elle vaut d'être tenue
/// dès ici : le chargeur lit ce vocabulaire pour dessiner, l'éditeur d'outils pour proposer sa
/// palette, le serveur MCP pour l'expliquer au modèle local.
///
/// <para>
/// Codé en dur dans le chargeur, il faudrait le réécrire dans l'éditeur, puis une troisième fois
/// pour le modèle — et trois listes divergent toujours. Décrit ici, une brique ajoutée apparaît
/// d'elle-même dans la palette et dans ce que le modèle sait faire, sans qu'une ligne soit écrite
/// ailleurs. C'est la même promesse que « un outil est un dossier », appliquée au vocabulaire.
/// </para>
///
/// <para>
/// Des données, pas du code : une brique ne sait ni se dessiner ni s'exécuter. Le rendu appartient à
/// l'hôte — l'outil décrit, l'hôte dessine — et c'est ce qui fait qu'un outil écrit par un
/// utilisateur est navigable à la manette sans que son auteur y ait pensé.
/// </para>
/// </remarks>
public static class PluginVocabulary
{
    /// <summary>Les briques connues, dans l'ordre où une palette les proposerait.</summary>
    public static IReadOnlyList<PluginBrick> Bricks { get; } =
    [
        new(
            "file",
            "Fichier",
            "Demande un fichier à l'utilisateur, restreint aux extensions listées.",
            ["id", "label", "options"]),

        new(
            "choice",
            "Choix",
            "Propose une liste de valeurs, dont une par défaut.",
            ["id", "label", "value", "options"]),

        new(
            "number",
            "Nombre",
            "Un curseur entre deux bornes, par pas de un.",
            ["id", "label", "value", "min", "max"]),

        new(
            "text",
            "Texte",
            "Une ligne saisie librement.",
            ["id", "label", "value"]),

        new(
            "action",
            "Action",
            "Le bouton qui lance le travail, avec ce qu'il fait et sur quoi.",
            ["label", "does", "target"]),
    ];

    /// <summary>Cette brique fait-elle partie du vocabulaire ?</summary>
    /// <remarks>
    /// Un manifeste qui déclare un mot inconnu est refusé plutôt qu'affiché à moitié : une tuile où
    /// un champ manque sans que rien ne le dise est plus coûteuse à diagnostiquer qu'un outil qui ne
    /// se charge pas et qui écrit pourquoi.
    /// </remarks>
    public static bool Knows(string kind)
        => Bricks.Any(b => b.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>La brique portant ce mot, ou null.</summary>
    public static PluginBrick? Find(string kind)
        => Bricks.FirstOrDefault(b => b.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Le vocabulaire en clair, pour la palette de l'éditeur et pour le modèle.
    /// </summary>
    /// <remarks>
    /// Une seule mise en forme, partagée : ce que l'utilisateur lit dans l'éditeur et ce que le
    /// modèle reçoit décrivent la même chose avec les mêmes mots. Le modèle n'a pas de vocabulaire
    /// privé — c'est écrit dans <c>docs/contrat-mcp.md</c>, et c'est ici que ça devient vrai.
    /// </remarks>
    public static string Describe()
        => string.Join(
            '\n',
            Bricks.Select(b => $"- {b.Kind} ({b.Label}) : {b.Purpose} Champs : {string.Join(", ", b.Fields)}."));
}
