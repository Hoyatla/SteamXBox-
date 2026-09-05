using SenSÉ.Tools.Modeles;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Les jeux de modèles, et le choix qu'ils permettent selon la machine.
/// </summary>
/// <remarks>
/// <b>Ce que cela évite.</b> Sans ce choix, il ne reste que deux issues : un produit qui ne tourne
/// que sur la machine de son auteur, ou un client qui télécharge treize gigaoctets pour découvrir
/// que sa carte ne les prend pas. Le second cas est arrivé ici même — seize gigaoctets installés
/// sur une carte de douze, puis quinze minutes de calcul à 13 % d'occupation.
/// </remarks>
public class JeuxModelesTests
{
    private const string Declaration = """
        {
          "jeux": [
            {
              "id": "gros", "nom": "Le gros", "pour": "Faire une image.", "licence": "Apache-2.0",
              "modules": ["ComfyUI-GGUF"],
              "fichiers": [
                { "url": "https://x/gros.gguf", "vers": "unet/gros.gguf", "octets": 8589934592 },
                { "url": "https://x/enc.safetensors", "vers": "text_encoders/enc.safetensors", "octets": 4294967296 }
              ]
            },
            {
              "id": "petit", "nom": "Le petit", "pour": "Pareil, en plus léger.", "licence": "Apache-2.0",
              "fichiers": [
                { "url": "https://x/petit.gguf", "vers": "unet/petit.gguf", "octets": 4294967296 }
              ]
            }
          ]
        }
        """;

    private static IReadOnlyList<JeuModeles> Jeux => JeuxModeles.Lire(Declaration);

    private static JeuModeles Un(string id) => Jeux.First(j => j.Id == id);

    [Fact]
    public void ADeclarationIsReadWithWhatEachSetNeeds()
    {
        Assert.Equal(2, Jeux.Count);

        var gros = Un("gros");

        Assert.Equal("Le gros", gros.Nom);
        Assert.Equal("Apache-2.0", gros.Licence);
        Assert.Equal(["ComfyUI-GGUF"], gros.Modules);
        Assert.Equal(2, gros.Fichiers.Count);
        Assert.Equal(12884901888, gros.Octets);
    }

    /// <summary>La mémoire vidéo se déduit du plus gros fichier, pas de la somme.</summary>
    /// <remarks>
    /// ComfyUI charge l'encodeur de texte, s'en sert, le libère, puis charge le modèle : les deux ne
    /// sont jamais sur la carte en même temps. C'est ce qui permet à un jeu de 12 Go de tenir sur
    /// une carte de 12 — et confondre la somme avec le pic ferait refuser un jeu qui passe.
    /// </remarks>
    [Fact]
    public void TheVideoMemoryIsThelargestFileRatherThanTheTotal()
    {
        Assert.Equal(8192, Un("gros").MemoireVideoMo);
        Assert.Equal(4096, Un("petit").MemoireVideoMo);
    }

    /// <summary>Le palier se déduit de la mémoire, et non d'une case cochée par l'auteur.</summary>
    /// <remarks>
    /// Un auteur qui range lui-même son jeu dans un palier se trompe ou se flatte ; la mémoire, elle,
    /// ne discute pas. Et un jeu ajouté demain atterrit tout seul au bon endroit — c'est ce qui fait
    /// que le catalogue reste juste sans que personne y pense.
    /// </remarks>
    [Theory]
    [InlineData(1048576, Palier.Bureau)]
    [InlineData(6291456000, Palier.Bureau)]
    [InlineData(6442450944, Palier.Jeu)]
    [InlineData(12582912000, Palier.Jeu)]
    [InlineData(13421772800, Palier.Expert)]
    public void TheTierIsDeducedFromTheMemory(long octets, Palier attendu)
    {
        var jeu = JeuxModeles.Lire($$"""
            { "jeux": [ { "id": "a",
              "fichiers": [ { "url": "https://x/a", "vers": "unet/a", "octets": {{octets}} } ] } ] }
            """)[0];

        Assert.Equal(attendu, jeu.Palier);
    }

    /// <summary>Un chiffre déclaré l'emporte, pour les cas où la déduction serait fausse.</summary>
    [Fact]
    public void ADeclaredFigureWins()
    {
        var jeu = JeuxModeles.Lire("""
            { "jeux": [ { "id": "a", "memoireVideoMo": 20000,
              "fichiers": [ { "url": "https://x/a", "vers": "unet/a", "octets": 1048576 } ] } ] }
            """);

        Assert.Equal(20000, jeu[0].MemoireVideoMo);
    }

    /// <summary>On recommande le plus exigeant qui tienne encore.</summary>
    /// <remarks>
    /// À qualité croissante avec la taille, c'est celui qui exploite la carte sans la déborder. Et
    /// quand rien ne tient, on ne propose rien : c'est exactement la proposition qu'il ne faut pas
    /// faire, et celle qui a coûté seize gigaoctets ce soir.
    /// </remarks>
    [Theory]
    [InlineData(12000, "gros")]
    [InlineData(8192, "gros")]
    [InlineData(8191, "petit")]
    [InlineData(4096, "petit")]
    [InlineData(4095, null)]
    [InlineData(0, null)]
    public void TheHeaviestThatStillFitsIsRecommended(int libreMo, string? attendu)
        => Assert.Equal(attendu, JeuxModeles.Recommander(Jeux, libreMo, "")?.Id);

    // La mémoire libre, jamais celle de la carte : le bureau de Windows en garde une part qui ne se
    // rend pas — 1 404 Mio sur 12 282 avant qu'un seul modèle ne soit chargé.
    [Fact]
    public void AGraphicsCardWhoseMemoryIsUnknownGetsNoRecommendation()
        => Assert.Null(JeuxModeles.Recommander(Jeux, 0, ""));

    /// <summary>Deux rôles ne se comparent pas.</summary>
    /// <remarks>
    /// <b>Le défaut que cette épreuve verrouille a été trouvé en mesurant, pas en relisant.</b> La
    /// règle « le plus lourd qui tienne » conseillait un modèle image-vidéo à qui voulait dessiner à
    /// partir d'une phrase, au seul motif qu'il était le plus gros de la liste. Plus lourd ne veut
    /// dire meilleur qu'entre choses comparables.
    /// </remarks>
    [Fact]
    public void TwoRolesAreNeverPutInCompetition()
    {
        var jeux = JeuxModeles.Lire("""
            {
              "jeux": [
                { "id": "image-legere", "role": "texte-image",
                  "fichiers": [ { "url": "https://x/a", "vers": "unet/a", "octets": 2097152 } ] },
                { "id": "video-lourde", "role": "image-video",
                  "fichiers": [ { "url": "https://x/b", "vers": "unet/b", "octets": 8388608 } ] }
              ]
            }
            """);

        Assert.Equal("image-legere", JeuxModeles.Recommander(jeux, 12000, "texte-image")?.Id);
        Assert.Equal("video-lourde", JeuxModeles.Recommander(jeux, 12000, "image-video")?.Id);

        // Et un rôle pour lequel rien n'est déclaré ne rend rien, plutôt que le plus proche.
        Assert.Null(JeuxModeles.Recommander(jeux, 12000, "texte-son"));
    }

    /// <summary>Un conseil par rôle : un produit qui fait deux choses en a besoin de deux.</summary>
    [Fact]
    public void OneRecommendationPerRole()
    {
        var jeux = JeuxModeles.Lire("""
            {
              "jeux": [
                { "id": "img-q5", "role": "texte-image",
                  "fichiers": [ { "url": "https://x/a", "vers": "unet/a", "octets": 8388608 } ] },
                { "id": "img-q4", "role": "texte-image",
                  "fichiers": [ { "url": "https://x/b", "vers": "unet/b", "octets": 4194304 } ] },
                { "id": "video", "role": "image-video",
                  "fichiers": [ { "url": "https://x/c", "vers": "unet/c", "octets": 2097152 } ] }
              ]
            }
            """);

        Assert.Equal(["img-q5", "video"], JeuxModeles.Conseils(jeux, 12000).Select(j => j.Id));

        // À huit mégaoctets de mémoire près, c'est le plus léger des deux images qui passe.
        Assert.Equal(["img-q4", "video"], JeuxModeles.Conseils(jeux, 7).Select(j => j.Id));
    }

    /// <summary>Un fichier tronqué n'est pas un fichier installé.</summary>
    /// <remarks>
    /// C'est la raison d'être de la taille exacte. Un téléchargement interrompu laisse un fichier
    /// qui existe et qui ment ; découvert plus tard, il se présente comme une erreur de format au
    /// premier usage, qu'on va chercher partout sauf dans le réseau.
    /// </remarks>
    [Fact]
    public void ATruncatedFileIsNotAnInstalledOne()
    {
        var bac = Directory.CreateTempSubdirectory("jeux");

        try
        {
            var jeu = JeuxModeles.Lire("""
                { "jeux": [ { "id": "un", "fichiers": [
                    { "url": "https://x/a", "vers": "unet/a.gguf", "octets": 64 } ] } ] }
                """)[0];

            Assert.Equal(EtatJeu.Absent, JeuxModeles.Etat(jeu, bac.FullName));

            var ou = Path.Combine(bac.FullName, "unet", "a.gguf");
            Directory.CreateDirectory(Path.GetDirectoryName(ou)!);

            // Le fichier existe, et il ment : c'est exactement ce que laisse un téléchargement
            // interrompu, et ce que la seule présence ne saurait pas distinguer.
            File.WriteAllBytes(ou, new byte[30]);
            Assert.Equal(EtatJeu.Absent, JeuxModeles.Etat(jeu, bac.FullName));

            File.WriteAllBytes(ou, new byte[64]);
            Assert.Equal(EtatJeu.Installe, JeuxModeles.Etat(jeu, bac.FullName));
        }
        finally
        {
            bac.Delete(recursive: true);
        }
    }

    [Fact]
    public void HalfADownloadShowsAsPartial()
    {
        var bac = Directory.CreateTempSubdirectory("jeux");

        try
        {
            var jeu = JeuxModeles.Lire("""
                { "jeux": [ { "id": "duo", "fichiers": [
                    { "url": "https://x/a", "vers": "unet/a", "octets": 4 },
                    { "url": "https://x/b", "vers": "vae/b", "octets": 4 } ] } ] }
                """)[0];

            Directory.CreateDirectory(Path.Combine(bac.FullName, "unet"));
            File.WriteAllBytes(Path.Combine(bac.FullName, "unet", "a"), new byte[4]);

            Assert.Equal(EtatJeu.Partiel, JeuxModeles.Etat(jeu, bac.FullName));

            Directory.CreateDirectory(Path.Combine(bac.FullName, "vae"));
            File.WriteAllBytes(Path.Combine(bac.FullName, "vae", "b"), new byte[4]);

            Assert.Equal(EtatJeu.Installe, JeuxModeles.Etat(jeu, bac.FullName));
        }
        finally
        {
            bac.Delete(recursive: true);
        }
    }

    /// <summary>Un chemin qui sortirait du dossier des modèles est écarté.</summary>
    /// <remarks>
    /// La déclaration peut venir d'ailleurs que du produit — un client la complète, quelqu'un la
    /// partage. Un chemin remontant écrirait n'importe où sur la machine sous couvert d'installer
    /// un modèle.
    /// </remarks>
    [Theory]
    [InlineData("../../Windows/System32/x.dll")]
    [InlineData("C:/Windows/System32/x.dll")]
    [InlineData("unet/../../x")]
    public void APathThatEscapesTheModelFolderIsDropped(string vers)
    {
        var jeux = JeuxModeles.Lire($$"""
            { "jeux": [ { "id": "a",
              "fichiers": [ { "url": "https://x/a", "vers": "{{vers}}", "octets": 4 } ] } ] }
            """);

        Assert.Empty(jeux);
    }

    // Un jeu mal formé est écarté et les autres restent : une virgule de trop dans une entrée ne
    // doit pas priver l'utilisateur de tout le catalogue.
    [Fact]
    public void ABadEntryDoesNotTakeTheOthersDown()
    {
        var jeux = JeuxModeles.Lire("""
            { "jeux": [
              { "id": "", "fichiers": [ { "url": "https://x/a", "vers": "unet/a", "octets": 4 } ] },
              { "id": "sansfichier", "fichiers": [] },
              { "id": "bon", "fichiers": [ { "url": "https://x/b", "vers": "unet/b", "octets": 4 } ] }
            ] }
            """);

        Assert.Equal(["bon"], jeux.Select(j => j.Id));
    }

    [Fact]
    public void AnUnreadableDeclarationYieldsNothingRatherThanThrowing()
    {
        Assert.Empty(JeuxModeles.Lire("{ pas du json"));
        Assert.Empty(JeuxModeles.Lire(""));
        Assert.Empty(JeuxModeles.Lire("{}"));
    }

    [Theory]
    [InlineData(8263222304, "7.7 Go")]
    [InlineData(246144152, "235 Mo")]
    public void TheWeightIsWrittenForAHuman(long octets, string attendu)
        => Assert.Equal(attendu, JeuxModeles.Poids(octets));
}
