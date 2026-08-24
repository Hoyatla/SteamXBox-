using System.Text.Json;
using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Ce qu'un flux a réellement écrit, par opposition à ce qu'il a montré.
/// </summary>
/// <remarks>
/// <b>Une faute qui ne produit pas d'erreur, seulement un mensonge.</b> ComfyUI note dans son
/// histoire tout ce qu'un nœud a affiché, et marque chaque entrée de son <c>type</c> : <c>output</c>
/// pour un fichier écrit, <c>input</c> ou <c>temp</c> pour un simple aperçu. Nous ramassions les
/// trois.
///
/// <para>
/// Le 23 août, un montage de sept photos a donc annoncé « Terminé en 2 s : 7 fichiers dans
/// output » — les sept aperçus du chargeur, alors que le flux n'a qu'un seul nœud d'enregistrement.
/// Le compte était faux, et la conséquence pire que le compte : le message ne nomme le fichier que
/// lorsqu'il n'y en a qu'un, si bien que la vidéo produite n'était plus nommée du tout. L'assistant,
/// privé du chemin, a inventé « montage_1.mp4 » — un fichier qui n'existait nulle part, annoncé à
/// l'utilisateur avec aplomb.
/// </para>
/// </remarks>
public class RecolteTests
{
    private static JsonElement Sorties(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Le cas réel : un chargeur qui affiche, un enregistreur qui écrit.</summary>
    /// <remarks>
    /// C'est la forme exacte du montage qui a échoué : le nœud 1 lit sept images du dossier
    /// d'entrée et les affiche, le nœud 3 écrit une vidéo. Un seul fichier a été produit.
    /// </remarks>
    [Fact]
    public void OnlyWhatWasWrittenIsCounted()
    {
        var sorties = Sorties("""
        {
          "1": { "images": [
            { "filename": "a.jpg", "subfolder": "steamxbox-test", "type": "input" },
            { "filename": "b.jpg", "subfolder": "steamxbox-test", "type": "input" },
            { "filename": "c.jpg", "subfolder": "steamxbox-test", "type": "input" }
          ] },
          "3": { "videos": [
            { "filename": "sequence_00001_.mp4", "subfolder": "", "type": "output" }
          ] }
        }
        """);

        Assert.Equal(["sequence_00001_.mp4"], FluxTravail.Recolter(sorties));
    }

    /// <summary>Les fichiers temporaires ne sont pas des résultats non plus.</summary>
    /// <remarks>
    /// Un aperçu de prévisualisation vit dans <c>temp</c> et disparaît. L'annoncer comme un
    /// résultat enverrait l'utilisateur chercher dans le dossier de sortie un fichier qui n'y sera
    /// jamais.
    /// </remarks>
    [Fact]
    public void TemporaryFilesAreNotResultsEither()
    {
        var sorties = Sorties("""
        {
          "5": { "images": [ { "filename": "apercu.png", "subfolder": "", "type": "temp" } ] }
        }
        """);

        Assert.Empty(FluxTravail.Recolter(sorties));
    }

    /// <summary>Sans « type », l'entrée est gardée : les nœuds anciens n'en déclarent pas.</summary>
    /// <remarks>
    /// Exiger le champ aurait fait disparaître les résultats des flux qui marchaient jusqu'ici — une
    /// correction qui casse ce qu'elle prétend réparer, et qui se découvrirait sur le premier flux
    /// ancien, c'est-à-dire chez un client.
    /// </remarks>
    [Fact]
    public void AnEntryWithoutTypeIsKept()
    {
        var sorties = Sorties("""
        { "9": { "images": [ { "filename": "image_00001_.png", "subfolder": "" } ] } }
        """);

        Assert.Equal(["image_00001_.png"], FluxTravail.Recolter(sorties));
    }

    /// <summary>Le sous-dossier de sortie est conservé dans le chemin rendu.</summary>
    [Fact]
    public void TheOutputSubfolderIsKept()
    {
        var sorties = Sorties("""
        { "3": { "videos": [ { "filename": "v.mp4", "subfolder": "montages", "type": "output" } ] } }
        """);

        Assert.Equal([Path.Combine("montages", "v.mp4")], FluxTravail.Recolter(sorties));
    }

    /// <summary>Un flux sans nœud d'enregistrement ne rend rien, et c'est le bon résultat.</summary>
    /// <remarks>
    /// L'appelant a un message pour ce cas — « le flux n'a peut-être pas de nœud d'enregistrement ».
    /// Il ne servait à rien tant que les aperçus comptaient : il y avait toujours quelque chose à
    /// annoncer, et le vrai défaut restait invisible.
    /// </remarks>
    [Fact]
    public void AFlowThatSavesNothingReturnsNothing()
    {
        var sorties = Sorties("""
        { "1": { "images": [ { "filename": "a.jpg", "subfolder": "test", "type": "input" } ] } }
        """);

        Assert.Empty(FluxTravail.Recolter(sorties));
    }
}
