namespace SteamXBox.Plugins;

/// <summary>Une option d'un choix : ce que l'utilisateur lit, et ce que l'outil reçoit.</summary>
/// <param name="Valeur">Ce qui part dans la cible.</param>
/// <param name="Libelle">Ce qui s'affiche.</param>
public readonly record struct OptionOutil(string Valeur, string Libelle)
{
    /// <summary>Ce qui s'affiche, pour une liste déroulante.</summary>
    public override string ToString() => Libelle;
}

/// <summary>
/// Traduit les options d'un choix entre les mots de l'utilisateur et les chiffres de l'outil.
/// </summary>
/// <remarks>
/// <b>Le problème que cela résout.</b> Un panneau demandait « Mouvement, entre 1 et 255 ». Personne
/// ne sait ce que vaut 127, ni qu'au-delà de 200 l'image se déforme au point de n'avoir plus de
/// sens. Le réglage était donc à la fois incompréhensible et dangereux — deux défauts qui se
/// soignent ensemble en écrivant <c>60=Léger</c>, <c>127=Modéré</c>, <c>180=Ample</c> : trois mots
/// qu'on comprend, et la zone qui casse simplement absente.
///
/// <para>
/// <b>Une option sans signe égal reste elle-même.</b> Les listes lues sur la machine — les modèles
/// installés, les extensions d'un fichier — n'ont pas de libellé et n'en veulent pas : un nom de
/// fichier est déjà ce qu'il faut montrer. La règle ne coûte donc rien aux manifestes existants.
/// </para>
///
/// <para>
/// <b>Le premier signe égal seulement.</b> Un libellé peut en contenir — « 1024=1024 × 1024 » — et
/// découper sur le dernier ou sur tous produirait une valeur fausse là où l'on croyait ne rien
/// changer.
/// </para>
/// </remarks>
public static class OptionsOutil
{
    /// <summary>Lit une option écrite <c>valeur=Libellé</c>, ou une option nue.</summary>
    public static OptionOutil Lire(string? option)
    {
        var texte = (option ?? "").Trim();
        var egal = texte.IndexOf('=', StringComparison.Ordinal);

        if (egal <= 0 || egal == texte.Length - 1)
        {
            return new OptionOutil(texte, texte);
        }

        return new OptionOutil(texte[..egal].Trim(), texte[(egal + 1)..].Trim());
    }

    /// <summary>Toutes les options d'un élément, traduites.</summary>
    public static IReadOnlyList<OptionOutil> Lire(IEnumerable<string> options)
        => options.Select(Lire).ToList();

    /// <summary>
    /// La valeur que l'outil doit recevoir pour ce que l'utilisateur — ou le modèle — a dit.
    /// </summary>
    /// <remarks>
    /// Accepte le libellé comme la valeur. L'utilisateur choisit « Ample » dans une liste ;
    /// l'assistant, lui, peut employer l'un ou l'autre selon ce qu'il a retenu de la déclaration, et
    /// lui refuser « 180 » parce qu'on attendait « Ample » serait une pédanterie qui casse une
    /// demande parfaitement claire.
    ///
    /// <para>
    /// Ce qui ne correspond à rien traverse inchangé : un champ dont les options viennent de la
    /// machine peut recevoir un nom de fichier que la liste ne portait pas encore, et le refuser
    /// ici masquerait la vraie erreur que l'arbitre dira bien mieux.
    /// </para>
    /// </remarks>
    public static string Valeur(IEnumerable<string> options, string? dit)
    {
        var cherche = (dit ?? "").Trim();

        if (cherche.Length == 0)
        {
            return cherche;
        }

        foreach (var option in Lire(options))
        {
            if (option.Valeur.Equals(cherche, StringComparison.OrdinalIgnoreCase)
                || option.Libelle.Equals(cherche, StringComparison.OrdinalIgnoreCase))
            {
                return option.Valeur;
            }
        }

        return cherche;
    }

    /// <summary>Le libellé à montrer pour une valeur, ou la valeur si elle n'en a pas.</summary>
    public static string Libelle(IEnumerable<string> options, string? valeur)
    {
        var cherche = (valeur ?? "").Trim();

        foreach (var option in Lire(options))
        {
            if (option.Valeur.Equals(cherche, StringComparison.OrdinalIgnoreCase))
            {
                return option.Libelle;
            }
        }

        return cherche;
    }
}
