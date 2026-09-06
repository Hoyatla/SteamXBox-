namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Les Capacite de memoire que l'Assistant peut appeler pendant la
/// conversation : noter, lire, lister, oublier.
/// </summary>
/// <remarks>
/// <b>Quatre capacites, et pas une seule de plus.</b> Le modele peut
/// noter une information nouvelle, la relire, voir ce qu'il a note, et
/// oublier une note obsolète. C'est le strict minimum : ajouter d'autres
/// capacites (recherche par tag, par date, par similarite) supposerait
/// un moteur de recherche, et un modele de 4 milliards de parametres
/// n'en a pas besoin — il retrouve par le nom qu'il a lui-meme donne.
///
/// <para><b>La consolidation est separee.</b> <see cref="Consolider"/> est
/// appelee a la fermeture de la fenetre, pas pendant la conversation.
/// C'est ce qui distingue « j'apprends au fil de l'eau » de « je
/// recapitule avant de partir ».</para>
/// </remarks>
public static class AssistantMemoire
{
    /// <summary>
    /// Construit la liste des quatre Capacite de memoire que l'Assistant
    /// peut appeler. A ajouter a la liste passee a
    /// <see cref="AssistantLocal.Repondre"/>.
    /// </summary>
    public static IReadOnlyList<AssistantLocal.Capacite> Creer()
    {
        return
        [
            new AssistantLocal.Capacite(
                "memoire_noter",
                "Note une information dans la memoire de l'Assistant, " +
                "au niveau demande (court/moyen/long). Un fichier Markdown " +
                "par sujet. La note est horodatee. Utilise cette capacite " +
                "quand l'utilisateur te dit quelque chose qui merite d'etre " +
                "retrouve plus tard, ou quand tu apprends un fait nouveau sur " +
                "lui ou sur le contexte de travail.",
                [
                    new AssistantLocal.Parametre("niveau", "court, moyen ou long", ["court", "moyen", "long"]),
                    new AssistantLocal.Parametre("sujet", "Le sujet en 2-5 mots, sans accents ni ponctuation (utilise le meme sujet pour le completer plus tard)", []),
                    new AssistantLocal.Parametre("contenu", "L'information a retenir, en 1-3 phrases", []),
                ],
                args => Memoire.Noter(
                    ParserNiveau(args.GetValueOrDefault("niveau")),
                    args.GetValueOrDefault("sujet") ?? "",
                    args.GetValueOrDefault("contenu") ?? ""),
                Interne: true),

            new AssistantLocal.Capacite(
                "memoire_lire",
                "Lit un souvenir par son sujet et son niveau. Renvoie le " +
                "contenu complet du fichier, ou 'sujet inconnu' si rien " +
                "n'a ete note sous ce nom.",
                [
                    new AssistantLocal.Parametre("niveau", "court, moyen ou long", ["court", "moyen", "long"]),
                    new AssistantLocal.Parametre("sujet", "Le sujet exact, ou un fragment de celui utilise a la note", []),
                ],
                args =>
                {
                    var lu = Memoire.Lire(
                        ParserNiveau(args.GetValueOrDefault("niveau")),
                        args.GetValueOrDefault("sujet") ?? "");
                    return lu ?? "sujet inconnu";
                }),

            new AssistantLocal.Capacite(
                "memoire_lister",
                "Liste les sujets notes a un niveau, avec la date du " +
                "dernier paragraphe. Utile pour retrouver ce que tu sais.",
                [
                    new AssistantLocal.Parametre("niveau", "court, moyen ou long", ["court", "moyen", "long"]),
                ],
                args =>
                {
                    var liste = Memoire.Lister(ParserNiveau(args.GetValueOrDefault("niveau")));
                    return liste.Count == 0
                        ? "rien de note a ce niveau"
                        : string.Join("\n", liste.Select(t =>
                            $"{t.Sujet}  (modifie {t.DerniereModification:yyyy-MM-dd HH:mm})"));
                }),

            new AssistantLocal.Capacite(
                "memoire_oublier",
                "Supprime un souvenir. A utiliser quand une note est " +
                "devenue obsolète ou quand l'utilisateur demande d'oublier.",
                [
                    new AssistantLocal.Parametre("niveau", "court, moyen ou long", ["court", "moyen", "long"]),
                    new AssistantLocal.Parametre("sujet", "Le sujet exact a oublier", []),
                ],
                args =>
                {
                    var niveau = ParserNiveau(args.GetValueOrDefault("niveau"));
                    var nom = args.GetValueOrDefault("sujet") ?? "";
                    if (string.IsNullOrWhiteSpace(nom)) return "sujet vide";
                    var chemin = Path.Combine(
                        AppContext.BaseDirectory, "Memoire", niveau.ToString(),
                        nom.ToLowerInvariant().Replace(" ", "-") + ".md");
                    if (!File.Exists(chemin)) return "sujet inconnu";
                    File.Delete(chemin);
                    return $"oublie : {nom}";
                }),
        ];
    }

    /// <summary>
    /// Appelé a la fermeture de la fenetre. Fait la promotion automatique
    /// (court -> moyen apres 2 jours) et purge les notes oubliees.
    /// </summary>
    public static string Consolider(Action<string>? journal = null)
    {
        Memoire.AssurerDossiers();
        var promus = Memoire.Promouvoir();
        var oublies = Memoire.Purger(journal);
        var msg = $"consolidation: {promus} note(s) promue(s) en moyen terme, {oublies} oubliee(s)";
        journal?.Invoke(msg);
        return msg;
    }

    private static NiveauMemoire ParserNiveau(string? texte)
    {
        return texte?.ToLowerInvariant() switch
        {
            "moyen" or "moyen terme" or "moyen-terme" => NiveauMemoire.Moyen,
            "long" or "long terme" or "long-terme" => NiveauMemoire.Long,
            _ => NiveauMemoire.Court,
        };
    }
}