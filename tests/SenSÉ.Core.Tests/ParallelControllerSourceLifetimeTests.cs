using SenSÉ.Core.Input;
using SenSÉ.Core.Runtime;
using SenSÉ.Windows;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// That letting go of a controller reader actually stops it.
/// </summary>
/// <remarks>
/// <b>The bug these were written for.</b> Everything inside the reader ran on the token its caller
/// passed, which is the session's. So a reader the caller had finished with kept its arrivals
/// watcher alive until the whole session ended — rescanning every three seconds, opening devices,
/// and announcing controllers beside its own replacement.
///
/// <para>
/// The loop that waits for a pad to come back builds a new reader each time round and released none
/// of them. Two naps meant three watchers on three independent beats, which is why the log showed
/// the same controller arriving twice eight tenths of a second apart — from a loop that waits three
/// seconds and therefore cannot do that on its own.
/// </para>
///
/// <para>
/// It is tested here rather than watched for in a log because it is invisible until it is measured:
/// nothing crashes, nothing is lost, the machine simply does the same work several times over and
/// says so in a file nobody reads to the end.
/// </para>
///
/// <para>
/// <b>Le battement est donne, pas attendu.</b> Ces tests ont d'abord dormi trois cents millisecondes
/// et compte les rescans de part et d'autre, en tolerant un battement de trop apres l'arret — parce
/// que sous charge la machine en glisse un. Cette tolerance est exactement l'espace ou une
/// regression tient, et le reste du test mesurait l'occupation de la machine autant que le veilleur.
/// Le battement du veilleur est desormais fourni par le test, qui le pilote coup par coup : les
/// comptes sont exacts, l'arret est constate et non suppose, et rien ne dort.
/// </para>
/// </remarks>
public class ParallelControllerSourceLifetimeTests
{
    /// <summary>
    /// De quoi echouer plutot que de rester pendu, si quelque chose ne repond jamais.
    /// </summary>
    /// <remarks>
    /// Ce n'est pas une hypothese de duree : rien ici n'attend ces dix secondes en marche normale.
    /// C'est la difference entre un test qui echoue en le disant et une serie qui se fige.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Le battement du veilleur, tenu par le test.</summary>
    /// <remarks>
    /// Le veilleur boucle sur « relever ce qui est connu, attendre un battement, rescanner ». En
    /// remplacant l'attente, le test sait ou le veilleur se trouve : arrete au rendez-vous, ou en
    /// train de faire son tour. Il peut donc lui accorder un tour et rendre la main quand ce tour
    /// est fini — au lieu de dormir et d'esperer.
    /// </remarks>
    private sealed class Beat
    {
        /// <summary>Le veilleur signale qu'il est arrive au rendez-vous.</summary>
        private readonly SemaphoreSlim _arrived = new(0);

        /// <summary>Le test le laisse repartir.</summary>
        private readonly SemaphoreSlim _released = new(0);

        private readonly TaskCompletionSource _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Se termine quand le veilleur a quitte le rendez-vous pour de bon.</summary>
        /// <remarks>
        /// C'est ce qui remplace « dormir puis constater que le compte n'a pas bouge ». Le veilleur
        /// attend son battement sur le jeton de la source ; quand celui-ci est annule, l'attente
        /// leve, la boucle sort, et cette promesse se resout. Un compte releve apres ca est
        /// definitif, pas un instantane.
        /// </remarks>
        public Task Stopped => _stopped.Task;

        /// <summary>A passer en <c>beat</c> a la source.</summary>
        public async Task Wait(TimeSpan _, CancellationToken cancellationToken)
        {
            _arrived.Release();

            try
            {
                await _released.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _stopped.TrySetResult();
                throw;
            }
        }

        /// <summary>
        /// Accorde un tour au veilleur et rend la main quand ce tour est termine.
        /// </summary>
        /// <remarks>
        /// Le retour au rendez-vous suivant est la preuve que le tour precedent est alle jusqu'au
        /// bout, rescan compris. C'est ce qui permet d'affirmer « exactement deux rescans » plutot
        /// que « au moins un, probablement ».
        /// </remarks>
        public async Task GrantOneRoundAsync()
        {
            using var deadline = new CancellationTokenSource(Patience);

            await _arrived.WaitAsync(deadline.Token);
            _released.Release();

            // Il revient : son tour est fini. Le jeton est repose pour l'appel suivant.
            await _arrived.WaitAsync(deadline.Token);
            _arrived.Release();
        }
    }

    /// <summary>A controller that never sends anything and never ends by itself.</summary>
    private sealed class SilentSource : IPhysicalControllerSource
    {
        public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A controller that sends one frame and then goes quiet for ever.
    /// </summary>
    /// <remarks>
    /// Needed so that an enumeration can be entered and left. With a source that never sends
    /// anything, <c>await foreach</c> waits on the first item and the <c>break</c> below is never
    /// reached — which is how the first version of the ordering test hung instead of failing.
    /// </remarks>
    private sealed class OneFrameSource : IPhysicalControllerSource
    {
        public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ControllerState();

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Une manette dont c'est le test qui decide quand elle s'en va.
    /// </summary>
    /// <remarks>
    /// Le jeton d'annulation est ignore volontairement. Une source qui s'arrete a l'annulation
    /// s'arrete quand la liberation commence, c'est-a-dire a un moment que le test ne choisit pas ;
    /// celle-ci s'arrete sur ordre, ce qui permet de placer un depart exactement au milieu d'une
    /// liberation.
    /// </remarks>
    private sealed class SourceOnCommand(bool speaks) : IPhysicalControllerSource
    {
        private readonly TaskCompletionSource _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        /// <summary>Ce que cette source fait pendant qu'on la libere.</summary>
        public Func<Task>? WhileDisposing { get; set; }

        /// <summary>Termine le flux, comme une manette qu'on eteint.</summary>
        public void End() => _end.TrySetResult();

        public async IAsyncEnumerable<ControllerState> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Une trame pour celle qui parle : c'est ce qui dit au test que la lecture a commence.
            if (speaks)
            {
                yield return new ControllerState();
            }

            await _end.Task.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            Disposed = true;

            if (WhileDisposing is { } hook)
            {
                await hook().ConfigureAwait(false);
            }
        }
    }

    private static (ControllerIdentity, IPhysicalControllerSource) Pad(string id)
        => (new ControllerIdentity(ControllerKind.XInput, id, id, Slot: 0), new SilentSource());

    private static (ControllerIdentity, IPhysicalControllerSource) Talking(string id)
        => (new ControllerIdentity(ControllerKind.XInput, id, id, Slot: 0), new OneFrameSource());

    /// <summary>Une source dont on tient le battement et dont on compte les rescans.</summary>
    private static (ParallelControllerSource Source, Beat Beat, Func<int> Rescans) Watched(
        (ControllerIdentity, IPhysicalControllerSource) child)
    {
        var beat = new Beat();
        var rescans = 0;

        var source = new ParallelControllerSource(
            [child],
            rescan: () =>
            {
                Interlocked.Increment(ref rescans);
                return [Pad("one")];
            },
            rescanInterval: TimeSpan.FromMilliseconds(40),
            beat: beat.Wait);

        return (source, beat, () => Volatile.Read(ref rescans));
    }

    /// <summary>Deux tours accordes, deux rescans : ni plus, ni moins.</summary>
    private const int Rounds = 2;

    // The heart of it: after DisposeAsync, the rescan must stop being called.
    [Fact]
    public async Task DisposingStopsTheArrivalsWatcher()
    {
        var (source, beat, rescans) = Watched(Pad("one"));

        using var session = new CancellationTokenSource();

        // Drained on a task of its own: the stream never ends by itself, which is the point.
        var reading = Task.Run(async () =>
        {
            await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
            }
        });

        for (var round = 0; round < Rounds; round++)
        {
            await beat.GrantOneRoundAsync();
        }

        Assert.Equal(Rounds, rescans());

        await source.DisposeAsync();

        // Constate, et non suppose : le veilleur a quitte son rendez-vous.
        await beat.Stopped.WaitAsync(Patience);

        Assert.Equal(Rounds, rescans());

        session.Cancel();
        await Settle(reading);
    }

    /// <summary>
    /// The order the product actually uses: stop reading first, dispose second.
    /// </summary>
    /// <remarks>
    /// <b>The test above passes without catching this.</b> It disposes while the enumeration is
    /// still running, so the link between the caller's life and this source's is still alive to
    /// carry the cancellation. The product does the opposite: it breaks out of the frame loop — the
    /// iterator ends, and with it anything the iterator owned — and only then releases the source.
    ///
    /// <para>
    /// A linked cancellation source that has been disposed forwards nothing. So the first fix put
    /// the link in a <c>using</c> inside the enumerator, the enumerator disposed it on the way out,
    /// and cancelling afterwards reached nobody. Measured in the product: SenSÉ handed the
    /// controller to Steam and released it from HidHide, and six tenths of a second later the
    /// orphaned watcher hid it again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StoppingTheEnumerationThenDisposingAlsoStopsTheWatcher()
    {
        var (source, beat, rescans) = Watched(Talking("one"));

        using var session = new CancellationTokenSource();

        // Entered and left, exactly as the frame loop leaves it: one frame in, then break.
        await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
        {
            break;
        }

        for (var round = 0; round < Rounds; round++)
        {
            await beat.GrantOneRoundAsync();
        }

        Assert.Equal(Rounds, rescans());

        await source.DisposeAsync();
        await beat.Stopped.WaitAsync(Patience);

        Assert.Equal(Rounds, rescans());
    }

    // The caller's token must still work on its own: disposal is an addition, not a replacement.
    [Fact]
    public async Task CancellingTheCallerStopsTheWatcherToo()
    {
        var (source, beat, rescans) = Watched(Pad("one"));

        await using var owned = source;

        using var session = new CancellationTokenSource();

        var reading = Task.Run(async () =>
        {
            await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
            }
        });

        for (var round = 0; round < Rounds; round++)
        {
            await beat.GrantOneRoundAsync();
        }

        Assert.Equal(Rounds, rescans());

        session.Cancel();
        await beat.Stopped.WaitAsync(Patience);

        Assert.Equal(Rounds, rescans());

        await Settle(reading);
    }

    /// <summary>
    /// Une manette qui s'en va pendant la liberation ne doit pas interrompre celle-ci.
    /// </summary>
    /// <remarks>
    /// <b>Le defaut que ceci corrige.</b> <c>DisposeAsync</c> annule d'abord, ce qui met fin aux
    /// lecteurs ; un lecteur qui se termine retire sa manette de la liste depuis sa propre tache.
    /// Puis <c>DisposeAsync</c> parcourait cette meme liste sans verrou. Une manette qui s'eteint au
    /// mauvais dixieme de seconde faisait donc lever « Collection was modified » au milieu de la
    /// liberation, et les manettes suivantes n'etaient jamais fermees — jamais rendues a HidHide,
    /// c'est-a-dire invisibles pour Steam, ce que tout ce fichier existe pour empecher.
    ///
    /// <para>
    /// Attrape par hasard, une fois sur soixante executions, une fois les tests ci-dessus rendus
    /// deterministes : leur ancienne version dormait trois cents millisecondes apres la liberation
    /// et le depart avait le temps d'avoir eu lieu avant. Rendu certain ici plutot que laisse au
    /// hasard : c'est la liberation de la premiere manette qui declenche le depart de la seconde et
    /// qui attend qu'il soit enregistre, donc la collision a lieu a chaque fois.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AControllerLeavingDuringDisposalDoesNotInterruptIt()
    {
        var departed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // La premiere ne s'en va jamais d'elle-meme : elle doit encore etre dans la liste au moment
        // ou celle-ci est parcourue.
        var staying = new SourceOnCommand(speaks: false);
        var leaving = new SourceOnCommand(speaks: true);

        await using var source = new ParallelControllerSource(
            [
                (new ControllerIdentity(ControllerKind.XInput, "premiere", "premiere", Slot: 0), staying),
                (new ControllerIdentity(ControllerKind.XInput, "seconde", "seconde", Slot: 0), leaving),
            ],
            log: line =>
            {
                if (line.Contains("controller left", StringComparison.Ordinal)
                    && line.Contains("seconde", StringComparison.Ordinal))
                {
                    departed.TrySetResult();
                }
            });

        staying.WhileDisposing = async () =>
        {
            leaving.End();
            await departed.Task.WaitAsync(Patience);
        };

        using var session = new CancellationTokenSource();

        var reachedTheStream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = Task.Run(async () =>
        {
            await foreach (var _ in source.ReadAllAsync(session.Token).ConfigureAwait(false))
            {
                reachedTheStream.TrySetResult();
            }
        });

        // La trame de la seconde manette prouve que les lecteurs tournent. Sans ca, la liberation
        // pourrait arriver avant eux et le depart n'aurait rien a interrompre.
        await reachedTheStream.Task.WaitAsync(Patience);

        // L'assertion est qu'elle se termine. Avant, elle levait.
        await source.DisposeAsync();

        Assert.True(departed.Task.IsCompletedSuccessfully, "la seconde manette n'est pas partie, le test ne prouve rien");
        Assert.True(staying.Disposed, "la premiere manette n'a pas ete liberee");
        Assert.True(leaving.Disposed, "la seconde manette n'a pas ete liberee");

        staying.End();
        session.Cancel();
        await Settle(reading);
    }

    /// <summary>
    /// Attend la fin de la lecture, dont l'annulation est la fin normale.
    /// </summary>
    /// <remarks>
    /// Le delai n'est pas une tolerance : c'est de quoi echouer si la lecture ne s'arrete jamais,
    /// au lieu de figer la serie sur un <c>await</c> sans fin.
    /// </remarks>
    private static async Task Settle(Task reading)
    {
        try
        {
            await reading.WaitAsync(Patience);
        }
        catch (OperationCanceledException)
        {
            // C'est la facon dont une enumeration annulee se termine.
        }
    }
}
