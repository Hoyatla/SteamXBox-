using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SteamXBox.Desktop.Assets;
using SteamXBox.Tools.Search;

namespace SteamXBox.Desktop.Search;

/// <summary>
/// The launcher summoned by tapping Shift twice.
/// </summary>
/// <remarks>
/// Held for the life of the session rather than built on each summon: the index is the expensive
/// part and rebuilding it per keystroke of the gesture would make the window take seconds to appear.
/// It is hidden, not closed.
/// </remarks>
public partial class SearchLauncherWindow : Window
{
    /// <summary>Never more than this on screen. Beyond it nobody is reading, and every row costs.</summary>
    private const int MaxResults = 40;

    private readonly Action<string>? _log;

    /// <summary>Everything on disk. Built once: walking it again would take seconds.</summary>
    private IReadOnlyList<SearchItem> _files = [];

    /// <summary>
    /// The volumes, kept apart because they change while the session runs.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="_files"/> so a drive plugged in can be added without rebuilding
    /// anything else. Merged into the search at the moment of the query, which costs a handful of
    /// entries and saves the only part of the index that goes stale.
    /// </remarks>
    private IReadOnlyList<SearchItem> _volumes = [];

    private bool _indexing;

    /// <summary>This person's own mail, built on demand and held on this machine.</summary>
    /// <remarks>
    /// Built the first time <c>mail:</c> is typed, not with the file index. Reading a mailbox is not
    /// something to do because a launcher happened to open: somebody who never types the prefix
    /// should find that SteamXBox never went near their correspondence.
    /// </remarks>
    private MailIndex _mail = MailIndex.Empty;

    private bool _mailIndexing;
    private bool _mailAsked;

    /// <summary>What the user opens, which nudges the ranking towards it next time.</summary>
    private readonly LaunchHistory _history;

    /// <summary>Where web queries go, and what an administrator has fixed about it.</summary>
    private readonly ResolvedWebSearch _web;

    /// <summary>The institution's own corpus, and what an administrator fixed about it.</summary>
    private readonly ResolvedDocs _docs;

    public SearchLauncherWindow(Action<string>? log = null)
    {
        InitializeComponent();
        _log = log;
        _history = LaunchHistoryStore.Load(log);
        var configuration = SearchPolicyStore.Load(log);
        _web = configuration.Web;
        _docs = configuration.Docs;

        // Hidden as soon as it loses the foreground. A launcher that stays after the user has
        // clicked elsewhere is a window they have to dismiss, which defeats a shortcut meant to be
        // faster than the mouse.
        Deactivated += (_, _) => Dismiss();

        // The caret is asked for once the window is active, and not before, because WPF decides
        // where focus goes as part of handling the activation and would undo an earlier request.
        //
        // Which is what made the very first summon, and only the first, arrive without a caret: a
        // window that has already been shown has a remembered focused element, so activation puts
        // the caret straight back into the field; a window shown for the first time has none, so
        // activation focuses the window itself and the request made just before it is lost. The
        // user then has to click in the field — one wasted click, on the one use where a first
        // impression is being formed.
        Activated += (_, _) => FocusQuery();

        // The handle now, without showing anything. Windows broadcasts device changes to top-level
        // windows, and a window that has no handle until its first summon would miss every drive
        // plugged in before then.
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).EnsureHandle());
        source?.AddHook(OnWindowMessage);

        RefreshVolumes();
    }

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

    /// <summary>
    /// Notices a drive arriving or leaving, and takes it into account at once.
    /// </summary>
    /// <remarks>
    /// Volume events are broadcast to every top-level window without anything having to register for
    /// them, so listening costs one message test. Both directions matter: a key removed must stop
    /// being offered, or the launcher opens a letter that is no longer anything.
    /// </remarks>
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_DEVICECHANGE)
        {
            return IntPtr.Zero;
        }

        var change = wParam.ToInt32();

        if (change is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
        {
            // Posted rather than done here. The broadcast is synchronous and every window on the
            // machine is waiting behind ours; listing volumes touches the drives and can block on one
            // that is spinning up.
            Dispatcher.BeginInvoke(() =>
            {
                RefreshVolumes();

                if (IsVisible)
                {
                    // The strip and the results both, because the user may already be typing the
                    // name of the key they have just plugged in.
                    ShowVolumes();
                    Refresh();
                }
            });
        }

        return IntPtr.Zero;
    }

    /// <summary>Re-reads the volumes into the searchable set.</summary>
    private void RefreshVolumes()
    {
        try
        {
            _volumes = Volumes.AsSearchItems(_log);
        }
        catch (Exception exception)
        {
            _log?.Invoke($"search: refreshing volumes failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>Builds the index once, off the interface thread.</summary>
    /// <remarks>
    /// Started at the first summon rather than at startup: an environment that walks every drive
    /// while the user is trying to launch a game is worse than a launcher that is slow the first
    /// time it is asked for.
    /// </remarks>
    public void EnsureIndex()
    {
        if (_indexing || _files.Count > 0)
        {
            return;
        }

        _indexing = true;
        Status.Text = "Indexation…";

        Task.Run(() => SearchIndexBuilder.Build(_log)).ContinueWith(built =>
        {
            _files = built.IsCompletedSuccessfully ? built.Result : [];
            _indexing = false;

            if (!built.IsCompletedSuccessfully)
            {
                _log?.Invoke($"search index failed: {built.Exception?.GetBaseException().Message}");
            }

            Status.Text = "";
            Refresh();

            // The window was summoned to be typed into, and building the index took seconds during
            // which anything could have taken the foreground. Asking again costs nothing and is the
            // difference between a launcher that is ready when the wait ends and one that quietly
            // is not.
            TakeForeground();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Builds the mail index once, off the interface thread.
    /// </summary>
    /// <remarks>
    /// Started by the prefix, and only by the prefix. A saved index is loaded first so the wait
    /// happens once ever rather than once per session.
    /// </remarks>
    private void EnsureMailIndex()
    {
        if (_mailIndexing || _mailAsked)
        {
            return;
        }

        _mailAsked = true;
        _mail = MailIndex.Load(_log);

        if (_mail.Count > 0)
        {
            return;
        }

        if (!MailIndexBuilder.AnythingToIndex())
        {
            return;
        }

        _mailIndexing = true;
        Status.Text = "Indexation du courrier…";

        Task.Run(() => MailIndexBuilder.Build(_log)).ContinueWith(built =>
        {
            _mail = built.IsCompletedSuccessfully ? built.Result : MailIndex.Empty;
            _mailIndexing = false;

            if (built.IsCompletedSuccessfully)
            {
                _mail.Save(_log);
            }
            else
            {
                _log?.Invoke($"mail index failed: {built.Exception?.GetBaseException().Message}");
            }

            Status.Text = "";
            Refresh();
            TakeForeground();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Puts the window back in front and the caret back in the field.</summary>
    /// <remarks>
    /// Only while it is still up. A window the user has already dismissed must not claw its way back
    /// because a background task finished.
    /// </remarks>
    private void TakeForeground()
    {
        if (!IsVisible)
        {
            return;
        }

        Activate();
        WindowForeground.Take(this);
        FocusQuery();
    }

    /// <summary>Shows the launcher, or hides it if it is already up.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Dismiss();
            return;
        }

        EnsureIndex();

        Query.Text = "";
        PlaceOnActiveScreen();
        Show();

        // Taken, not requested. Activate() calls SetForegroundWindow, which Windows refuses when the
        // caller is not already the foreground process — silently, returning as though it worked.
        // A launcher summoned by a global shortcut is always in that case, and the result is a window
        // on screen that swallows nothing: the user types and the letters go to whatever was in
        // front, which reads as the launcher being broken.
        Activate();
        WindowForeground.Take(this);
        ShowVolumes();

        // Asked for here as well as on activation, because a summon that finds the window already
        // active raises no activation at all. Both routes end in the same idempotent call.
        FocusQuery();
    }

    /// <summary>Puts the caret in the search field, ready to type.</summary>
    /// <remarks>
    /// Deferred to Input priority, and that is the whole of it. Focusing synchronously puts the
    /// caret in a window WPF has not finished arranging — the call reports success, no caret
    /// appears, and the user has to click in the field. Input priority runs after the layout pass
    /// and after WPF has settled focus for the activation, so the field is really there to take it.
    /// </remarks>
    private void FocusQuery() => Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Input,
        () =>
        {
            if (!IsVisible)
            {
                return;
            }

            Query.Focus();
            Keyboard.Focus(Query);
            Query.CaretIndex = Query.Text.Length;
        });

    private void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <remarks>
    /// On the screen holding the mouse, which is where the user is. Centred horizontally and a third
    /// of the way down, the position a launcher is expected at.
    /// </remarks>
    private void PlaceOnActiveScreen()
    {
        var area = PointerScreen.WorkArea();

        Left = area.X + ((area.Width - Width) / 2);
        Top = area.Y + (area.Height / 3.0);
    }

    /// <summary>The connected volumes, listed above the field.</summary>
    /// <remarks>
    /// Read at each summon rather than taken from the index. The index is built once per session and
    /// a stick plugged in between two searches would be missing from it — which is exactly the
    /// moment somebody looks at this strip to check whether the machine has seen their drive.
    ///
    /// <para>
    /// Deliberately quiet: dim text, small, no frame. It answers "is my drive there" at a glance and
    /// otherwise stays out of the way of the field, which is what the window is for.
    /// </para>
    /// </remarks>
    private void ShowVolumes()
    {
        try
        {
            VolumeStrip.ItemsSource = Volumes.All()
                .Select(volume => new VolumeChip(
                    $"{volume.Label} ({volume.Letter.TrimEnd('\\')})",
                    volume.Letter,
                    IconLibrary.Get(IconLibrary.Drive),
                    IconLibrary.Get(IconLibrary.Eject),

                    // Only what can be unplugged. An eject button next to the system disk is an
                    // offer nobody wants taken up.
                    volume.IsRemovable ? Visibility.Visible : Visibility.Collapsed))
                .ToList();
        }
        catch (Exception exception)
        {
            _log?.Invoke($"search: listing volumes failed: {exception.GetType().Name}: {exception.Message}");
            VolumeStrip.ItemsSource = null;
        }
    }

    private void Volume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string letter })
        {
            Dismiss();

            try
            {
                Process.Start(new ProcessStartInfo { FileName = letter, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                _log?.Invoke($"search: opening {letter} failed: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private void Eject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string letter })
        {
            return;
        }

        // Not dismissed. Ejecting is not leaving: the user may want to eject a second one, and
        // Windows may put a dialog on screen naming what still holds the volume.
        var said = Eject.Request(letter, _log);

        if (said.Length > 0)
        {
            Status.Text = said;
        }

        // The strip is rebuilt from the device-change broadcast when the volume really goes. Doing it
        // here as well would remove it from the list while Windows is still asking an application to
        // let go of it, and it would come back a second later.
    }

    /// <summary>One volume in the strip.</summary>
    private sealed record VolumeChip(
        string Label,
        string Letter,
        System.Windows.Media.Geometry? Icon,
        System.Windows.Media.Geometry? EjectIcon,
        Visibility EjectVisibility);

    private void Query_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    /// <summary>Ranks the index against what has been typed.</summary>
    /// <remarks>
    /// Nothing is shown before the first letter. A launcher that lists the whole machine on opening
    /// makes the user read instead of type, and the list is never what they wanted anyway.
    /// </remarks>
    private void Refresh()
    {
        var routed = QueryPrefix.Parse(Query.Text);

        Results.Items.Clear();

        // Emptied with the rows it belongs to. Left behind, a message from the previous query would
        // be read for a row that no longer means it.
        _mailResults = [];
        HidePreview();

        // Mail is local, so it behaves like local: results appear as the words are typed, rather
        // than waiting for Enter the way the web and the corpus do. Nothing leaves the machine, so
        // there is nothing to hold back for.
        if (routed.Route == QueryRoute.Mail)
        {
            EnsureMailIndex();
            ShowMailResults(routed.Rest);
            return;
        }

        // A routed query is not searched at all: there is nothing on this machine to rank against
        // "web: manettes ps5", and showing local guesses underneath would suggest the prefix had not
        // been understood.
        if (routed.Route != QueryRoute.Local)
        {
            Show(results: false);

            // Said plainly rather than silently doing nothing. Whatever an administrator turned off,
            // a user typing the prefix deserves to know it rather than think the tool broken.
            Status.Text = routed.Route switch
            {
                QueryRoute.Docs when !_docs.IsUsable =>
                    "La recherche documentaire n'est pas configurée.",
                QueryRoute.Docs when routed.Rest.Length == 0 =>
                    "Tapez les mots à chercher dans les documents.",
                QueryRoute.Docs =>
                    $"Entrée : chercher « {routed.Rest} » dans les documents.",

                _ when _web.Effective.Provider == WebSearchProvider.Disabled =>
                    "La recherche web est désactivée par l'administrateur.",
                _ when routed.Rest.Length == 0 =>
                    "Tapez les mots à chercher sur le web.",
                _ =>
                    $"Entrée : chercher « {routed.Rest} » sur le web.",
            };

            return;
        }

        var raw = routed.Rest;
        var pathMode = PathQuery.LooksLikeAPath(raw);
        var terms = SearchRanking.Terms(pathMode ? PathQuery.Expand(raw) : raw);

        if (terms.Count == 0)
        {
            Show(results: false);
            return;
        }

        var now = DateTime.UtcNow;

        // The volumes are searched alongside the files rather than being part of them: they are the
        // one thing that changes while the session runs, and keeping them apart is what lets a key
        // plugged in a moment ago be found without rebuilding anything.
        // The history is added after the ranking rather than inside it. What answers the query and
        // what the user tends to open are two different judgements, and keeping them apart means the
        // ranking stays testable without a history and the history can be cleared without touching
        // the ranking.
        var matches = _files.Concat(_volumes)
            .Select(item => (Item: item, Score: SearchRanking.Score(item, terms, pathMode, now)))
            .Where(pair => pair.Score.HasValue)
            .Select(pair => (pair.Item, Score: pair.Score!.Value + _history.Bonus(pair.Item.Path, now)))
            .OrderByDescending(pair => pair.Score)
            .Take(MaxResults);

        foreach (var (item, _) in matches)
        {
            Results.Items.Add(new Row(item));
        }

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        Show(results: Results.Items.Count > 0);

        Status.Text = Results.Items.Count == 0 && !_indexing ? "Aucun résultat." : "";

        // What the query produced, once per query rather than per keystroke of it. "It does not find
        // X" and "it finds X and opening it does nothing" are different faults with opposite fixes,
        // and until now the log could not tell them apart.
        _log?.Invoke(
            $"search '{string.Join(' ', terms)}': {Results.Items.Count} result(s)"
            + (Results.Items.Count > 0 ? $", first = {(Results.Items[0] as Row)?.Path}" : ""));

        StartLiveFolderSearch(terms, now);
    }

    /// <summary>Lists the messages matching what has been typed after <c>mail:</c>.</summary>
    /// <remarks>
    /// A message is shown by its subject with the sender underneath, because a mailbox is full of
    /// subjects that repeat — five messages called "Re: candidature" are told apart by who sent
    /// them, never by their file name, which is a hash.
    /// </remarks>
    private void ShowMailResults(string query)
    {
        if (query.Length == 0)
        {
            Show(results: false);

            Status.Text = _mailIndexing
                ? "Indexation du courrier…"
                : _mail.Count > 0
                    ? $"Tapez les mots à chercher dans {_mail.Count} message(s)."
                    : "Aucun courrier indexé sur cette machine.";

            return;
        }

        _mailResults = [.. _mail.Search(query)];

        foreach (var message in _mailResults)
        {
            Results.Items.Add(new Row(new SearchItem(
                message.Subject,
                message.Path,
                SearchItemKind.Mail,
                Priority: 0,
                LastWrite: DateTime.MinValue),
                Describe(message)));
        }

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        Show(results: Results.Items.Count > 0);

        Status.Text = Results.Items.Count > 0
            ? "Flèches : lire  ·  Ctrl+Entrée : ouvrir le dossier"
            : _mailIndexing ? "Indexation du courrier…" : "Aucun message.";

        // The first result is read straight away. A preview that waits for an arrow key is one the
        // user has to discover, and the top result is the one they were looking for most of the time.
        ShowPreview();

        _log?.Invoke($"mail search '{query}': {Results.Items.Count} result(s) of {_mail.Count} indexed.");
    }

    /// <summary>The line under a message's subject.</summary>
    private static string Describe(MailEntry message)
        => string.Join("  ·  ", new[] { message.From, message.Date }.Where(part => part.Length > 0));

    private CancellationTokenSource? _liveSearch;

    /// <summary>
    /// Walks the named drive for folders, alongside the indexed results.
    /// </summary>
    /// <remarks>
    /// Only when a term names a drive. Without one there is nothing to walk but every disk, on every
    /// keystroke — which is the cost the index was trimmed to avoid.
    ///
    /// <para>
    /// The previous walk is cancelled first, and that matters more than anything else here. A walk
    /// left running after its query has changed spends the machine's time on an answer nobody wants
    /// and then delivers it over the top of the current one. Typing eight letters would leave eight
    /// walks racing to overwrite the list.
    /// </para>
    /// </remarks>
    private void StartLiveFolderSearch(IReadOnlyList<string> terms, DateTime now)
    {
        _liveSearch?.Cancel();
        _liveSearch?.Dispose();
        _liveSearch = null;

        if (LiveFolderSearch.DriveNamedBy(terms) is not { } root)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _liveSearch = cancellation;

        // The query as it was when the walk started. Compared on arrival, because the user may have
        // typed on while the disk was being read.
        var forQuery = Query.Text;

        Task.Run(() => LiveFolderSearch.Find(root, terms, cancellation.Token), cancellation.Token)
            .ContinueWith(
                found =>
                {
                    if (cancellation.IsCancellationRequested
                        || !found.IsCompletedSuccessfully
                        || !string.Equals(forQuery, Query.Text, StringComparison.Ordinal))
                    {
                        return;
                    }

                    AddLiveResults(found.Result, now);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Adds what the walk found, without disturbing what is already on screen.</summary>
    /// <remarks>
    /// Appended rather than merged and re-sorted. The indexed results are already there and the user
    /// may be about to press Enter on the first one; reordering the list under their hand is how a
    /// launcher opens the wrong thing.
    /// </remarks>
    private void AddLiveResults(IReadOnlyList<SearchItem> found, DateTime now)
    {
        var known = Results.Items.OfType<Row>()
            .Select(row => row.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = found
            .Where(item => known.Add(item.Path))
            .OrderByDescending(item => _history.Bonus(item.Path, now))
            .Take(Math.Max(0, MaxResults - Results.Items.Count));

        foreach (var item in added)
        {
            Results.Items.Add(new Row(item));
        }

        if (Results.Items.Count == 0)
        {
            return;
        }

        if (Results.SelectedIndex < 0)
        {
            Results.SelectedIndex = 0;
        }

        Show(results: true);
        Status.Text = "";
    }

    private void Show(bool results)
    {
        var visibility = results ? Visibility.Visible : Visibility.Collapsed;

        Results.Visibility = visibility;
        Separator.Visibility = visibility;

        if (!results)
        {
            HidePreview();
        }
    }

    private void HidePreview()
    {
        PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewText.Text = "";
        _previewed = null;
    }

    /// <summary>What the panel is currently showing, so moving within a list does not re-read.</summary>
    private string? _previewed;

    /// <summary>
    /// Shows the selected message, read from where it actually lives.
    /// </summary>
    /// <remarks>
    /// Read on selection rather than kept in the index. A message in <c>Sent Items</c> is a slice of
    /// a three-hundred-megabyte file, and the index deliberately holds only the stripped, shortened
    /// form used for matching — which is the searchable text, not the readable one.
    ///
    /// <para>
    /// Read on the interface thread, which was measured before being decided. An ordinary message is
    /// a seek and two kilobytes; the worst case found on a real machine was twenty-one milliseconds,
    /// in a Sent Items whose eighty-eight messages average nearly four megabytes each because they
    /// carry attachments. That is under a frame at sixty hertz, and a task would trade it for a
    /// preview arriving after the user has already pressed the arrow key again.
    /// </para>
    /// </remarks>
    private void ShowPreview()
    {
        var selected = Results.SelectedIndex;

        if (selected < 0 || selected >= _mailResults.Count)
        {
            HidePreview();
            return;
        }

        var entry = _mailResults[selected];

        if (_previewed == entry.Key)
        {
            return;
        }

        var message = MailReader.Read(entry, _log);

        if (message is null)
        {
            HidePreview();
            return;
        }

        _previewed = entry.Key;
        PreviewText.Text = MailReader.ToPreview(message.Value);
        PreviewScroll.ScrollToTop();
        PreviewPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The messages currently listed, in the order the rows were added.
    /// </summary>
    /// <remarks>
    /// Parallel to the list rather than looked up by path, because a mailbox holds thousands of
    /// messages at one path and the path alone identifies none of them. Cleared whenever the results
    /// are rebuilt, so a stale entry can never be read for a row that has been replaced.
    /// </remarks>
    private List<MailEntry> _mailResults = [];

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;

            // Ctrl+Enter shows the entry in the file explorer instead of running it. The everyday
            // need behind it: a result that is a tray application does nothing visible when
            // launched, and a result that is a file is often wanted for its folder rather than for
            // itself.
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                RevealSelected();
                e.Handled = true;
                break;

            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;

            // Handled here rather than in the list, because the focus stays in the text field: the
            // user is still typing, and moving the focus to the list would stop the next letter from
            // reaching the query.
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (Results.Items.Count == 0)
        {
            return;
        }

        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
        Results.ScrollIntoView(Results.SelectedItem);

        // The arrow keys are how a message is read: the preview follows the selection rather than
        // waiting for a key of its own.
        ShowPreview();
    }

    private void Results_Activate(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        // A routed query has no selected row: Enter sends it where the prefix said.
        var routed = QueryPrefix.Parse(Query.Text);

        if (routed.Route == QueryRoute.Docs)
        {
            if (routed.Rest.Length > 0 && _docs.IsUsable)
            {
                _ = AskCorpusAsync(routed.Rest);
            }

            return;
        }

        if (routed.Route == QueryRoute.Web)
        {
            if (routed.Rest.Length == 0 || _web.Effective.Provider == WebSearchProvider.Disabled)
            {
                return;
            }

            // An instance answers into the window; the browser route leaves for it.
            if (_web.Effective.Provider == WebSearchProvider.Instance)
            {
                _ = AskInstanceAsync(routed.Rest);
                return;
            }

            Dismiss();
            Open(QueryPrefix.WebSearchUrl(routed.Rest), remember: false);

            return;
        }

        if (Results.SelectedItem is not Row row)
        {
            return;
        }

        // Hidden first. Opening takes a moment, and a launcher still on screen while the application
        // appears behind it reads as a launcher that failed.
        Dismiss();
        Open(row.Path, remember: true);
    }

    private CancellationTokenSource? _webQuery;

    /// <summary>
    /// Asks the instance and shows what it answers, inside the window.
    /// </summary>
    /// <remarks>
    /// On Enter, never as the user types. A request per keystroke would put eight queries on an
    /// institution's instance for one search — a classroom of thirty doing that at once is a denial
    /// of service by design, and the instance is theirs to pay for. The local index is searched as
    /// you type because it costs nothing; a network is not the same thing.
    /// </remarks>
    private async Task AskInstanceAsync(string keywords)
    {
        _webQuery?.Cancel();
        _webQuery?.Dispose();

        var cancellation = new CancellationTokenSource();
        _webQuery = cancellation;

        Results.Items.Clear();
        Show(results: false);
        Status.Text = "Recherche…";

        var answer = await SearxngClient.AskAsync(_web.Effective, keywords, cancellation.Token)
            .ConfigureAwait(true);

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        if (answer.Problem.Length > 0)
        {
            Status.Text = answer.Problem;
            _log?.Invoke($"web search failed: {answer.Problem}");
            return;
        }

        foreach (var result in answer.Results)
        {
            Results.Items.Add(new Row(new SearchItem(
                result.Title,
                result.Url,
                SearchItemKind.Web,
                Priority: 0,
                DateTime.UnixEpoch)));
        }

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        Show(results: Results.Items.Count > 0);
        Status.Text = Results.Items.Count == 0 ? "Aucun résultat." : "";

        _log?.Invoke($"web search '{keywords}': {answer.Results.Count} result(s).");
    }

    /// <summary>
    /// Asks the corpus and shows what it answers, inside the window.
    /// </summary>
    /// <remarks>
    /// On Enter, for the same reason as the web side: a request per keystroke would put eight queries
    /// on the institution's index for one search.
    ///
    /// <para>
    /// Results are documents on their file server, so they open and are remembered exactly like a
    /// local file — which is the point of putting them in this window rather than in a browser.
    /// </para>
    /// </remarks>
    private async Task AskCorpusAsync(string keywords)
    {
        _webQuery?.Cancel();
        _webQuery?.Dispose();

        var cancellation = new CancellationTokenSource();
        _webQuery = cancellation;

        Results.Items.Clear();
        Show(results: false);
        Status.Text = "Recherche dans les documents…";

        var answer = await MeilisearchClient.AskAsync(_docs.Effective, keywords, cancellation.Token)
            .ConfigureAwait(true);

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        if (answer.Problem.Length > 0)
        {
            Status.Text = answer.Problem;
            _log?.Invoke($"docs search failed: {answer.Problem}");
            return;
        }

        foreach (var result in answer.Results)
        {
            Results.Items.Add(new Row(new SearchItem(
                result.Title,
                result.Path,
                SearchItemKind.Document,
                Priority: 0,
                DateTime.UnixEpoch)));
        }

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }

        Show(results: Results.Items.Count > 0);
        Status.Text = Results.Items.Count == 0 ? "Aucun document." : "";

        _log?.Invoke($"docs search '{keywords}': {answer.Results.Count} result(s).");
    }

    /// <summary>Shows the selected entry in the file explorer, selected, instead of opening it.</summary>
    /// <remarks>
    /// <c>explorer /select</c> rather than opening the parent folder ourselves: it opens the folder
    /// <i>and</i> highlights the entry inside it, which is the difference between "here is a folder"
    /// and "here is your file". It also does the right thing for a folder — the parent opens with
    /// the folder picked — so there is no case to special-case.
    ///
    /// <para>
    /// A Store application has no path to reveal; its entry is an identifier. Saying so is better
    /// than opening an explorer window on nothing.
    /// </para>
    /// </remarks>
    private void RevealSelected()
    {
        if (Results.SelectedItem is not Row row)
        {
            return;
        }

        if (row.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            Status.Text = "Cette application n'a pas de dossier.";
            return;
        }

        Dismiss();

        if (ShellFolders.Reveal(row.Path, _log))
        {
            _log?.Invoke($"search: revealed {row.Path}.");
        }
    }

    /// <summary>Opens something, and remembers it if it was a result.</summary>
    /// <remarks>
    /// A web search is not remembered. The history exists to rank the machine's own entries, and a
    /// search address is neither an entry nor something that would ever be offered again.
    /// </remarks>
    private void Open(string target, bool remember)
    {
        try
        {
            // The shell first, for everything, and the explorer only if the shell refuses.
            //
            // Both orders have now been tried on a real machine, and each fixes what the other
            // breaks. Handing folders to explorer.exe by name was meant to solve one case: a cloud
            // placeholder folder — C:\Users\User\OneDrive — that ShellExecute could not classify and
            // rejected with "no application is associated with this file". It solved that and broke
            // every ordinary folder, because on this machine explorer.exe given a path opens nothing
            // whatsoever: it starts, reports a process id, and exits. The launcher logged a
            // successful launch each time and no window ever appeared.
            //
            // So the common route is the one that works for the common case, and the rare route is
            // kept for the rare one. A refusal is visible here — ShellExecute throws — which is what
            // makes the fallback possible at all.
            var isFolder = Directory.Exists(target);

            Process? started;

            try
            {
                started = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception) when (isFolder)
            {
                _log?.Invoke($"search: the shell would not open {target}; handing it to the explorer.");

                started = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{target}\"",
                    UseShellExecute = true,
                });
            }

            // Said on success as well as on failure. Only failures were logged, and that left the
            // two questions that actually get asked indistinguishable: did the entry appear at all,
            // and did opening it do anything? A tray application already running is the common case
            // — a second launch exits at once and nothing appears on screen, which reads exactly
            // like a launcher that ignored the key.
            _log?.Invoke(started is null
                ? $"search: opened {target} (handed to the shell; no new process)."
                : $"search: opened {target} (pid {started.Id}).");

            if (remember)
            {
                _history.Record(target, DateTime.UtcNow);
                LaunchHistoryStore.Save(_history, _log);
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"search: opening {target} failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>One line of the list.</summary>
    /// <remarks>
    /// SteamXBox's own icons rather than the file's real one. Extracting the real icon means asking
    /// the shell for every visible row, on the interface thread, while the user is typing — that is
    /// the part of the original that stutters. These come from the library, are cached and frozen,
    /// and cost nothing per row.
    ///
    /// <para>
    /// An executable and a script are told apart from an ordinary file, because they are what a
    /// launcher is usually asked for and the difference is worth seeing at a glance.
    /// </para>
    /// </remarks>
    private sealed record Row(SearchItem Item, string? Subtitle = null)
    {
        public string Name => Item.Name;

        public string Path => Item.Path;

        /// <summary>
        /// The second line: the path, unless the caller had something better to say.
        /// </summary>
        /// <remarks>
        /// A message is the case that needed it. Its file name is a hash, so the path says nothing;
        /// who sent it and when is what tells two messages with the same subject apart.
        /// </remarks>
        public string Detail => string.IsNullOrWhiteSpace(Subtitle) ? Path : Subtitle;

        public System.Windows.Media.Geometry? Icon => IconLibrary.Get(IconName);

        private string IconName => Item.Kind switch
        {
            SearchItemKind.Application => IconLibrary.Application,
            SearchItemKind.Folder => IconLibrary.Folder,
            SearchItemKind.Drive => IconLibrary.Drive,
            SearchItemKind.Web => IconLibrary.Web,
            SearchItemKind.Document => IconLibrary.Document,
            SearchItemKind.Mail => IconLibrary.Mail,
            _ when IsCode(Item.Path) => IconLibrary.Code,
            _ => IconLibrary.Document,
        };

        private static bool IsCode(string path)
            => System.IO.Path.GetExtension(path).ToLowerInvariant()
                is ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".msi";
    }
}
