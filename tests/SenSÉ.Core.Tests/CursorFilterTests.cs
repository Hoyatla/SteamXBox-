using SenSÉ.Core.Osk;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The overlay cursor has to sit still under a resting finger and still keep up with a real
/// movement. A fixed-coefficient average cannot do both, which is why these two properties are
/// asserted together: making one pass by weakening the filter breaks the other.
/// </summary>
/// <remarks>
/// <b>Le temps est donne, pas attendu.</b> Le filtre s'adapte a la vitesse, donc a l'intervalle
/// entre deux echantillons ; ces tests espacaient les leurs avec <c>Thread.Sleep(8)</c>. Sous
/// charge, huit millisecondes en deviennent quarante : l'intervalle mesure change, le filtre — qui
/// n'a rien de casse — rend un autre resultat, et le test mesure l'occupation de la machine plutot
/// que le lissage. La cadence est maintenant ecrite noir sur blanc, et la serie ne dort plus.
/// </remarks>
public class CursorFilterTests
{
    /// <summary>La cadence du pave tactile, huit millisecondes par echantillon.</summary>
    /// <remarks>
    /// C'est la valeur que les <c>Thread.Sleep(8)</c> visaient. Elle est ici une constante du test
    /// au lieu d'un souhait adresse a l'ordonnanceur.
    /// </remarks>
    private const int FrameMs = 8;

    /// <summary>Une horloge que le test avance lui-meme.</summary>
    private sealed class Metronome
    {
        private int _tick;

        /// <param name="start">
        /// Depart arbitraire mais non nul : <see cref="Environment.TickCount"/> ne commence jamais a
        /// zero en production, et un filtre qui en dependrait doit echouer ici.
        /// </param>
        public Metronome(int start = 1_234_567) => _tick = start;

        public int Now => _tick;

        public void Advance(int milliseconds) => _tick += milliseconds;
    }

    /// <summary>Feeds samples one frame apart, on a clock the test advances itself.</summary>
    private static void Feed(CursorFilter filter, Metronome clock, IEnumerable<(double X, double Y)> samples)
    {
        foreach (var (x, y) in samples)
        {
            filter.Update(x, y);
            clock.Advance(FrameMs);
        }
    }

    private static (CursorFilter Filter, Metronome Clock) New(double smoothing)
    {
        var clock = new Metronome();

        return (new CursorFilter(smoothing, () => clock.Now), clock);
    }

    [Fact]
    public void FirstSampleIsTakenAsIs()
    {
        var (filter, _) = New(0.5);
        filter.Update(120, 80);

        Assert.Equal(120, filter.X, 3);
        Assert.Equal(80, filter.Y, 3);
    }

    /// <summary>
    /// A finger held still still moves the reported contact centroid by a pixel or so. That must not
    /// reach the cursor at all.
    /// </summary>
    [Fact]
    public void RestingFingerDoesNotMoveTheCursor()
    {
        var (filter, clock) = New(0.5);
        filter.Update(200, 100);
        clock.Advance(FrameMs);

        var jitter = new[]
        {
            (200.6, 100.4), (199.5, 99.7), (200.8, 100.9), (199.4, 99.3),
            (200.5, 100.6), (199.6, 99.5), (200.7, 100.2), (199.3, 99.8),
        };
        Feed(filter, clock, jitter);

        Assert.True(Math.Abs(filter.X - 200) < 0.5, $"X drifted to {filter.X}");
        Assert.True(Math.Abs(filter.Y - 100) < 0.5, $"Y drifted to {filter.Y}");
    }

    /// <summary>
    /// A deliberate sweep must arrive. Filtering hard enough to kill jitter is worthless if the
    /// cursor then trails the finger across the keyboard.
    /// </summary>
    [Fact]
    public void DeliberateMovementIsFollowed()
    {
        var (filter, clock) = New(0.5);
        filter.Update(0, 0);
        clock.Advance(FrameMs);

        var sweep = Enumerable.Range(1, 25).Select(i => (X: i * 20.0, Y: 0.0));
        Feed(filter, clock, sweep);

        // Within a fifth of the travelled distance of the target: responsive, not merely eventual.
        Assert.True(filter.X > 400, $"cursor only reached {filter.X} of 500");
    }

    [Fact]
    public void ResetTakesTheNextSampleAsIs()
    {
        var (filter, clock) = New(0.5);
        filter.Update(10, 10);
        clock.Advance(FrameMs);
        Feed(filter, clock, [(12.0, 12.0), (14.0, 14.0)]);

        filter.Reset();
        filter.Update(900, 500);

        Assert.Equal(900, filter.X, 3);
        Assert.Equal(500, filter.Y, 3);
    }

    /// <summary>
    /// The setting scales how hard a resting finger is filtered. It must stay bounded: a zero or
    /// negative value used to be possible through the settings file and would divide by zero.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    [InlineData(50.0)]
    public void ExtremeSmoothingSettingsStayFinite(double smoothing)
    {
        var (filter, clock) = New(smoothing);
        filter.Update(50, 50);
        clock.Advance(FrameMs);
        Feed(filter, clock, [(60.0, 60.0), (70.0, 70.0), (80.0, 80.0)]);

        Assert.True(double.IsFinite(filter.X));
        Assert.True(double.IsFinite(filter.Y));
    }

    /// <summary>
    /// Une trame gelee ne doit pas faire diverger le filtre.
    /// </summary>
    /// <remarks>
    /// Ce que les <c>Thread.Sleep</c> rencontraient par accident sous charge, et que personne
    /// n'ecrivait : au-dela d'un quart de seconde entre deux echantillons, le filtre retombe sur un
    /// intervalle suppose plutot que de diviser par un temps qui n'a plus de sens. C'est desormais
    /// un cas nomme et non un aleas de l'ordonnanceur.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1_000)]
    [InlineData(60_000)]
    public void AStalledFrameDoesNotThrowTheCursorAcrossTheScreen(int stallMs)
    {
        var (filter, clock) = New(0.5);
        filter.Update(300, 200);
        clock.Advance(stallMs);
        filter.Update(302, 202);

        Assert.True(double.IsFinite(filter.X));
        Assert.True(double.IsFinite(filter.Y));

        // Deux pixels demandes, jamais davantage rendus, quel que soit le trou dans les trames.
        Assert.True(Math.Abs(filter.X - 300) <= 2.0, $"X sauta a {filter.X}");
        Assert.True(Math.Abs(filter.Y - 200) <= 2.0, $"Y sauta a {filter.Y}");
    }

    /// <summary>
    /// Le compteur de millisecondes de Windows repasse par zero tous les quarante-neuf jours.
    /// </summary>
    /// <remarks>
    /// La soustraction dans le filtre est ecrite pour survivre a ce repli, et rien ne le verifiait :
    /// une horloge reelle n'y arrive pas pendant une serie de tests. Avec une horloge donnee, c'est
    /// une ligne.
    /// </remarks>
    [Fact]
    public void SurvivesTheTickCountWraparound()
    {
        var clock = new Metronome(int.MaxValue - (FrameMs / 2));
        var filter = new CursorFilter(0.5, () => clock.Now);

        filter.Update(400, 300);
        clock.Advance(FrameMs);
        filter.Update(460, 300);

        Assert.True(clock.Now < 0, "l'horloge n'a pas franchi le repli, le test ne prouve rien");
        Assert.True(double.IsFinite(filter.X));
        Assert.True(filter.X > 400, $"le curseur n'a pas suivi apres le repli : {filter.X}");
    }
}
