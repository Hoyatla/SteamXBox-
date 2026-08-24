using System.Diagnostics;
using SteamXBox.Plugins;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// L'environnement propre donné à un outil extérieur.
/// </summary>
/// <remarks>
/// Ce qui est éprouvé ici vient d'un fait constaté : Comfy Desktop désinstallé a laissé
/// quarante-deux gigaoctets dans le dossier de l'utilisateur, parce qu'il les avait écrits là où
/// son environnement lui disait de le faire. Rien de tout cela n'aurait survécu s'il avait été
/// lancé avec un environnement à lui.
/// </remarks>
public class EnvironnementIsoleTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("env");

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private (ProcessStartInfo Depart, string Refus) Preparer(EnvironnementOutil? declare = null)
    {
        var depart = new ProcessStartInfo("outil.exe");

        return (depart, EnvironnementIsole.Preparer(depart, _bac.FullName, declare));
    }

    /// <summary>Ce que l'outil croit être le dossier de l'utilisateur est dans le produit.</summary>
    [Theory]
    [InlineData("APPDATA")]
    [InlineData("LOCALAPPDATA")]
    [InlineData("TEMP")]
    [InlineData("USERPROFILE")]
    public void WhatTheToolTakesForTheUserFolderIsInsideTheProduct(string variable)
    {
        var (depart, refus) = Preparer();

        Assert.Equal("", refus);
        Assert.StartsWith(_bac.FullName, depart.Environment[variable]!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Les caches des bibliothèques qui téléchargent sont détournés eux aussi.
    /// </summary>
    /// <remarks>
    /// C'est la variable la plus lourde de conséquences du lot : sans elle, une bibliothèque va
    /// chercher ses poids et les dépose dans le dossier de l'utilisateur, hors de toute
    /// désinstallation. Les quarante-deux gigaoctets retrouvés venaient de là.
    /// </remarks>
    [Theory]
    [InlineData("HF_HOME")]
    [InlineData("HUGGINGFACE_HUB_CACHE")]
    [InlineData("TORCH_HOME")]
    [InlineData("PIP_CACHE_DIR")]
    public void TheCachesOfDownloadingLibrariesAreDivertedToo(string variable)
        => Assert.StartsWith(
            _bac.FullName, Preparer().Depart.Environment[variable]!, StringComparison.OrdinalIgnoreCase);

    /// <summary>Les dossiers existent avant que l'outil ne démarre.</summary>
    /// <remarks>
    /// Beaucoup de programmes n'essaient pas de créer le dossier que leur environnement désigne :
    /// ils supposent qu'il est là, puisque Windows le garantit d'habitude.
    /// </remarks>
    [Fact]
    public void TheFoldersExistBeforeTheToolStarts()
    {
        var depart = Preparer().Depart;

        Assert.True(Directory.Exists(depart.Environment["LOCALAPPDATA"]));
        Assert.True(Directory.Exists(depart.Environment["TEMP"]));
    }

    /// <summary>
    /// Le lancement passe par le processus et non par le shell.
    /// </summary>
    /// <remarks>
    /// Windows n'accepte un environnement sur mesure que là : par le shell, l'enfant hérite de la
    /// session et tout le détournement est perdu sans un mot.
    /// </remarks>
    [Fact]
    public void TheLaunchGoesThroughTheProcessRatherThanTheShell()
        => Assert.False(Preparer().Depart.UseShellExecute);

    /// <summary>Ce que l'outil sait de la machine ne lui est pas retiré.</summary>
    /// <remarks>
    /// On isole où il écrit, pas ce qu'il voit : repartir d'un environnement vide casserait la
    /// plupart des programmes — à commencer par ceux qui cherchent leurs propres bibliothèques
    /// dans le PATH — pour un gain nul.
    /// </remarks>
    [Fact]
    public void WhatTheToolKnowsOfTheMachineIsNotTakenAway()
        => Assert.False(string.IsNullOrEmpty(Preparer().Depart.Environment["PATH"]));

    /// <summary>Un outil peut détourner une variable que la liste habituelle ignore.</summary>
    [Fact]
    public void AToolCanDivertAVariableTheStandardListDoesNotKnow()
    {
        var declare = new EnvironnementOutil { Detourne = { ["OLLAMA_MODELS"] = "modeles" } };

        Assert.Equal(
            Path.Combine(_bac.FullName, "modeles"),
            Preparer(declare).Depart.Environment["OLLAMA_MODELS"]);
    }

    /// <summary>Ce qui est partagé n'est pas rattaché au dossier de l'outil.</summary>
    /// <remarks>
    /// Le dossier des modèles est commun : chaque outil doit pouvoir le désigner sans qu'on en
    /// recopie soixante-six gigaoctets dans son environnement.
    /// </remarks>
    [Fact]
    public void WhatIsSharedIsNotTiedToTheToolFolder()
    {
        var declare = new EnvironnementOutil { Ajoute = { ["COMFYUI_MODELS"] = @"D:\Modeles" } };

        Assert.Equal(@"D:\Modeles", Preparer(declare).Depart.Environment["COMFYUI_MODELS"]);
    }

    /// <summary>Une déclaration sans dossier est refusée plutôt que devinée.</summary>
    [Fact]
    public void ADeclarationWithoutAFolderIsRefusedRatherThanGuessed()
        => Assert.Contains(
            "aucun dossier",
            EnvironnementIsole.Preparer(new ProcessStartInfo("x.exe"), "", null),
            StringComparison.Ordinal);

    /// <summary>Un manifeste qui ne demande rien n'a pas d'environnement imposé.</summary>
    [Fact]
    public void AManifestThatAsksForNothingGetsNoImposedEnvironment()
        => Assert.Null(new PluginManifest().Environnement);
}
