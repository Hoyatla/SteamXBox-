using System.Text.Json;
using System.Text.Json.Nodes;

// Serveur MCP de SteamXBox — lecture seule.
//
// Il expose au modele ce que la machine sait deja : les outils declares dans Plugins/, les fichiers
// de l'utilisateur, et l'etat du produit. Rien de plus, et rien qui agisse : aucun outil de ce
// serveur n'ouvre une application, n'ecrit un fichier ni ne lance un processus.
//
// C'est deliberé et c'est la premiere tranche. « Ouvre ce que tu veux sur cette machine » offert a un
// client exterieur est de l'execution de code a distance par conception : dans un salon c'est
// confortable, dans un etablissement c'est ce qui fait refuser le produit. Ce qui agit viendra avec
// ce qui va avec — liste blanche, confirmation a l'ecran, journal des appels.
//
// Transport stdio, une ligne JSON par message : pas de port ouvert, pas d'authentification a ecrire,
// pas de surface d'attaque, et le client lance et arrete le serveur lui-meme.

var racine = RacineDuProduit();

// stdout porte le protocole et rien d'autre. Toute trace part sur stderr, sinon la premiere ligne de
// journal casse la session du client sans que personne ne comprenne pourquoi.
var entree = Console.OpenStandardInput();
using var lecteur = new StreamReader(entree, System.Text.Encoding.UTF8);
using var sortie = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false))
{
    AutoFlush = true,
};

while (await lecteur.ReadLineAsync() is { } ligne)
{
    if (ligne.Trim().Length == 0)
    {
        continue;
    }

    JsonNode? message;

    try
    {
        message = JsonNode.Parse(ligne);
    }
    catch (JsonException)
    {
        continue;
    }

    if (message is null)
    {
        continue;
    }

    var methode = message["method"]?.GetValue<string>() ?? "";
    var identifiant = message["id"];

    // Une notification n'a pas d'identifiant et n'attend pas de reponse. Repondre a
    // « notifications/initialized » est le premier faux pas classique d'un serveur ecrit a la main.
    if (identifiant is null)
    {
        continue;
    }

    JsonNode? resultat;

    try
    {
        resultat = methode switch
        {
            "initialize" => Initialize(),
            "ping" => new JsonObject(),
            "tools/list" => ListerOutils(),
            "tools/call" => Appeler(message["params"], racine),
            _ => null,
        };
    }
    catch (Exception exception)
    {
        // Un outil qui echoue rend une erreur d'outil, pas une erreur de protocole : le modele doit
        // pouvoir lire ce qui s'est passe et reessayer autrement.
        resultat = Texte($"L'appel a echoue : {exception.GetType().Name} — {exception.Message}", erreur: true);
    }

    if (resultat is null)
    {
        Ecrire(sortie, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = identifiant.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = -32601,
                ["message"] = $"Methode inconnue : {methode}",
            },
        });

        continue;
    }

    Ecrire(sortie, new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = identifiant.DeepClone(),
        ["result"] = resultat,
    });
}

return 0;

static void Ecrire(StreamWriter sortie, JsonNode reponse)
    => sortie.WriteLine(reponse.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

static JsonNode Initialize() => new JsonObject
{
    // La version du protocole est annoncee, pas negociee : un client plus recent la lit et decide.
    ["protocolVersion"] = "2024-11-05",
    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
    ["serverInfo"] = new JsonObject
    {
        ["name"] = "steamxbox",
        ["version"] = "1.0.0",
    },
};

/// <summary>
/// Les outils publies.
/// </summary>
/// <remarks>
/// Trois, tous en lecture seule, et chacun repond a une question qu'un modele local pose vraiment sur
/// une machine : qu'est-ce que cet environnement sait faire, ou est ce fichier, et est-ce que le
/// produit va bien.
/// </remarks>
static JsonNode ListerOutils() => new JsonObject
{
    ["tools"] = new JsonArray(
        new JsonObject
        {
            ["name"] = "outils_declares",
            ["description"] = "Liste les outils de l'environnement SteamXBox, tels que leurs manifestes les declarent.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
            },
        },
        new JsonObject
        {
            ["name"] = "chercher_fichier",
            ["description"] = "Cherche un fichier par son nom dans le dossier personnel de l'utilisateur. Lecture seule, rien n'est ouvert.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["motif"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Un morceau du nom cherche.",
                    },
                },
                ["required"] = new JsonArray("motif"),
            },
        },
        new JsonObject
        {
            ["name"] = "etat_produit",
            ["description"] = "Version de SteamXBox, manettes connues et fichiers d'etat presents.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
            },
        },
        new JsonObject
        {
            ["name"] = "manettes_connectees",
            ["description"] = "Les manettes que SteamXBox tient en ce moment. Demande que le produit soit lance.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
            },
        }),
};

static JsonNode Appeler(JsonNode? parametres, string racine)
{
    var nom = parametres?["name"]?.GetValue<string>() ?? "";
    var arguments = parametres?["arguments"];

    return nom switch
    {
        "outils_declares" => Texte(OutilsDeclares(racine)),
        "chercher_fichier" => Texte(ChercherFichier(arguments?["motif"]?.GetValue<string>() ?? "")),
        "etat_produit" => Texte(EtatProduit(racine)),

        // La premiere question posee au produit vivant plutot qu'a ses fichiers. Le reste des outils
        // lit le disque ; celui-ci passe par le tuyau, et dit quand personne n'ecoute.
        "manettes_connectees" => Texte(SteamXBox.Mcp.Produit.Demander("manettes")),
        _ => Texte($"Outil inconnu : {nom}", erreur: true),
    };
}

static JsonNode Texte(string texte, bool erreur = false)
{
    var reponse = new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = texte,
        }),
    };

    if (erreur)
    {
        reponse["isError"] = true;
    }

    return reponse;
}

/// <summary>
/// Lit les manifestes de <c>Plugins/</c>.
/// </summary>
/// <remarks>
/// Un <c>plugin.json</c> declare un nom, une description et des entrees typees : c'est deja la forme
/// d'une definition d'outil. Les lire ici plutot que de tenir une seconde liste veut dire qu'un outil
/// depose dans le dossier est connu du modele sans qu'une ligne soit ecrite ailleurs — la meme
/// promesse que « un outil est un dossier », etendue au modele.
/// </remarks>
static string OutilsDeclares(string racine)
{
    var dossier = Path.Combine(racine, "Plugins");

    if (!Directory.Exists(dossier))
    {
        return "Aucun dossier Plugins a cet emplacement.";
    }

    var lignes = new List<string>();

    foreach (var manifeste in Directory.EnumerateFiles(dossier, "plugin.json", SearchOption.AllDirectories))
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(manifeste)) is not JsonObject outil)
            {
                continue;
            }

            var nom = outil["name"]?.GetValue<string>() ?? Path.GetFileName(Path.GetDirectoryName(manifeste)) ?? "?";
            var aide = outil["hint"]?.GetValue<string>() ?? "";
            lignes.Add(aide.Length > 0 ? $"- {nom} : {aide}" : $"- {nom}");
        }
        catch (Exception)
        {
            // Un manifeste illisible est un outil de moins, pas un appel qui echoue.
        }
    }

    return lignes.Count == 0 ? "Aucun outil declare." : string.Join('\n', lignes);
}

/// <summary>
/// Cherche par nom, sous le seul dossier personnel de l'utilisateur.
/// </summary>
/// <remarks>
/// La racine n'est pas un parametre, et c'est le point : un modele qui choisit ou chercher peut
/// choisir <c>C:\</c>, et une recherche sur tout le disque expose autant qu'elle coute. Bornee en
/// nombre de resultats pour la meme raison — un modele n'a pas besoin de dix mille lignes pour
/// repondre a « ou est mon devis ».
/// </remarks>
static string ChercherFichier(string motif)
{
    motif = motif.Trim();

    if (motif.Length < 2)
    {
        return "Motif trop court : il faut au moins deux caracteres.";
    }

    var racine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var trouves = new List<string>();

    try
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
            MaxRecursionDepth = 8,
        };

        foreach (var fichier in Directory.EnumerateFiles(racine, "*", options))
        {
            if (!Path.GetFileName(fichier).Contains(motif, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            trouves.Add(fichier);

            if (trouves.Count >= 50)
            {
                trouves.Add("… (arrete a cinquante resultats)");
                break;
            }
        }
    }
    catch (Exception exception)
    {
        return $"Recherche interrompue : {exception.Message}";
    }

    return trouves.Count == 0 ? $"Rien qui contienne « {motif} »." : string.Join('\n', trouves);
}

static string EtatProduit(string racine)
{
    var lignes = new List<string> { $"Installation : {racine}" };

    var executable = Path.Combine(racine, "SteamXBox.exe");

    if (File.Exists(executable))
    {
        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(executable).FileVersion;
        lignes.Add($"Version : {version ?? "inconnue"}");
    }
    else
    {
        lignes.Add("SteamXBox.exe absent de ce dossier.");
    }

    var etat = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamXBox");

    if (Directory.Exists(etat))
    {
        var fichiers = Directory.EnumerateFiles(etat).Select(Path.GetFileName).ToArray();
        lignes.Add($"Etat local ({etat}) : {(fichiers.Length == 0 ? "vide" : string.Join(", ", fichiers))}");
    }
    else
    {
        lignes.Add("Aucun etat local : le produit n'a jamais tourne sur cette session.");
    }

    return string.Join('\n', lignes);
}

/// <summary>La racine du produit, pour lire Plugins/ quel que soit le dossier courant.</summary>
static string RacineDuProduit()
{
    var dossier = new DirectoryInfo(AppContext.BaseDirectory);

    while (dossier is not null)
    {
        if (Directory.Exists(Path.Combine(dossier.FullName, "Plugins")))
        {
            return dossier.FullName;
        }

        dossier = dossier.Parent;
    }

    return AppContext.BaseDirectory;
}
