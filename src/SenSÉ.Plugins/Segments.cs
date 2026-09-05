namespace SenSÉ.Plugins;

/// <summary>
/// La cible d'une action, découpée en segments — et ce qu'il faut faire d'une valeur qui contient
/// le séparateur.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci corrige.</b> Une cible s'écrit <c>fichier|réglage|réglage</c>, et le
/// panneau y substitue ce que l'utilisateur a tapé. Une invite d'image parfaitement ordinaire —
/// « un chat roux | style aquarelle » — ajoutait donc un segment. Le lecteur du flux répondait
/// « Réglage illisible, « = » attendu : « style aquarelle » », le travail ne partait pas, et rien
/// ne désignait le caractère fautif : l'utilisateur voyait un outil qui refuse une phrase qu'il
/// venait d'écrire.
///
/// <para>
/// <b>Une barre échappée, et rien d'autre.</b> <c>\|</c> vaut une barre littérale ; toute autre
/// barre oblique inverse reste ce qu'elle est. C'est la seule règle possible ici : une cible porte
/// des chemins Windows — <c>C:\Outils\Python\python.exe</c> — et traiter la barre oblique inverse
/// comme un échappement général les détruirait tous.
/// </para>
///
/// <para>
/// Le manifeste, lui, n'échappe rien : ses barres sont des séparateurs, c'est ce qu'il déclare. Seul
/// ce qui vient de l'utilisateur passe par <see cref="Echapper"/>, à l'instant de la substitution.
/// </para>
/// </remarks>
public static class Segments
{
    /// <summary>Une valeur d'utilisateur, rendue inoffensive pour le découpage.</summary>
    public static string Echapper(string? valeur)
        => (valeur ?? "").Replace("|", @"\|", StringComparison.Ordinal);

    /// <summary>
    /// Découpe une cible sur ses barres non échappées, et rend chaque segment déjà décodé.
    /// </summary>
    /// <param name="cible">La cible, après substitution et résolution des repères.</param>
    /// <param name="combien">
    /// Au plus tant de segments ; le dernier garde alors tout le reste, barres comprises. Zéro pour
    /// découper sans borne. C'est l'équivalent du compte de <c>string.Split</c>, dont deux appelants
    /// ont besoin pour laisser une valeur libre en fin de cible.
    /// </param>
    public static IReadOnlyList<string> Decouper(string? cible, int combien = 0)
    {
        var texte = cible ?? "";
        var morceaux = new List<string>();
        var courant = new System.Text.StringBuilder();

        for (var i = 0; i < texte.Length; i++)
        {
            // Une barre échappée n'est pas un séparateur : elle entre dans le segment, sans son
            // échappement.
            if (texte[i] == '\\' && i + 1 < texte.Length && texte[i + 1] == '|')
            {
                courant.Append('|');
                i++;

                continue;
            }

            // Le dernier segment autorisé garde tout ce qui suit, séparateurs compris — sans quoi
            // borner le découpage reviendrait à jeter la fin de la cible.
            if (texte[i] == '|' && (combien <= 0 || morceaux.Count < combien - 1))
            {
                morceaux.Add(courant.ToString());
                courant.Clear();

                continue;
            }

            courant.Append(texte[i]);
        }

        morceaux.Add(courant.ToString());

        return morceaux;
    }
}
