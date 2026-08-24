namespace SteamXBox.Tools.Assistant;

/// <summary>
/// Retrouve, dans ce qu'un outil a répondu, le fichier qu'il vient de produire.
/// </summary>
/// <remarks>
/// <b>C'est ce qui permet d'enchaîner.</b> « Fais-moi un arbre, anime-le, puis agrandis-le » n'est
/// pas un graphe à composer : ce sont trois outils à la file, où la sortie de l'un devient l'entrée
/// du suivant. Il ne manquait qu'une chose pour que le modèle sache le faire — savoir quel fichier
/// vient d'apparaître, et pouvoir le recopier tel quel.
///
/// <para>
/// <b>Le système de fichiers tranche, pas une expression régulière.</b> Chaque verbe annonce sa
/// réussite dans sa propre langue — « Terminé : », « Écrit : », « Terminé en 143 s : » — et courir
/// après ces formulations reviendrait à casser l'enchaînement chaque fois que quelqu'un reformule
/// une phrase. Ici on repère où un chemin peut commencer, puis on demande au disque si ce fichier
/// existe. Une réponse fausse est impossible : ou le fichier est là, ou il n'y a rien à enchaîner.
/// </para>
///
/// <para>
/// Le corollaire est qu'un outil qui ne nomme pas son fichier en entier reste inchaînable, quoi
/// qu'il raconte. C'est voulu : mieux vaut ne rien proposer que proposer un chemin inventé, dont
/// l'échec, deux outils plus loin, parlerait d'un fichier que personne n'a jamais mentionné.
/// </para>
/// </remarks>
public static class FichierProduit
{
    /// <summary>
    /// Au-delà, ce n'est plus un chemin.
    /// </summary>
    /// <remarks>
    /// Windows accepte des chemins très longs, mais la borne est ici pour tenir le coût : la
    /// recherche raccourcit le candidat caractère par caractère, et chaque essai touche le disque.
    /// Une phrase entière prise pour un chemin coûterait autant d'appels qu'elle a de lettres.
    /// </remarks>
    private const int PlusLong = 400;

    /// <summary>Le plus long chemin de la phrase qui désigne un fichier existant, ou null.</summary>
    public static string? Trouver(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            return null;
        }

        for (var i = 0; i < phrase.Length; i++)
        {
            if (!Depart(phrase, i))
            {
                continue;
            }

            if (Confirmer(Candidat(phrase, i)) is { } trouve)
            {
                return trouve;
            }
        }

        return null;
    }

    /// <summary>Un chemin peut-il commencer ici ?</summary>
    /// <remarks>
    /// Deux formes seulement : une lettre de lecteur suivie de deux-points et d'un séparateur, ou
    /// le double antislash d'un chemin réseau. Un chemin relatif serait indiscernable d'un mot de
    /// la phrase, et n'aurait de toute façon pas de sens à transmettre à un autre outil, qui ne
    /// travaille pas depuis le même dossier.
    /// </remarks>
    private static bool Depart(string phrase, int i)
    {
        if (i + 1 < phrase.Length && phrase[i] == '\\' && phrase[i + 1] == '\\')
        {
            return true;
        }

        return i + 2 < phrase.Length
            && char.IsLetter(phrase[i])
            && phrase[i + 1] == ':'
            && (phrase[i + 2] == '\\' || phrase[i + 2] == '/');
    }

    /// <summary>Ce qui suit, jusqu'à la fin de la ligne.</summary>
    private static string Candidat(string phrase, int depart)
    {
        var fin = phrase.IndexOfAny(['\r', '\n'], depart);

        if (fin < 0)
        {
            fin = phrase.Length;
        }

        var longueur = Math.Min(fin - depart, PlusLong);

        return phrase.Substring(depart, longueur);
    }

    /// <summary>Le plus long préfixe du candidat qui soit un fichier existant.</summary>
    /// <remarks>
    /// On raccourcit par la droite parce que la phrase continue souvent après le chemin — une
    /// ponctuation, un mot. Le plus long qui existe est le bon : un chemin plus court qui existerait
    /// aussi serait un dossier parent, que <c>File.Exists</c> refuse déjà.
    /// </remarks>
    private static string? Confirmer(string candidat)
    {
        for (var longueur = candidat.Length; longueur > 3; longueur--)
        {
            // Le point final est retiré comme une espace, et ce n'est pas de la cosmétique :
            // Windows ignore les points et les espaces en fin de nom, si bien que
            // « ...\film.mp4. » désigne le même fichier et que File.Exists répond oui. Le chemin
            // rendu au modèle porterait alors la ponctuation de la phrase, qu'il recopierait
            // fidèlement dans l'appel suivant.
            var essai = candidat[..longueur].TrimEnd(' ', '\t', '.');

            if (essai.Length < 4)
            {
                break;
            }

            // Un chemin mal formé ne lève pas : File.Exists rend faux et l'on continue.
            if (File.Exists(essai))
            {
                return essai;
            }
        }

        return null;
    }
}
