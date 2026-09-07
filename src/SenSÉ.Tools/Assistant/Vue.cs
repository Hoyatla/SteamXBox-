using System.Text.Json.Nodes;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Ce qui permet à l'assistant de regarder une image, et non seulement d'en lire le nom.
/// </summary>
/// <remarks>
/// <b>Le modèle voyait ; c'est nous qui ne lui montrions rien.</b> Le serveur charge bien le
/// projecteur — <c>loaded multimodal model</c> dans son journal — mais la conversation ne
/// transportait que du texte. Un chemin d'image déposé dans la fenêtre arrivait au modèle sous forme
/// de caractères, et il répondait, en toute honnêteté, qu'il ne savait pas lire une image. Il
/// décrivait sa situation, pas sa capacité.
///
/// <para>
/// Un chemin qui désigne une image existante devient donc une image jointe. C'est ce qui donne enfin
/// un sens au glisser-déposer : déposer un fichier n'écrit plus seulement son nom, cela le montre.
/// </para>
///
/// <para>
/// Le texte est conservé tel quel à côté de l'image, chemin compris : le modèle en a besoin pour
/// nommer le fichier aux outils qui, eux, ne voient rien et ne travaillent que sur des chemins.
/// </para>
/// </remarks>
public static class Vue
{
    /// <summary>Les images qu'un message peut porter, au-delà desquelles on n'en joint plus.</summary>
    /// <remarks>
    /// Une image coûte plus de mille jetons. Deux tiennent sans peine dans le contexte — voir
    /// <see cref="ServeurModele.Contexte"/>, qui est la seule place où ce nombre est écrit ; dix les
    /// rempliraient à elles seules, et l'utilisateur qui dépose un dossier entier ne s'attend pas à
    /// ce que sa conversation en meure.
    /// </remarks>
    public const int Maximum = 2;

    /// <summary>Au-delà, une image n'est pas jointe : la transporter coûterait plus qu'elle ne vaut.</summary>
    private const long PoidsMaximum = 12 * 1024 * 1024;

    private static readonly string[] Extensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    /// <summary>Le contenu d'un message : du texte seul, ou du texte et ses images.</summary>
    public static JsonNode Contenu(string demande, Action<string>? journal)
    {
        var images = Designees(demande);

        if (images.Count == 0)
        {
            return JsonValue.Create(demande ?? "")!;
        }

        var morceaux = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = demande },
        };

        foreach (var image in images)
        {
            try
            {
                morceaux.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = "data:" + Genre(image) + ";base64,"
                            + Convert.ToBase64String(File.ReadAllBytes(image)),
                    },
                });

                journal?.Invoke($"image montrée à l'assistant : {Path.GetFileName(image)}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                journal?.Invoke($"image illisible, non montrée : {Path.GetFileName(image)}");
            }
        }

        // Toutes illisibles : on rend le texte seul plutôt qu'un message à une seule branche, que
        // le serveur accepterait mais qui ferait croire à une image transmise.
        return morceaux.Count > 1 ? morceaux : JsonValue.Create(demande ?? "")!;
    }

    /// <summary>Les images existantes qu'un texte désigne, dans l'ordre où elles apparaissent.</summary>
    /// <remarks>
    /// Reconnues par l'existence du fichier, jamais par la forme du chemin. Un chemin Windows
    /// contient des espaces — « Modspack perso » en est un — et toute règle de découpage se
    /// tromperait ; demander au disque est la seule vérification qui ne se discute pas.
    /// </remarks>
    public static IReadOnlyList<string> Designees(string demande)
    {
        var trouvees = new List<string>();

        foreach (var ligne in (demande ?? "").Split('\n'))
        {
            foreach (var morceau in Candidats(ligne))
            {
                if (trouvees.Count >= Maximum)
                {
                    return trouvees;
                }

                if (morceau.Length == 0
                    || !Extensions.Contains(Path.GetExtension(morceau), StringComparer.OrdinalIgnoreCase)
                    || trouvees.Contains(morceau, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(morceau) && new FileInfo(morceau).Length <= PoidsMaximum)
                    {
                        trouvees.Add(morceau);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException
                          or ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Un chemin que le système refuse d'examiner n'est pas une image à montrer.
                }
            }
        }

        return trouvees;
    }

    /// <summary>Ce qui, dans une ligne, pourrait être un chemin de fichier.</summary>
    /// <remarks>
    /// Trois formes, parce que l'utilisateur en produit trois : la ligne entière quand il a fait
    /// glisser un fichier, la portion entre guillemets quand il l'a copiée depuis l'explorateur, et
    /// le reste de la ligne à partir d'une lettre de lecteur quand il l'a collée au milieu d'une
    /// phrase — c'est le cas de « "D:\…\image.png" refaire un essai avec cette image ».
    /// </remarks>
    private static IEnumerable<string> Candidats(string ligne)
    {
        var propre = ligne.Trim();

        yield return propre.Trim('"').Trim();

        var ouvrant = propre.IndexOf('"', StringComparison.Ordinal);

        if (ouvrant >= 0)
        {
            var fermant = propre.IndexOf('"', ouvrant + 1);

            if (fermant > ouvrant + 1)
            {
                yield return propre[(ouvrant + 1)..fermant];
            }
        }

        var lecteur = propre.IndexOf(":\\", StringComparison.Ordinal);

        if (lecteur > 0)
        {
            yield return propre[(lecteur - 1)..].Trim('"').Trim();
        }
    }

    private static string Genre(string image) => Path.GetExtension(image).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };
}
