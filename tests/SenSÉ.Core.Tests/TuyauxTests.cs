using System.Diagnostics;
using SenSÉ.Tools.Serveurs;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Vider les sorties d'un programme sans se bloquer avec lui.
/// </summary>
/// <remarks>
/// <b>Ce défaut a coûté une heure d'attente le 23 août.</b> Le journal annonce « Montage de 7
/// clips… » à 17:58:12 et n'écrit plus rien pendant soixante-quatre minutes ; l'utilisateur a fini
/// par fermer le produit. Le fichier portait l'horodatage de la fermeture : le montage avait pris
/// une seconde — <c>ffmpeg -c copy</c> ne réencode rien — et c'est notre attente qui avait duré une
/// heure.
///
/// <para>
/// Les épreuves lancent un programme qui remplit délibérément la sortie d'erreur au-delà du tampon
/// du tuyau. Avec l'ancienne lecture — l'une jusqu'au bout, puis l'autre — elles ne finiraient
/// jamais. Elles sont donc bornées dans le temps : une régression échoue au lieu de figer la suite.
/// </para>
/// </remarks>
public class TuyauxTests
{
    /// <summary>Combien de lignes il faut pour déborder un tampon de tuyau.</summary>
    /// <remarks>
    /// Le tampon fait quelques kilo-octets sous Windows. Vingt mille lignes en font largement plus,
    /// et c'est ce qu'un ffmpeg bavard produit en quelques secondes de travail.
    /// </remarks>
    private const int Lignes = 20000;

    private static Process Bavard(string flux)
    {
        var depart = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        depart.ArgumentList.Add("/c");
        depart.ArgumentList.Add(
            $"for /L %i in (1,1,{Lignes}) do @echo ligne de remplissage {flux}");

        return Process.Start(depart)!;
    }

    /// <summary>Un programme qui inonde sa sortie d'erreur ne bloque plus la lecture.</summary>
    /// <remarks>
    /// C'est le cas exact du montage : ffmpeg écrit son avancement sur la sortie d'erreur, remplit
    /// le tampon, se bloque en écrivant — et ne ferme donc jamais la sortie standard que nous
    /// lisions jusqu'au bout. Chacun attendait l'autre.
    /// </remarks>
    [Fact]
    public async Task AProgramFloodingItsErrorStreamNoLongerBlocks()
    {
        using var processus = Bavard("1>&2");

        var fait = Task.Run(() => Tuyaux.Vider(processus));
        var gagnant = await Task.WhenAny(fait, Task.Delay(TimeSpan.FromSeconds(60)));

        Assert.True(
            gagnant == fait,
            "Vider ne rend pas la main : les deux sorties ne sont pas lues en même temps.");

        Assert.Contains("ligne de remplissage", (await fait).Erreur, StringComparison.Ordinal);
    }

    /// <summary>L'inondation de la sortie standard se lit aussi.</summary>
    /// <remarks>
    /// La symétrie n'est pas décorative : une correction qui n'aurait déplacé le problème que d'un
    /// tuyau à l'autre passerait l'épreuve précédente et échouerait ici.
    /// </remarks>
    [Fact]
    public async Task AProgramFloodingItsStandardStreamIsReadToo()
    {
        using var processus = Bavard("");

        var fait = Task.Run(() => Tuyaux.Vider(processus));
        var gagnant = await Task.WhenAny(fait, Task.Delay(TimeSpan.FromSeconds(60)));

        Assert.True(gagnant == fait, "Vider ne rend pas la main.");
        Assert.Contains("ligne de remplissage", (await fait).Sortie, StringComparison.Ordinal);
    }

    /// <summary>La patience bornée rend la main même si le programme s'éternise.</summary>
    /// <remarks>
    /// Sans borne, un outil resté bloqué gèlerait l'appelant — c'est-à-dire l'interface. Ce qui a
    /// déjà été lu est rendu tout de même : c'est la seule trace de ce qui s'est passé, et la
    /// perdre reviendrait à diagnostiquer à l'aveugle.
    /// </remarks>
    [Fact]
    public void ABoundedPatienceGivesTheHandBack()
    {
        var depart = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        depart.ArgumentList.Add("/c");
        depart.ArgumentList.Add("ping -n 30 127.0.0.1 > nul");

        using var processus = Process.Start(depart)!;

        var montre = Stopwatch.StartNew();

        Tuyaux.Vider(processus, TimeSpan.FromSeconds(2));

        Assert.True(
            montre.Elapsed < TimeSpan.FromSeconds(20),
            $"Vider a attendu {montre.Elapsed.TotalSeconds:F0} s malgré une patience de 2 s.");

        try
        {
            processus.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
