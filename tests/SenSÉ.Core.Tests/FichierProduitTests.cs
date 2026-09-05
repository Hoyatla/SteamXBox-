using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce qui permet à l'assistant d'enchaîner un outil sur le précédent.
/// </summary>
/// <remarks>
/// Chaque verbe annonce sa réussite dans sa propre langue — « Terminé : », « Écrit : », « Terminé
/// en 143 s : ». Ces phrases sont éprouvées ici telles qu'elles existent, parce que c'est sur
/// elles que l'enchaînement repose ; mais la reconnaissance, elle, ne dépend d'aucune : c'est le
/// disque qui tranche. Une phrase reformulée demain continuera de marcher.
/// </remarks>
public class FichierProduitTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("produit");

    private string Poser(string nom)
    {
        var ou = Path.Combine(_bac.FullName, nom);
        File.WriteAllText(ou, "x");

        return ou;
    }

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Les phrases que les verbes rendent réellement aujourd'hui.</summary>
    [Fact]
    public void TheSentencesTheVerbsActuallyReturnAreUnderstood()
    {
        var fichier = Poser("animation_00001.mp4");

        Assert.Equal(fichier, FichierProduit.Trouver($"Terminé : {fichier}"));
        Assert.Equal(fichier, FichierProduit.Trouver($"Écrit : {fichier}"));
        Assert.Equal(fichier, FichierProduit.Trouver($"Terminé en 143 s : {fichier}"));
    }

    // Le chemin est rarement en fin de phrase : ce qui suit ne doit pas partir avec lui.
    [Fact]
    public void WhatFollowsThePathIsNotTakenWithIt()
    {
        var fichier = Poser("arbre.png");

        Assert.Equal(fichier, FichierProduit.Trouver($"Terminé : {fichier} — ouvert dans le dossier."));
        Assert.Equal(fichier, FichierProduit.Trouver($"Écrit : {fichier}\nDossier révélé."));
    }

    /// <summary>Un fichier qui n'existe pas n'est jamais proposé.</summary>
    /// <remarks>
    /// C'est la propriété qui compte. Proposer un chemin inventé ferait échouer l'outil suivant en
    /// nommant un fichier dont l'utilisateur n'a jamais entendu parler — impossible à relier à sa
    /// demande.
    /// </remarks>
    [Fact]
    public void APathThatDoesNotExistIsNeverOffered()
        => Assert.Null(FichierProduit.Trouver(@"Terminé : D:\rien\du\tout\jamais-vu.mp4"));

    [Fact]
    public void AnAnswerWithoutAPathOffersNothing()
    {
        Assert.Null(FichierProduit.Trouver("Choisissez une vidéo, puis lancez."));
        Assert.Null(FichierProduit.Trouver("Le générateur a refusé le flux : nœuds en cause : 4, 6"));
        Assert.Null(FichierProduit.Trouver(""));
        Assert.Null(FichierProduit.Trouver(null));
    }

    // Un dossier n'est pas un fichier produit : File.Exists le refuse déjà, et il ne faut pas que la
    // recherche se rabatte dessus en raccourcissant le chemin.
    [Fact]
    public void AFolderIsNotAProducedFile()
        => Assert.Null(FichierProduit.Trouver($"Terminé : {_bac.FullName}\\pas-la.mp4"));

    // Le nom seul est inchaînable, et c'est voulu : sans dossier, il ne désigne rien pour l'outil
    // suivant. Mieux vaut ne rien proposer qu'un chemin reconstruit au jugé.
    [Fact]
    public void ABareFileNameIsNotEnough()
    {
        Poser("rapport.docx");

        Assert.Null(FichierProduit.Trouver("Écrit : rapport.docx"));
    }

    // Les espaces dans un chemin sont la norme sur cette machine — « Modspack perso », « sauv.minecraft ».
    [Fact]
    public void SpacesInAPathAreOrdinary()
    {
        var fichier = Poser("mon film final.mp4");

        Assert.Equal(fichier, FichierProduit.Trouver($"Terminé : {fichier}."));
    }
}
