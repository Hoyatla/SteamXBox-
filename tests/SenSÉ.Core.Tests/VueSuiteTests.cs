using SenSÉ.Tools.Assistant;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce qui casse quand un message n'est plus une chaîne.
/// </summary>
/// <remarks>
/// <b>La vue a marché du premier coup, et a fait tomber le tour suivant.</b> Le 23 août, l'assistant
/// a décrit correctement une image — « une guerrière blonde tenant une épée lumineuse sous un ciel
/// étoilé » — puis la demande d'après a échoué sur « The node must be of type 'JsonValue' ».
///
/// <para>
/// La cause : depuis que les images sont jointes, un message d'utilisateur peut être un tableau
/// — du texte, puis l'image — au lieu d'une chaîne. Or le rappel des travaux relit tout l'historique
/// pour retrouver sa marque, et le lisait comme une chaîne. Une fonction ajoutée en avait cassé une
/// autre, à un endroit qui n'avait pas été touché.
/// </para>
/// </remarks>
[Collection(Carnets.Nom)]
public class VueSuiteTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("vuesuite");

    public VueSuiteTests() => FichierTravail.Racine = _bac.FullName;

    public void Dispose()
    {
        FichierTravail.Racine = null;
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Un message porteur d'image traverse un second tour sans exception.</summary>
    /// <remarks>
    /// L'épreuve n'appelle pas le serveur : l'arrêt est demandé d'emblée, ce qui suffit à parcourir
    /// la préparation du fil — c'est là que se trouvait le défaut, pas dans la réponse du modèle.
    /// </remarks>
    [Fact]
    public void AMessageCarryingAnImageSurvivesASecondTurn()
    {
        var image = Path.Combine(_bac.FullName, "photo.png");
        File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        FichierTravail.Noter("Mes tâches", ["regarder l'image"], null);

        using var arret = new CancellationTokenSource();
        arret.Cancel();

        var agent = new AssistantLocal();

        agent.Repondre(image, [], [], (_, _) => "", null, arret.Token);

        // Le second tour est celui qui échouait : le premier avait déposé un tableau dans le fil.
        var exception = Record.Exception(
            () => agent.Repondre("recrée-moi une nouvelle image", [], [], (_, _) => "", null, arret.Token));

        Assert.Null(exception);
    }
}
