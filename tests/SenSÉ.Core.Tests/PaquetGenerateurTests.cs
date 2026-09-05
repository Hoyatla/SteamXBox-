using System.IO.Compression;
using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le générateur mis dans un fichier, et ressorti — sans réseau, sans installation.
/// </summary>
/// <remarks>
/// Le programme et son interpréteur pèsent cinq gigaoctets ; les modèles, soixante-six. Ce qui est
/// éprouvé ici est la frontière entre les deux, parce que c'est elle qui décide si l'archive est
/// transportable ou absurde.
/// </remarks>
public class PaquetGenerateurTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("paquet");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Monte un faux produit : le programme, l'interpréteur, et des modèles.</summary>
    private string Produit()
    {
        var racine = _bac.FullName;

        Ecrire(racine, @"Outils\ComfyUI\main.py", "print('comfy')");
        Ecrire(racine, @"Outils\ComfyUI\custom_nodes\ComfyUI-GGUF\nodes.py", "gguf");
        Ecrire(racine, @"Outils\Python\python.exe", "binaire");
        Ecrire(racine, @"Outils\ComfyUI\models\unet\enorme.gguf", new string('m', 4096));
        Ecrire(racine, @"Outils\ComfyUI\output\video_00001_.mp4", "sortie");
        Ecrire(racine, @"Outils\ComfyUI\input\photo.png", "entree");
        Ecrire(racine, @"Outils\ffmpeg\ffmpeg.exe", "pas a nous");

        return racine;
    }

    private static void Ecrire(string racine, string relatif, string contenu)
    {
        var chemin = Path.Combine(racine, relatif);

        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllText(chemin, contenu);
    }

    /// <summary>Le paquet emporte le programme et l'interpréteur, et rien d'autre.</summary>
    [Fact]
    public void ThePackageCarriesTheProgramAndItsInterpreter()
    {
        var noms = PaquetGenerateur.Contenu(Produit())
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains("main.py", noms);
        Assert.Contains("nodes.py", noms);
        Assert.Contains("python.exe", noms);
    }

    /// <summary>Les modèles restent dehors : soixante-six gigaoctets contre cinq.</summary>
    /// <remarks>
    /// Ils ne se compressent pas — ce sont des poids déjà quantifiés — et ils sont déjà décrits
    /// dans jeux-modeles.json, qui dit lesquels et où les prendre. Les embarquer multiplierait
    /// l'archive par quatorze pour transporter ce qu'une déclaration sait nommer.
    /// </remarks>
    [Fact]
    public void ModelsStayOutOfThePackage()
        => Assert.DoesNotContain(
            "enorme.gguf", PaquetGenerateur.Contenu(Produit()).Select(Path.GetFileName));

    /// <summary>Le travail de l'utilisateur non plus n'est pas emporté à son insu.</summary>
    [Theory]
    [InlineData("video_00001_.mp4")]
    [InlineData("photo.png")]
    public void TheUsersOwnFilesAreNotTakenAlong(string nom)
        => Assert.DoesNotContain(nom, PaquetGenerateur.Contenu(Produit()).Select(Path.GetFileName));

    // ffmpeg a sa propre licence et sa propre vie : il n'appartient pas au générateur.
    [Fact]
    public void WhatIsNotTheGeneratorIsLeftAlone()
        => Assert.DoesNotContain(
            "ffmpeg.exe", PaquetGenerateur.Contenu(Produit()).Select(Path.GetFileName));

    /// <summary>Empaqueter puis déballer redonne exactement le programme.</summary>
    [Fact]
    public void APackageMadeHereOpensBackToTheSameFiles()
    {
        var racine = Produit();
        var paquet = Path.Combine(_bac.FullName, "generateur.zip");

        Assert.Equal("", PaquetGenerateur.Compresser(racine, paquet));
        Assert.True(File.Exists(paquet));

        // On efface le programme, on garde les modèles : c'est le cas réel d'une restauration.
        Directory.Delete(Path.Combine(racine, @"Outils\Python"), recursive: true);
        File.Delete(Path.Combine(racine, @"Outils\ComfyUI\main.py"));

        Assert.Equal("", PaquetGenerateur.Decompresser(racine, paquet));

        Assert.Equal("print('comfy')", File.ReadAllText(Path.Combine(racine, @"Outils\ComfyUI\main.py")));
        Assert.True(File.Exists(Path.Combine(racine, @"Outils\Python\python.exe")));
    }

    /// <summary>Le déballage ne touche pas aux modèles, qu'il ne contient pas.</summary>
    [Fact]
    public void UnpackingLeavesTheModelsAlone()
    {
        var racine = Produit();
        var paquet = Path.Combine(_bac.FullName, "generateur.zip");
        var modele = Path.Combine(racine, @"Outils\ComfyUI\models\unet\enorme.gguf");

        PaquetGenerateur.Compresser(racine, paquet);
        PaquetGenerateur.Decompresser(racine, paquet);

        Assert.True(File.Exists(modele));
        Assert.Equal(4096, new FileInfo(modele).Length);
    }

    /// <summary>Une archive qui remonte hors du dossier est refusée.</summary>
    /// <remarks>
    /// Une archive n'est pas toujours celle qu'on a faite soi-même : elle peut venir d'un collègue,
    /// d'une clé, d'un téléchargement. « ..\..\Windows\System32 » est le plus vieux tour du métier,
    /// et il ne coûte rien de le fermer.
    /// </remarks>
    [Fact]
    public void AnArchiveThatEscapesTheFolderIsRefused()
    {
        var racine = Produit();
        var piege = Path.Combine(_bac.FullName, "piege.zip");

        using (var flux = File.Create(piege))
        using (var archive = new ZipArchive(flux, ZipArchiveMode.Create))
        {
            var entree = archive.CreateEntry("../../evade.txt");
            using var ecriture = new StreamWriter(entree.Open());
            ecriture.Write("dehors");
        }

        Assert.Contains("sort du dossier", PaquetGenerateur.Decompresser(racine, piege), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_bac.FullName, "..", "evade.txt")));
    }

    [Fact]
    public void AMissingPackageSaysSoRatherThanThrowing()
        => Assert.Contains(
            "introuvable",
            PaquetGenerateur.Decompresser(_bac.FullName, Path.Combine(_bac.FullName, "jamais.zip")),
            StringComparison.Ordinal);

    [Fact]
    public void NothingToPackageIsSaidPlainly()
        => Assert.Contains(
            "Aucun générateur",
            PaquetGenerateur.Compresser(
                Directory.CreateTempSubdirectory("vide").FullName,
                Path.Combine(_bac.FullName, "vide.zip")),
            StringComparison.Ordinal);

    /// <summary>L'avancement est rendu, sinon un écran ne peut rien montrer pendant cinq gigaoctets.</summary>
    [Fact]
    public void ProgressIsReportedWhilePacking()
    {
        var vus = new List<AvanceePaquet>();

        PaquetGenerateur.Compresser(
            Produit(), Path.Combine(_bac.FullName, "g.zip"), a => vus.Add(a));

        Assert.NotEmpty(vus);
        Assert.True(vus[^1].Fait == vus[^1].Total, "le dernier avancement doit être complet");
    }
}
