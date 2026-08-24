using SteamXBox.Plugins;
using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// L'outil qui dépasse les deux secondes d'un clip, en recollant plusieurs plans.
/// </summary>
/// <remarks>
/// <b>La borne vient d'une mesure, pas d'une prudence.</b> Trois maillons ont été générés en
/// repartant chaque fois de la dernière image du précédent : le premier tenait, le deuxième restait
/// net, le troisième perdait le visage et l'anatomie — et son fichier grossissait de bruit. Deux
/// maillons est donc un résultat, et l'épreuve est là pour qu'il ne se perde pas.
/// </remarks>
public class SequenceAnimeeTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("serie");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SequenceIsPartOfTheVocabulary()
    {
        Assert.Contains(PluginActions.Sequence, PluginActions.Known);
        Assert.True(PluginActions.NeedsTarget(PluginActions.Sequence));
        Assert.True(PluginActions.NeedsPanel(PluginActions.Sequence));
    }

    /// <summary>Le troisième maillon n'est pas atteignable.</summary>
    [Fact]
    public void TheChainStopsAtTwoLinks()
        => Assert.Equal(2, SequenceAnimee.PontsMaximum);

    /// <summary>Les images sont animées dans l'ordre de leurs noms.</summary>
    /// <remarks>
    /// C'est le seul ordre qu'un utilisateur puisse prévoir : il renomme ses fichiers 01, 02, 03 et
    /// s'attend à les voir défiler ainsi. Trier par date aurait suivi l'ordre de copie, que
    /// personne ne contrôle et qui change d'une clé USB à l'autre.
    /// </remarks>
    [Fact]
    public void ImagesAreTakenInTheOrderOfTheirNames()
    {
        foreach (var nom in new[] { "03.png", "01.png", "02.png" })
        {
            File.WriteAllText(Path.Combine(_bac.FullName, nom), "x");
        }

        Assert.Equal(
            ["01.png", "02.png", "03.png"],
            SequenceAnimee.Ordonner(_bac.FullName).Select(Path.GetFileName));
    }

    // Ce qui n'est pas une image ne se laisse pas animer : un fichier de notes posé dans le dossier
    // ferait échouer un plan au milieu d'une série d'une demi-heure.
    [Fact]
    public void WhatIsNotAnImageIsIgnored()
    {
        File.WriteAllText(Path.Combine(_bac.FullName, "photo.jpg"), "x");
        File.WriteAllText(Path.Combine(_bac.FullName, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_bac.FullName, "musique.mp3"), "x");

        Assert.Equal(["photo.jpg"], SequenceAnimee.Ordonner(_bac.FullName).Select(Path.GetFileName));
    }

    [Fact]
    public void AFolderThatIsNotThereYieldsNothing()
        => Assert.Empty(SequenceAnimee.Ordonner(Path.Combine(_bac.FullName, "absent")));

    /// <summary>La liste de montage supporte les apostrophes d'un chemin.</summary>
    /// <remarks>
    /// Le démultiplexeur <c>concat</c> délimite par des apostrophes. Un chemin qui en contient
    /// couperait la liste en deux, et ffmpeg se plaindrait d'un fichier introuvable dont le nom
    /// serait la moitié du vrai — une erreur qu'on chercherait longtemps.
    /// </remarks>
    [Fact]
    public void TheMontageListSurvivesAnApostrophe()
    {
        var liste = SequenceAnimee.Liste([@"D:\photos\l'été\clip.mp4"]);

        Assert.Equal("file 'D:\\photos\\l'\\''été\\clip.mp4'\n", liste);
    }

    [Fact]
    public void EachClipGetsItsOwnLine()
    {
        var liste = SequenceAnimee.Liste([@"C:\a.mp4", @"C:\b.mp4"]);

        Assert.Equal(2, liste.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
