using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// La veille qui prévient quand les dossiers d'outils changent.
/// </summary>
/// <remarks>
/// Elle remplace « au prochain démarrage ». Ce qui est éprouvé n'est pas seulement qu'elle prévient,
/// mais qu'elle ne prévient <b>qu'une fois</b> : un installeur écrit des milliers de fichiers, et
/// reconstruire la grille à chacun la ferait clignoter pendant toute l'installation.
/// </remarks>
public class VeilleOutilsTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("veille");

    public VeilleOutilsTests()
    {
        Directory.CreateDirectory(Path.Combine(_bac.FullName, "Plugins"));
        Directory.CreateDirectory(Path.Combine(_bac.FullName, "Outils"));
    }

    public void Dispose()
    {
        _bac.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Attend que la veille prévienne, ou renonce.</summary>
    private static bool Attendre(ManualResetEventSlim dit)
        => dit.Wait(TimeSpan.FromMilliseconds(VeilleOutils.CalmeMillisecondes * 6));

    /// <summary>Un dossier déposé dans Outils fait prévenir.</summary>
    [Fact]
    public void AFolderDroppedIntoToolsRaisesTheAlarm()
    {
        using var veille = new VeilleOutils(_bac.FullName);
        using var dit = new ManualResetEventSlim(false);

        veille.Change += dit.Set;

        Directory.CreateDirectory(Path.Combine(_bac.FullName, "Outils", "UnOutil"));

        Assert.True(Attendre(dit), "la veille aurait dû prévenir");
    }

    /// <summary>Un manifeste déposé dans Plugins aussi.</summary>
    /// <remarks>
    /// Les deux dossiers comptent pour des raisons différentes : Plugins porte les tuiles, Outils
    /// porte ce qu'elles promettent. Un outil dont le corps disparaît garde sinon une tuile qui ne
    /// mène nulle part.
    /// </remarks>
    [Fact]
    public void AManifestDroppedIntoPluginsDoesToo()
    {
        using var veille = new VeilleOutils(_bac.FullName);
        using var dit = new ManualResetEventSlim(false);

        veille.Change += dit.Set;

        var dossier = Path.Combine(_bac.FullName, "Plugins", "un-outil");

        Directory.CreateDirectory(dossier);
        File.WriteAllText(Path.Combine(dossier, "plugin.json"), "{}");

        Assert.True(Attendre(dit), "la veille aurait dû prévenir");
    }

    /// <summary>
    /// Mille fichiers d'un coup ne préviennent qu'une fois.
    /// </summary>
    /// <remarks>
    /// C'est la raison d'être du délai. Sans lui, une installation de cinq cents mégaoctets
    /// reconstruirait la grille des milliers de fois et mangerait le fil d'affichage pendant qu'elle
    /// dure.
    /// </remarks>
    [Fact]
    public void AThousandFilesAtOnceRaiseTheAlarmOnlyOnce()
    {
        using var veille = new VeilleOutils(_bac.FullName);
        using var dit = new ManualResetEventSlim(false);

        var fois = 0;

        veille.Change += () =>
        {
            Interlocked.Increment(ref fois);
            dit.Set();
        };

        var dossier = Path.Combine(_bac.FullName, "Outils", "GrosOutil");

        Directory.CreateDirectory(dossier);

        for (var i = 0; i < 1000; i++)
        {
            File.WriteAllText(Path.Combine(dossier, $"f{i}.bin"), "x");
        }

        Assert.True(Attendre(dit), "la veille aurait dû prévenir");

        // Laisse passer un second délai : s'il y avait une deuxième annonce en route, elle arrive là.
        Thread.Sleep(VeilleOutils.CalmeMillisecondes * 2);

        Assert.Equal(1, Volatile.Read(ref fois));
    }

    /// <summary>Un dossier absent n'est pas une panne.</summary>
    /// <remarks>
    /// <c>Outils</c> n'existe pas sur une installation qui n'a encore rien accueilli, et la fenêtre
    /// doit s'ouvrir quand même.
    /// </remarks>
    [Fact]
    public void AnAbsentFolderIsNotAFailure()
    {
        using var veille = new VeilleOutils(Path.Combine(_bac.FullName, "nulle-part"));

        Assert.NotNull(veille);
    }

    /// <summary>Une fois arrêtée, elle ne prévient plus.</summary>
    /// <remarks>
    /// La fenêtre fermée, une annonce qui arrive encore toucherait une collection dont plus personne
    /// ne veut — et le ferait depuis un autre fil que celui de l'affichage.
    /// </remarks>
    [Fact]
    public void OnceStoppedItSaysNothingMore()
    {
        var veille = new VeilleOutils(_bac.FullName);
        var fois = 0;

        veille.Change += () => Interlocked.Increment(ref fois);
        veille.Dispose();

        Directory.CreateDirectory(Path.Combine(_bac.FullName, "Outils", "ApresCoup"));
        Thread.Sleep(VeilleOutils.CalmeMillisecondes * 2);

        Assert.Equal(0, Volatile.Read(ref fois));
    }
}
