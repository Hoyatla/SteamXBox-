using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// L'arbitre qui rend la composition de graphes raisonnable.
/// </summary>
/// <remarks>
/// Sans lui, demander un graphe à un modèle de langage est un pari : il produit du JSON
/// vraisemblable, on l'envoie, et l'on apprend au bout de plusieurs minutes de chargement que le
/// modèle nommé n'existe pas sur ce disque. Avec lui, c'est une boucle — il propose, l'hôte refuse
/// en nommant la faute, il corrige.
///
/// <para>
/// Chaque épreuve ici correspond à une faute que le modèle commettra, et à la phrase qui doit lui
/// permettre de la corriger en un tour. Une faute détectée mais mal nommée ne vaut guère mieux
/// qu'une faute manquée.
/// </para>
/// </remarks>
public class FluxVerificateurTests
{
    /// <summary>Un catalogue réduit, de la même forme que celui du générateur.</summary>
    private static readonly Catalogue Connu = Catalogue.Lire("""
        {
          "CheckpointLoaderSimple": {
            "input": { "required": {
              "ckpt_name": [["svd_xt.safetensors", "autre.safetensors"], {}] } },
            "output": ["MODEL", "CLIP", "VAE"],
            "output_node": false,
            "category": "loaders",
            "description": "Charge un modèle."
          },
          "KSampler": {
            "input": { "required": {
              "model": ["MODEL", {}],
              "seed": ["INT", {"default": 0, "min": 0, "max": 1000}],
              "steps": ["INT", {"default": 20, "min": 1, "max": 100}],
              "cfg": ["FLOAT", {"default": 8.0, "min": 0.0, "max": 100.0}],
              "sampler_name": [["euler", "ddim"], {}],
              "latent_image": ["LATENT", {}] },
              "optional": { "denoise": ["FLOAT", {"min": 0.0, "max": 1.0}] } },
            "output": ["LATENT"],
            "output_node": false,
            "category": "sampling",
            "description": "Débruite."
          },
          "EmptyLatentImage": {
            "input": { "required": {
              "width": ["INT", {"default": 512, "min": 16, "max": 16384}] } },
            "output": ["LATENT"],
            "output_node": false,
            "category": "latent",
            "description": "Une toile vide."
          },
          "VAEDecode": {
            "input": { "required": {
              "samples": ["LATENT", {}],
              "vae": ["VAE", {}] } },
            "output": ["IMAGE"],
            "output_node": false,
            "category": "latent",
            "description": "Rend une image visible."
          },
          "SaveImage": {
            "input": { "required": {
              "images": ["IMAGE", {}],
              "codec": ["COMFY_DYNAMICCOMBO_V3", {}],
              "filename_prefix": ["STRING", {"default": "ComfyUI"}] } },
            "output": [],
            "output_node": true,
            "category": "image",
            "description": "Enregistre."
          }
        }
        """);

    private static IReadOnlyList<string> Juger(string flux)
        => FluxVerificateur.Verifier(flux, Connu);

    private static string Seule(string flux)
    {
        var fautes = Juger(flux);

        Assert.Single(fautes);

        return fautes[0];
    }

    [Fact]
    public void TheCatalogueIsReadAsTheServerDeclaresIt()
    {
        Assert.Equal(5, Connu.Compte);

        var sampler = Connu.Noeud("KSampler");

        Assert.NotNull(sampler);
        Assert.True(sampler!.Requises["model"].Cable);
        Assert.False(sampler.Requises["steps"].Cable);
        Assert.Equal(100, sampler.Requises["steps"].Max);
        Assert.Equal(["LATENT"], sampler.Sorties);
        Assert.True(Connu.Noeud("SaveImage")!.EstSortie);
    }

    /// <summary>Une liste de valeurs admises est un choix, pas un type.</summary>
    /// <remarks>
    /// C'est ainsi que ComfyUI énumère les fichiers réellement installés. C'est de là que vient la
    /// capacité à refuser un nom de modèle absent du disque sans rien connaître du disque.
    /// </remarks>
    [Fact]
    public void AListOfValuesIsAChoiceRatherThanAType()
    {
        var entree = Connu.Noeud("CheckpointLoaderSimple")!.Requises["ckpt_name"];

        Assert.Equal("COMBO", entree.Type);
        Assert.False(entree.Cable);
        Assert.Equal(["svd_xt.safetensors", "autre.safetensors"], entree.Valeurs);
    }

    /// <summary>Un choix dynamique reste un choix, pas un câble.</summary>
    /// <remarks>
    /// ComfyUI décline le mot : <c>COMFY_DYNAMICCOMBO_V3</c> désigne un choix dont les options
    /// dépendent d'une autre entrée — le codec d'une vidéo, qui change avec le format. Constaté sur
    /// le flux réellement livré : son codec se voyait reprocher d'être une valeur écrite là où il
    /// n'a jamais rien attendu d'autre. Reconnaître le seul type <c>COMBO</c> ne suffisait pas.
    /// </remarks>
    [Fact]
    public void ADynamicChoiceIsStillAChoice()
    {
        Assert.False(Connu.Noeud("SaveImage")!.Requises["codec"].Cable);

        Assert.DoesNotContain(
            Juger("""
                { "1": { "class_type": "SaveImage",
                         "inputs": { "images": ["1", 0], "codec": "auto",
                                     "filename_prefix": "x" } } }
                """),
            f => f.Contains("codec", StringComparison.Ordinal));
    }

    // Le flux qui marche doit passer sans un mot. Un arbitre qui se plaint d'un graphe correct est
    // pire qu'aucun arbitre : le modèle « corrige » ce qui allait bien.
    [Fact]
    public void AGraphThatWorksPassesInSilence()
    {
        var fautes = Juger("""
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } },
              "2": { "class_type": "KSampler",
                     "inputs": { "model": ["1", 0], "seed": 42, "steps": 20, "cfg": 8.0,
                                 "sampler_name": "euler", "latent_image": ["4", 0] } },
              "3": { "class_type": "SaveImage",
                     "inputs": { "images": ["5", 0], "codec": "auto", "filename_prefix": "essai" } },
              "5": { "class_type": "VAEDecode", "inputs": { "samples": ["2", 0], "vae": ["1", 2] } },
              "4": { "class_type": "EmptyLatentImage", "inputs": { "width": 512 } }
            }
            """);

        // Les fautes sont montrées si elles arrivent : « la collection n'était pas vide » ne dit
        // pas ce que l'arbitre reproche, et c'est précisément ce qu'on veut lire.
        Assert.True(fautes.Count == 0, string.Join(" | ", fautes));
    }

    [Fact]
    public void AnUnknownNodeIsNamedAndSoIsItsNeighbour()
    {
        Assert.Contains("« ksampler »", Seule("""
            { "1": { "class_type": "ksampler", "inputs": {} } }
            """), StringComparison.Ordinal);

        // Le modèle se trompe surtout de casse ou de séparateur. Lui rendre le nom voisin le remet
        // sur les rails en un tour, là où « nœud inconnu » le laisse recommencer au hasard.
        Assert.Contains("« KSampler »", Seule("""
            { "1": { "class_type": "K_Sampler", "inputs": {} } }
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRequiredInputIsNamedWithItsType()
    {
        var faute = Juger("""
            { "1": { "class_type": "SaveImage", "inputs": { "codec": "auto", "filename_prefix": "x" } } }
            """).First(f => f.Contains("images", StringComparison.Ordinal));

        Assert.Contains("Il manque", faute, StringComparison.Ordinal);
        Assert.Contains("IMAGE", faute, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInputTheNodeDoesNotHaveIsRefused()
        => Assert.Contains("n'a pas d'entrée « etapes »", Juger("""
            { "1": { "class_type": "SaveImage",
                     "inputs": { "images": ["1", 0], "codec": "auto", "filename_prefix": "x", "etapes": 5 } } }
            """).First(f => f.Contains("etapes", StringComparison.Ordinal)), StringComparison.Ordinal);

    /// <summary>Un câble mal typé est la faute que le serveur explique le plus mal.</summary>
    /// <remarks>
    /// Soumis tel quel, il donne une erreur qui parle d'un type manquant à l'autre bout du graphe,
    /// très loin du câble fautif. Ici les deux bouts sont nommés dans la même phrase.
    /// </remarks>
    [Fact]
    public void AMiswiredCableNamesBothEnds()
    {
        var faute = Seule("""
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } },
              "2": { "class_type": "SaveImage",
                     "inputs": { "images": ["1", 1], "codec": "auto", "filename_prefix": "x" } }
            }
            """);

        Assert.Contains("attend du IMAGE", faute, StringComparison.Ordinal);
        Assert.Contains("rend du CLIP", faute, StringComparison.Ordinal);
    }

    [Fact]
    public void ACableToANodeThatIsNotThereIsRefused()
        => Assert.Contains("qui n'existe pas dans ce flux", Juger("""
            { "1": { "class_type": "SaveImage",
                     "inputs": { "images": ["9", 0], "codec": "auto", "filename_prefix": "x" } } }
            """).First(f => f.Contains("images", StringComparison.Ordinal)), StringComparison.Ordinal);

    [Fact]
    public void ASlotThatDoesNotExistIsRefused()
        => Assert.Contains("qui en a 3", Juger("""
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } },
              "2": { "class_type": "SaveImage",
                     "inputs": { "images": ["1", 7], "codec": "auto", "filename_prefix": "x" } }
            }
            """).First(f => f.Contains("images", StringComparison.Ordinal)), StringComparison.Ordinal);

    // Écrire une valeur là où un câble est attendu est l'erreur naturelle de qui n'a jamais vu un
    // graphe : la réponse doit rappeler comment un câble s'écrit.
    [Fact]
    public void AValueWhereACableBelongsExplainsTheSyntax()
    {
        var faute = Juger("""
            { "1": { "class_type": "SaveImage",
                     "inputs": { "images": "mon-image.png", "filename_prefix": "x" } } }
            """).First(f => f.Contains("images", StringComparison.Ordinal));

        Assert.Contains("attend un câble", faute, StringComparison.Ordinal);
        Assert.Contains("rang de sortie", faute, StringComparison.Ordinal);
    }

    /// <summary>Le cas qui coûtait le plus cher : un modèle absent du disque.</summary>
    /// <remarks>
    /// C'est la faute qui justifie tout l'arbitre. Soumise, elle se paie en minutes de chargement
    /// avant un message obscur ; ici elle coûte une seconde, et la réponse porte la liste des
    /// modèles réellement installés.
    /// </remarks>
    [Fact]
    public void AModelThatIsNotOnThisDiskIsRefusedWithTheRealList()
    {
        var faute = Juger("""
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "flux1-dev.safetensors" } },
              "2": { "class_type": "SaveImage",
                     "inputs": { "images": ["1", 0], "codec": "auto", "filename_prefix": "x" } }
            }
            """).First(f => f.Contains("ckpt_name", StringComparison.Ordinal));

        Assert.Contains("ne peut pas valoir", faute, StringComparison.Ordinal);
        Assert.Contains("svd_xt.safetensors", faute, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"steps\": 500", "ne monte pas au-dessus de 100")]
    [InlineData("\"steps\": 0", "ne descend pas sous 1")]
    [InlineData("\"steps\": 20.5", "attend un entier")]
    [InlineData("\"steps\": \"vingt\"", "attend un nombre")]
    [InlineData("\"sampler_name\": \"dpmpp_2m\"", "Valeurs possibles ici")]
    public void ABadValueSaysWhatWouldBeGood(string ecrit, string attendu)
    {
        var flux = """
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } },
              "2": { "class_type": "KSampler",
                     "inputs": { "model": ["1", 0], "seed": 42, "steps": 20, "cfg": 8.0,
                                 "sampler_name": "euler", "latent_image": ["4", 0] } },
              "3": { "class_type": "SaveImage",
                     "inputs": { "images": ["2", 0], "codec": "auto", "filename_prefix": "x" } },
              "4": { "class_type": "EmptyLatentImage", "inputs": { "width": 512 } }
            }
            """;

        var nom = ecrit.Split(':')[0].Trim('"', ' ');
        var origine = nom == "steps" ? "\"steps\": 20" : "\"sampler_name\": \"euler\"";

        var fautes = Juger(flux.Replace(origine, ecrit, StringComparison.Ordinal));

        Assert.Contains(fautes, f => f.Contains(attendu, StringComparison.Ordinal));
    }

    /// <summary>Un flux qui ne garde rien tourne pour rien.</summary>
    /// <remarks>
    /// La panne la plus déroutante qui soit : la carte travaille, tout se passe bien, et il n'y a
    /// pas de fichier. Rien dans le journal ne dit pourquoi, puisque rien n'a échoué.
    /// </remarks>
    [Fact]
    public void AGraphThatSavesNothingIsRefused()
        => Assert.Contains("sans rien produire", Seule("""
            { "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } } }
            """), StringComparison.Ordinal);

    [Fact]
    public void AGraphThatBitesItsOwnTailIsRefused()
        => Assert.Contains(Juger("""
            {
              "1": { "class_type": "CheckpointLoaderSimple",
                     "inputs": { "ckpt_name": "svd_xt.safetensors" } },
              "2": { "class_type": "KSampler",
                     "inputs": { "model": ["1", 0], "seed": 1, "steps": 5, "cfg": 8.0,
                                 "sampler_name": "euler", "latent_image": ["2", 0] } },
              "3": { "class_type": "SaveImage",
                     "inputs": { "images": ["2", 0], "codec": "auto", "filename_prefix": "x" } }
            }
            """), f => f.Contains("en rond", StringComparison.Ordinal));

    [Fact]
    public void TheEditorFormatIsRecognised()
        => Assert.Contains("format de l'éditeur", Seule("""{"nodes": [], "links": []}"""),
            StringComparison.Ordinal);

    /// <summary>Le verdict est explicite dans les deux sens.</summary>
    /// <remarks>
    /// Une liste vide rendue telle quelle laisserait le modèle deviner si le flux a été jugé bon ou
    /// si la vérification n'a pas eu lieu.
    /// </remarks>
    [Fact]
    public void TheVerdictSaysPassAsPlainlyAsItSaysFail()
    {
        Assert.Contains("valide", FluxVerificateur.Verdict([]), StringComparison.Ordinal);
        Assert.Contains("1 faute", FluxVerificateur.Verdict(["une chose"]), StringComparison.Ordinal);
    }

    // Un flux entièrement faux produirait une faute par entrée de chaque nœud, et noierait les huit
    // mille jetons du modèle. Le compte des non-détaillées est annoncé plutôt que tu.
    [Fact]
    public void AFloodOfFaultsIsCutAndSaidToBeCut()
    {
        var beaucoup = Enumerable.Range(0, 40).Select(i => $"faute {i}").ToList();
        var verdict = FluxVerificateur.Verdict(beaucoup);

        Assert.Contains("40 fautes", verdict, StringComparison.Ordinal);
        Assert.Contains("28 fautes de plus", verdict, StringComparison.Ordinal);
        Assert.DoesNotContain("faute 30", verdict, StringComparison.Ordinal);
    }
}
