using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le carnet que l'utilisateur écrit lui-même.
/// </summary>
/// <remarks>
/// <b>La zone des travaux ne savait que montrer.</b> Tout ce qui s'y trouvait venait du modèle, et
/// l'utilisateur n'avait aucun moyen d'y déposer quoi que ce soit. Or une tâche dictée dans la
/// conversation disparaît au défilement, et l'assistant la traite comme une demande à satisfaire
/// sur-le-champ — alors que « pense à agrandir les photos du mariage » n'appelle aucune action
/// immédiate, seulement de ne pas être oublié.
/// </remarks>
[Collection(Carnets.Nom)]
public class MesTachesTests : IDisposable
{
    private const string Miennes = "Mes tâches";

    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("miennes");

    public MesTachesTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Ajouter une tâche garde celles qui étaient déjà là.</summary>
    /// <remarks>
    /// <c>Noter</c> réécrit le carnet entier : reprendre les étapes existantes n'est pas une
    /// précaution mais une nécessité, sans quoi chaque ajout effacerait la liste.
    /// </remarks>
    [Fact]
    public void AddingATaskKeepsTheOnesAlreadyThere()
    {
        FichierTravail.Noter(Miennes, ["agrandir les photos"], null);
        FichierTravail.Noter(Miennes, ["agrandir les photos", "trier le dossier"], null);

        var carnet = FichierTravail.Lire(Miennes, null)!;

        Assert.Equal(
            ["agrandir les photos", "trier le dossier"],
            carnet.Taches.Select(t => t.Texte));
    }

    /// <summary>Ce qui était coché le reste après un ajout.</summary>
    /// <remarks>
    /// C'est la propriété qui rend la liste utilisable dans la durée : ajouter une course en fin de
    /// journée ne doit pas rouvrir celles qu'on a faites le matin.
    /// </remarks>
    [Fact]
    public void WhatWasTickedStaysTickedAfterAnAddition()
    {
        FichierTravail.Noter(Miennes, ["agrandir les photos"], null);
        FichierTravail.Cocher(Miennes, "agrandir les photos", null);

        FichierTravail.Noter(Miennes, ["agrandir les photos", "trier le dossier"], null);

        var carnet = FichierTravail.Lire(Miennes, null)!;

        Assert.True(carnet.Taches[0].Faite);
        Assert.False(carnet.Taches[1].Faite);
    }

    /// <summary>Un carnet écrit par l'utilisateur n'attend pas son propre accord.</summary>
    /// <remarks>
    /// L'accord protège l'utilisateur d'un plan que le modèle aurait mal compris. Lui demander
    /// d'approuver ce qu'il vient d'écrire lui-même n'apprendrait qu'une chose : cliquer sans lire.
    /// </remarks>
    [Fact]
    public void AListWrittenByTheUserNeedsNoApprovalFromHim()
    {
        FichierTravail.Noter(Miennes, ["trier le dossier"], null);

        var carnet = FichierTravail.Lire(Miennes, null)!;

        Assert.DoesNotContain(
            "EN ATTENTE", FichierTravail.Resumer(carnet), StringComparison.Ordinal);
    }

    /// <summary>La consigne dit au modèle que ce carnet ne lui appartient pas.</summary>
    /// <remarks>
    /// Sans cela il le réécrirait comme les siens — <c>travail_noter</c> remplace la liste — et les
    /// tâches de l'utilisateur disparaîtraient à la première reformulation de plan.
    /// </remarks>
    [Fact]
    public void TheInstructionSaysThisNotebookIsNotHis()
    {
        Assert.Contains("MES TÂCHES", AssistantLocal.Regles, StringComparison.Ordinal);
        Assert.Contains("ne l'efface jamais", AssistantLocal.Regles, StringComparison.Ordinal);
    }
}
