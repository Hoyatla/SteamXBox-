using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// La table des moteurs : ce que la flottille contient, lu sur le disque.
/// </summary>
/// <remarks>
/// <b>Ce que ces épreuves protègent.</b> Le produit ne savait tenir qu'un modèle — un
/// <c>.gguf</c> au premier niveau, un port, un processus — si bien qu'essayer un second modèle
/// demandait de remplacer le premier. La table lève cette limite, et tout ce qui suit vérifie
/// qu'elle la lève sans introduire pire : un moteur déclaré mais incomplet, une voie annoncée mais
/// absente, un chemin résolu depuis le mauvais dossier.
///
/// <para>
/// Rien ici ne démarre de serveur ni ne lit un modèle réel : la suite tourne sur des machines qui
/// n'en ont aucun.
/// </para>
/// </remarks>
public class MoteursTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("moteurs");

    public MoteursTests() => Moteurs.Racine = _bac.FullName;

    public void Dispose()
    {
        Moteurs.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Pose un rôle : son dossier, son manifeste, et les fichiers qu'il déclare.</summary>
    private void Poser(string role, string manifeste, params string[] fichiers)
    {
        var dossier = Directory.CreateDirectory(Path.Combine(_bac.FullName, role));

        foreach (var fichier in fichiers)
        {
            var ou = Path.Combine(dossier.FullName, fichier);
            Directory.CreateDirectory(Path.GetDirectoryName(ou)!);
            File.WriteAllText(ou, "poids");
        }

        File.WriteAllText(Path.Combine(dossier.FullName, "modele.json"), manifeste);
    }

    [Fact]
    public void AnEngineIsReadWithItsRoleAndPort()
    {
        Poser("codage", """
            { "id": "coder", "nom": "Coder 3B", "role": "codage", "moteur": "llama.cpp",
              "port": 8083, "contexte": 32768, "fichiers": { "modele": "poids.gguf" } }
            """, "poids.gguf");

        var moteur = Moteurs.Pour("codage")!;

        Assert.Equal("coder", moteur.Id);
        Assert.Equal(8083, moteur.Port);
        Assert.Equal(32768, moteur.Contexte);
        Assert.Equal("http://127.0.0.1:8083", moteur.Adresse);
    }

    /// <summary>Les chemins sont résolus depuis le dossier du manifeste, et « .. » est permis.</summary>
    /// <remarks>
    /// C'est ce qui laisse les poids du modèle de dialogue à la racine — <c>ServeurModele</c> les y
    /// cherche encore — pendant que son manifeste vit dans son dossier de rôle. Résoudre depuis le
    /// répertoire courant, ou depuis la racine de la table, casserait ce montage sans prévenir.
    /// </remarks>
    [Fact]
    public void PathsResolveFromTheManifestFolderAndMayClimb()
    {
        File.WriteAllText(Path.Combine(_bac.FullName, "commun.gguf"), "poids");

        Poser("texte", """
            { "id": "dialogue", "role": "dialogue", "port": 8081,
              "fichiers": { "modele": "../commun.gguf" } }
            """);

        var moteur = Moteurs.Pour("dialogue")!;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_bac.FullName, "commun.gguf")),
            moteur.Poids);
    }

    /// <summary>Un moteur dont un fichier manque est écarté, pas proposé.</summary>
    /// <remarks>
    /// <b>Mieux vaut une voie absente qu'une voie qui échoue au premier appel.</b> C'est la règle
    /// déjà retenue pour les recherches distantes non configurées : une capacité déclarée qui rate
    /// à chaque fois coûte un tour et apprend au modèle à s'entêter.
    /// </remarks>
    [Fact]
    public void AnEngineWithAMissingFileIsLeftOut()
    {
        Poser("codage", """
            { "id": "coder", "role": "codage", "port": 8083,
              "fichiers": { "modele": "jamais-telecharge.gguf" } }
            """);

        Assert.Null(Moteurs.Pour("codage"));
        Assert.Empty(Moteurs.Lire());
    }

    /// <summary>« actif: false » retire un moteur du service sans l'effacer du disque.</summary>
    [Fact]
    public void AnEngineDeclaredInactiveIsNotServed()
    {
        Poser("orchestre", """
            { "id": "ecarte", "role": "orchestre", "port": 8082, "actif": false,
              "fichiers": { "modele": "poids.gguf" } }
            """, "poids.gguf");

        Assert.Null(Moteurs.Pour("orchestre"));
    }

    /// <summary>La vision se demande par capacité, jamais par rôle.</summary>
    /// <remarks>
    /// Elle appartient aujourd'hui au modèle de dialogue, par son projecteur, et pourrait demain
    /// appartenir à un modèle dédié. « Qui sait lire une image ? » survit à ce changement ; « le
    /// modèle de texte » non.
    /// </remarks>
    [Fact]
    public void SightIsAskedForAsACapabilityNotARole()
    {
        Poser("codage", """
            { "id": "coder", "role": "codage", "port": 8083,
              "fichiers": { "modele": "poids.gguf" } }
            """, "poids.gguf");

        Poser("texte", """
            { "id": "dialogue", "role": "dialogue", "port": 8081,
              "fichiers": { "modele": "poids.gguf", "projecteur": "vue.gguf" } }
            """, "poids.gguf", "vue.gguf");

        var voyant = Moteurs.Voyant()!;

        Assert.Equal("dialogue", voyant.Id);
        Assert.True(voyant.Vision);
        Assert.False(Moteurs.Pour("codage")!.Vision);
    }

    /// <summary>Les voies annoncées sont celles qui existent, et rien d'autre.</summary>
    [Fact]
    public void TheAnnouncedLanesAreTheOnesThatExist()
    {
        Poser("texte", """
            { "id": "dialogue", "role": "dialogue", "port": 8081, "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        Poser("audio", """
            { "id": "oreille", "role": "transcription", "port": 8084, "fichiers": { "modele": "p.bin" } }
            """, "p.bin");

        // Declare, mais son poids n'a jamais ete telecharge.
        Poser("video", """
            { "id": "film", "role": "video", "port": 8085, "fichiers": { "modele": "absent.gguf" } }
            """);

        Assert.Equal(["dialogue", "transcription"], Moteurs.Voies());
    }

    /// <summary>Un dossier sans manifeste n'est pas une erreur.</summary>
    /// <remarks>
    /// <c>vision/</c> est vide et volontairement : il attend un modèle dédié le jour où il en
    /// vaudra la peine. Traiter cette attente comme une panne ferait échouer le démarrage sur une
    /// installation parfaitement saine.
    /// </remarks>
    [Fact]
    public void AFolderWithoutAManifestIsNotAFault()
    {
        Directory.CreateDirectory(Path.Combine(_bac.FullName, "vision"));

        Poser("texte", """
            { "id": "dialogue", "role": "dialogue", "port": 8081, "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        Assert.Single(Moteurs.Lire());
    }

    /// <summary>Un manifeste illisible est signalé et sauté, jamais fatal.</summary>
    /// <remarks>
    /// Un JSON mal formé dans un dossier ne doit pas priver l'utilisateur des autres moteurs — et
    /// surtout pas du modèle de dialogue, qui est ce dont il se sert.
    /// </remarks>
    [Fact]
    public void AnUnreadableManifestIsSkippedNotFatal()
    {
        Poser("codage", "{ ceci n'est pas du JSON", "p.gguf");

        Poser("texte", """
            { "id": "dialogue", "role": "dialogue", "port": 8081, "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        var dits = new List<string>();
        var table = Moteurs.Lire(dits.Add);

        Assert.Single(table);
        Assert.Equal("dialogue", table[0].Id);
        Assert.Contains(dits, d => d.Contains("illisible", StringComparison.Ordinal));
    }

    /// <summary>Les arguments du manifeste arrivent tels quels.</summary>
    /// <remarks>
    /// C'est ce qui permet de régler un moteur sans recompiler : la réflexion coupée de
    /// l'aiguilleur, le <c>--params-backend</c> de la vidéo, le cache KV en huit bits. Les perdre
    /// en chemin ferait tourner chaque moteur sur les défauts, qui sont faux pour la plupart.
    /// </remarks>
    [Fact]
    public void TheManifestArgumentsArriveUntouched()
    {
        Poser("orchestre", """
            { "id": "aiguilleur", "role": "orchestre", "port": 8082,
              "arguments": ["--reasoning", "off", "-ctk", "q8_0"],
              "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        Assert.Equal(
            ["--reasoning", "off", "-ctk", "q8_0"],
            Moteurs.Pour("orchestre")!.Arguments);
    }

    /// <summary>Le serveur prend son port et sa place de travail dans le manifeste.</summary>
    /// <remarks>
    /// C'est tout le branchement : l'appelant demande une voie, jamais un modèle. Remplacer
    /// Qwen Coder par un autre ne change alors rien au code qui l'interroge.
    /// </remarks>
    [Fact]
    public void TheServerTakesItsPortAndContextFromTheManifest()
    {
        Poser("codage", """
            { "id": "coder", "role": "codage", "port": 8083, "contexte": 32768,
              "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        Poser("orchestre", """
            { "id": "aiguilleur", "role": "orchestre", "port": 8082, "contexte": 4096,
              "fichiers": { "modele": "p.gguf" } }
            """, "p.gguf");

        Assert.Equal("http://127.0.0.1:8083", ServeurModele.AdressePour("codage"));
        Assert.Equal("http://127.0.0.1:8082", ServeurModele.AdressePour("orchestre"));
        Assert.Equal(32768, ServeurModele.ContextePour("codage"));
        Assert.Equal(4096, ServeurModele.ContextePour("orchestre"));
    }

    /// <summary>Une voie que la table ignore retombe sur l'ancien chemin.</summary>
    /// <remarks>
    /// <b>C'est ce qui rend le branchement sûr.</b> Une installation sans manifeste — ou plus
    /// ancienne que la table — continue de fonctionner exactement comme avant : port 8081, place
    /// de travail de la constante. Brancher la table ne devait exiger de personne que tout soit
    /// décrit le même jour.
    /// </remarks>
    [Fact]
    public void ALaneTheTableIgnoresFallsBackToTheOldPath()
    {
        Assert.Equal("http://127.0.0.1:8081", ServeurModele.AdressePour("dialogue"));
        Assert.Equal(ServeurModele.Contexte, ServeurModele.ContextePour("dialogue"));
        Assert.Equal("http://127.0.0.1:8081", ServeurModele.Adresse);
    }

    // Aucun modele installe : la table est vide, et personne ne tombe.
    [Fact]
    public void AnEmptyInstallationYieldsAnEmptyTable()
    {
        Assert.Empty(Moteurs.Lire());
        Assert.Empty(Moteurs.Voies());
        Assert.Null(Moteurs.Voyant());
        Assert.Null(Moteurs.Pour("dialogue"));
    }
}
