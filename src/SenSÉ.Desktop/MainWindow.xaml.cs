using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SenSÉ.Desktop.ControlCentre;
using SenSÉ.Core.Diagnostics;
using SenSÉ.Shell.Localization;

namespace SenSÉ.Desktop;

/// <summary>
/// The SenSÉ Desktop window, holding the control centre.
/// </summary>
/// <remarks>
/// Fullscreen windowed: the environment fills the screen but stays an ordinary window, free to go
/// behind the others. It never activates itself (<c>ShowActivated="False"</c>), so typing keeps
/// going to whatever was in front. Every tile is focusable so the arrow keys — and the gamepad
/// mapped to them — walk the grid once the window has the focus; the mouse still works, it is
/// simply not what the layout is designed around.
/// </remarks>
public partial class MainWindow : Window
{
    public static readonly DependencyProperty SelectedHintProperty =
        DependencyProperty.Register(nameof(SelectedHint), typeof(string), typeof(MainWindow),
            new PropertyMetadata(string.Empty));

    /// <summary>Description of the tile under the pointer or the focus, shown under the title.</summary>
    public string SelectedHint
    {
        get => (string)GetValue(SelectedHintProperty);
        set => SetValue(SelectedHintProperty, value);
    }

    /// <summary>
    /// Les tuiles, dans une liste que l'écran suit au lieu de la lire une fois.
    /// </summary>
    /// <remarks>
    /// Une liste figée obligeait à redémarrer pour voir un outil qu'on venait de déposer. Celle-ci
    /// se remplit à nouveau quand la veille dit que les dossiers ont bougé, et l'écran suit parce
    /// que la collection prévient elle-même de ses changements — sans qu'on remplace la propriété,
    /// ce qui casserait la liaison.
    /// </remarks>
    public System.Collections.ObjectModel.ObservableCollection<QuickAction> Actions { get; }
        = [.. QuickActions.Toutes()];

    private SenSÉ.Plugins.VeilleOutils? _veille;

    /// <summary>Refait la grille après un changement dans les dossiers d'outils.</summary>
    /// <remarks>
    /// Le vidage puis le remplissage passent par la collection existante, jamais par une nouvelle :
    /// l'écran est lié à celle-ci, et lui en substituer une autre le laisserait afficher l'ancienne.
    /// </remarks>
    private void Recharger()
    {
        Tools.ToolRegistry.Oublier();

        Actions.Clear();

        foreach (var action in QuickActions.Toutes())
        {
            Actions.Add(action);
        }
    }

    public string VersionText { get; } = SenSÉ.Shell.AppVersionInfo.ProductAndVersion + " Desktop";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Les comptes des tuiles sont redemandés chaque fois que la fenêtre revient devant
        // l'utilisateur : c'est le moment où il les lit, et celui où ils ont pu changer pendant
        // qu'il regardait ailleurs.
        Activated += (_, _) => RafraichirComptes();

        Tools.PluginTools.Annonce += Annoncer;
        Closed += (_, _) => Tools.PluginTools.Annonce -= Annoncer;

        // La veille prévient depuis son propre fil : le passage par le répartiteur n'est pas une
        // politesse, c'est la seule façon de toucher une collection liée à l'écran sans faire
        // tomber la fenêtre.
        _veille = new SenSÉ.Plugins.VeilleOutils(AppContext.BaseDirectory, SenSÉ.Core.Diagnostics.UiLog.Info);
        _veille.Change += () => Dispatcher.BeginInvoke(Recharger);

        Closed += (_, _) =>
        {
            _veille?.Dispose();
            _veille = null;
        };
    }

    /// <summary>Redemande à chaque tuile ce qu'elle a à compter.</summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Le compte était calculé dans l'initialiseur de
    /// <c>QuickActions.All</c>, un membre statique : une fois par processus, au démarrage, quand
    /// rien ne tourne encore. La tuile du moniteur d'activité restait donc muette pour le reste de
    /// la session, y compris avec deux serveurs sur la carte.
    /// </remarks>
    private void RafraichirComptes()
    {
        foreach (var action in Actions)
        {
            action.Rafraichir();
        }
    }

    /// <summary>
    /// Jusqu'à quand le message d'une action garde la ligne.
    /// </summary>
    /// <remarks>
    /// Sans cette retenue, survoler une tuile effaçait ce qu'une action venait de dire — et c'est
    /// exactement ce qui s'est produit : « Démarrage du serveur, une minute ou deux » a disparu au
    /// premier mouvement de souris, l'utilisateur n'a plus rien vu pendant vingt-neuf secondes et a
    /// quitté en concluant que l'outil était cassé.
    /// </remarks>
    private DateTime _annonceJusqua;

    /// <summary>Montre ce qu'une action est en train de faire.</summary>
    /// <remarks>
    /// Appelé depuis le fil de l'action, jamais celui de l'affichage : un verbe qui attend un
    /// serveur tourne à part, et c'est tout l'intérêt.
    /// </remarks>
    private void Annoncer(string message)
    {
        if (message.Length == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            SelectedHint = message;

            // Renouvelé à chaque avancement : tant que l'action parle, la ligne lui appartient.
            _annonceJusqua = DateTime.UtcNow.AddSeconds(45);
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reposition();

        // Focus the first tile so a gamepad or the arrow keys have somewhere to start once the
        // window has the focus. Guarded on the window being active: at startup it is deliberately
        // not activated, and this must never steal the foreground from the user.
        if (IsActive)
        {
            Tiles.ApplyTemplate();
            Tiles.UpdateLayout();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    /// <summary>
    /// Spreads the environment over the work area, once the window is already shown.
    /// </summary>
    /// <remarks>
    /// The work area, not <c>WindowState.Maximized</c>. A borderless window
    /// (<c>WindowStyle="None"</c>) maximises over the whole monitor, taskbar included — Windows
    /// treats it as a fullscreen application and keeps the taskbar behind it. Sized to the work
    /// area instead, the window stops where the taskbar begins, so the taskbar stays in front and
    /// clickable, and clicking another window there is what puts this one behind.
    /// </remarks>
    private void Reposition()
    {
        var work = SystemParameters.WorkArea;

        WindowState = WindowState.Normal;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
    }

    // ---- Staying underneath ----

    private const int WM_WINDOWPOSCHANGING = 0x0046;

    /// <summary>The bottom of the Z order, as <c>SetWindowPos</c> spells it.</summary>
    private static readonly IntPtr HwndBottom = new(1);

    /// <summary>"Leave the Z order alone" — the flag that says this request is none of our business.</summary>
    private const uint SwpNoZOrder = 0x0004;

    /// <summary>
    /// Keeps the environment below every other window, whatever tries to raise it.
    /// </summary>
    /// <remarks>
    /// The window is the backdrop: it covers the Windows desktop and nothing else. Letting the
    /// clicks through was only half of it — that lets the window <i>go</i> behind, but nothing keeps
    /// it there. Clicking a tile activates it, activation raises it, and it lands on top of whatever
    /// the user had open. For a tool, being covered by the environment that launched it is the same
    /// as not working.
    ///
    /// <para>
    /// Forced here rather than by a call to <c>SetWindowPos</c>, because a call fixes the order at
    /// one instant and anything may change it the next. Every single Z-order change — ours, the
    /// user's, another application's, the shell's — is announced by this message first, and rewriting
    /// its request is what makes the rule hold instead of being re-applied forever.
    /// </para>
    ///
    /// <para>
    /// Focus is untouched, and that is the point of doing it this way. Under Windows the Z order and
    /// the active window are two separate things: the environment can hold the keyboard while sitting
    /// at the bottom, so the tiles stay reachable with the arrow keys and the gamepad. The Windows
    /// desktop stays below regardless — the shell pins it there itself, so the bottom of the ordinary
    /// Z order is still above the wallpaper and the icons.
    /// </para>
    ///
    /// <para>
    /// Which is also why tools need no pinning of their own. An ordinary window is above the backdrop
    /// by construction. Making tools <c>Topmost</c> instead would have put them above everything —
    /// Steam, a fullscreen game, a video call — and a tool developer would have had to write Z-order
    /// code to be a well-behaved window.
    /// </para>
    /// </remarks>
    private IntPtr KeepBehindEveryOtherWindow(
        IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_WINDOWPOSCHANGING)
        {
            return IntPtr.Zero;
        }

        var position = Marshal.PtrToStructure<WindowPos>(lParam);

        // Only the requests that were already going to touch the Z order. Anything carrying
        // SWP_NOZORDER is a move, a resize or a frame change and is left exactly as it came.
        //
        // The first version rewrote every request and cleared the flag, which turned each of those
        // into a real restack of the whole desktop. Measured on this machine that was five needless
        // restacks per session rather than the storm it was suspected of being — but a window that
        // reorders the desktop when it was only told to resize is wrong whatever the count.
        //
        // Nothing is lost by narrowing it. A window can only be raised by a request that changes the
        // Z order, and every one of those still arrives here and is still sent to the bottom.
        if ((position.Flags & SwpNoZOrder) != 0)
        {
            return IntPtr.Zero;
        }

        position.InsertAfter = HwndBottom;

        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);

        // Left unhandled on purpose: WPF reads this same message to follow its own move and resize,
        // and swallowing it would break the layout. The request has been rewritten; it still has to
        // reach everyone else.
        return IntPtr.Zero;
    }

    /// <summary>The move and resize request Windows is about to carry out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    // ---- Never minimising ----

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;

    /// <summary>The command bits of a system command; the low four are Windows' own.</summary>
    private const int SysCommandMask = 0xFFF0;

    /// <summary>
    /// Refuses the request to minimise, wherever it comes from.
    /// </summary>
    /// <remarks>
    /// The environment is the backdrop, and a backdrop that can be minimised leaves a hole: the
    /// screen falls back to the Windows desktop and the environment has to be found in the taskbar
    /// to come back. Nothing about it is a window the user should have to manage.
    ///
    /// <para>
    /// It matters most for the shortcut that clears the screen. Asking the shell to minimise
    /// everything asks this window too, and without this rule the one window meant to stay would go
    /// with the rest.
    /// </para>
    /// </remarks>
    private IntPtr RefuseToMinimise(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The low four bits carry internal state — which is why the value must be masked before it is
        // compared, and why comparing it raw silently stops matching.
        if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & SysCommandMask) == SC_MINIMIZE)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Puts the window back, for the requests that never asked.
    /// </summary>
    /// <remarks>
    /// A backstop, because <c>WM_SYSCOMMAND</c> is only the polite route. <c>ShowWindow</c> minimises
    /// a window without sending it, and that is the route the shell takes for some of its own
    /// operations — so refusing the message alone would hold most of the time, which for a rule like
    /// this is the same as not holding.
    /// </remarks>
    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        base.OnStateChanged(e);
    }

    // ---- Letting the clicks through ----

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    /// <summary>
    /// Tells Windows that the empty part of the window is not there, as far as the mouse goes.
    /// </summary>
    /// <remarks>
    /// <c>Background="{x:Null}"</c> alone is not enough, and believing it was cost several rounds of
    /// this. A null background stops WPF's <i>internal</i> hit test — no element in the visual tree
    /// is hit — but the window is still an HWND: Windows delivers the click to it, WPF answers
    /// <c>HTCLIENT</c>, and the click dies there. Nothing reaches the window underneath, so no other
    /// window can be brought forward and this one can never go behind.
    ///
    /// <para>
    /// <c>HTTRANSPARENT</c> is the answer that means "keep looking below me". Windows then repeats
    /// the hit test on the next window down and the click lands where the user aimed it.
    /// </para>
    ///
    /// <para>
    /// Per point rather than per window: <c>WS_EX_TRANSPARENT</c> would do the same thing for the
    /// whole surface, which is what the on-screen keyboard uses, but here the tiles have to stay
    /// clickable. The card's own rectangle answers the question — inside it the window keeps the
    /// click, outside it the click goes through.
    /// </para>
    ///
    /// <para>
    /// A cached rectangle, and that part is not an optimisation. <c>WM_NCHITTEST</c> does not arrive
    /// on click: Windows sends it on every mouse move, dozens of times a second. The first version
    /// of this hook called <c>PointFromScreen</c> and <see cref="UIElement.InputHitTest"/> on each
    /// one, walking the visual tree across a work-area-sized window — the pointer lagged, and device
    /// changes lagged with it, because <c>WM_DEVICECHANGE</c> is broadcast to every top-level window
    /// and one slow window delays the broadcast for the whole machine. Comparing four numbers costs
    /// nothing and answers the same question.
    /// </para>
    /// </remarks>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(PassClicksThroughTheEmptyArea);
            source.AddHook(KeepBehindEveryOtherWindow);
            source.AddHook(RefuseToMinimise);
        }

        // Setting a flag is free; recomputing happens at most once per hit test that follows a
        // layout pass, rather than on every mouse move.
        LayoutUpdated += (_, _) => _cardBoundsStale = true;
        LocationChanged += (_, _) => _cardBoundsStale = true;
    }

    private Rect _cardBounds = Rect.Empty;
    private bool _cardBoundsStale = true;

    private IntPtr PassClicksThroughTheEmptyArea(
        IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST)
        {
            return IntPtr.Zero;
        }

        // Screen coordinates, packed as two signed 16-bit halves. Signed matters: a monitor left of
        // the primary one has negative x, and reading it unsigned lands the point on the far right.
        var packed = lParam.ToInt32();
        var screenPoint = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));

        if (_cardBoundsStale)
        {
            _cardBounds = CardScreenBounds();
            _cardBoundsStale = false;
        }

        if (_cardBounds.IsEmpty || _cardBounds.Contains(screenPoint))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(HTTRANSPARENT);
    }

    /// <summary>The card's rectangle in physical screen pixels, or empty if it cannot be measured.</summary>
    /// <remarks>
    /// <c>PointToScreen</c> returns device pixels, which is the same space <c>WM_NCHITTEST</c> packs
    /// its point in, so the two compare directly and the DPI never enters into it. Empty is the safe
    /// answer while the visual has no presentation source: the window keeps the click rather than
    /// dropping it somewhere unpredictable.
    /// </remarks>
    private Rect CardScreenBounds()
    {
        try
        {
            if (Card.ActualWidth <= 0 || Card.ActualHeight <= 0)
            {
                return Rect.Empty;
            }

            return new Rect(
                Card.PointToScreen(new Point(0, 0)),
                Card.PointToScreen(new Point(Card.ActualWidth, Card.ActualHeight)));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    // ---- Dragging ----

    private Point _dragOrigin;
    private bool _dragging;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(this);
        _dragging = true;
        Mouse.Capture((IInputElement)sender);
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = e.GetPosition(this);
        Left += current.X - _dragOrigin.X;
        Top += current.Y - _dragOrigin.Y;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Mouse.Capture(null);
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickAction action })
        {
            return;
        }

        // Every tile runs inside this guard. Without it an exception here reaches the dispatcher,
        // the environment window goes down, and OnExit takes the controller bridge and the overlay
        // keyboard with it: one broken tile shutting down all of SenSÉ. A tile that fails
        // should fail alone and say so.
        UiLog.Action(action.Label);

        try
        {
            // Actions that need the environment window run it themselves and manage their own
            // hide-and-restore, so the overlay is never left off screen waiting for a signal.
            if (action.WithEnvironment is not null)
            {
                var dit = action.WithEnvironment(this);

                // Ce que l'action rend garde la ligne, même si la souris passe ailleurs. Une action
                // qui continue en fond renouvellera d'elle-même par ses annonces.
                if (dit.Length > 0)
                {
                    SelectedHint = dit;
                    _annonceJusqua = DateTime.UtcNow.AddSeconds(45);
                }

                UiLog.Action(action.Label, $"completed with the environment: {dit}");

                // Une action vient de lancer — ou d'arrêter — quelque chose : les comptes des
                // tuiles ont pu changer sans que la fenêtre ait quitté le premier plan.
                RafraichirComptes();

                return;
            }

            // The remaining keystroke action still needs this window out of the way: it goes to
            // whatever is in front, and the clipboard history has to paste into the application the
            // user was actually in. Hidden rather than closed — the window keeps its position and
            // comes back where it was.
            if (action.YieldsForeground)
            {
                Hide();
                UiLog.Window(nameof(MainWindow), "hidden", $"{action.Label} needs the foreground");
                ShowSignal.Watch(this);
            }

            action.Invoke();
            UiLog.Action(action.Label, "completed");
        }
        catch (Exception exception)
        {
            UiLog.Failure($"tile '{action.Label}'", exception);

            // Whatever went wrong, the overlay must end up back on screen: a hidden environment
            // with no way back is indistinguishable from a crash.
            if (!IsVisible)
            {
                OverlayShower.Show(this);
                UiLog.Window(nameof(MainWindow), "restored", "after a tile failed while hidden");
            }

            SelectedHint = $"{action.Label} : {exception.GetType().Name} — {exception.Message}";
        }
    }

    private void Tile_Focused(object sender, KeyboardFocusChangedEventArgs e) => ShowHint(sender);

    private void Tile_Hovered(object sender, MouseEventArgs e) => ShowHint(sender);

    private void ShowHint(object sender)
    {
        // Ce qu'une action dit passe avant la description d'une tuile qu'on survole.
        //
        // L'inverse a coûté une session : le message « Démarrage du serveur, une minute ou deux »
        // s'effaçait au premier mouvement de souris, et l'utilisateur, sans plus aucun signe, a
        // conclu que l'outil ne démarrait pas. Il chargeait. Une description de tuile se retrouve
        // en survolant de nouveau ; un avancement perdu ne se retrouve pas.
        if (DateTime.UtcNow < _annonceJusqua)
        {
            return;
        }

        if (sender is FrameworkElement { Tag: QuickAction action })
        {
            SelectedHint = Strings.Current[action.Hint];
        }
    }

    /// <summary>
    /// Quits when the overlay itself is closed by anything other than its own button.
    /// </summary>
    /// <remarks>
    /// Alt+F4 and "close window" from the taskbar bypass the ✕. Under
    /// <see cref="System.Windows.ShutdownMode.OnExplicitShutdown"/> nothing else would end the
    /// process, leaving it running with no window at all.
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        UiLog.Window(nameof(MainWindow), "closed", "quitting SenSÉ");
        base.OnClosed(e);
        App.Quit();
    }

    /// <summary>
    /// The only deliberate way out.
    /// </summary>
    /// <remarks>
    /// Quits explicitly rather than closing the window and relying on a shutdown mode to notice.
    /// The process now ends only here, so nothing that merely closes a window — a calculator, a
    /// settings screen, a capture that failed — can take SenSÉ down with it.
    /// </remarks>
    private void Close_Click(object sender, RoutedEventArgs e) => App.Quit();
}
