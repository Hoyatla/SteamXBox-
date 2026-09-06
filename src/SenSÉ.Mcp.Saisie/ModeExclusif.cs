using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Media;
using System.Windows.Threading;

namespace SenSÉ.Mcp.Saisie;

/// <summary>
/// Le gardien visuel : un overlay WPF topmost qui apparait des que
/// l'Assistant pilote la souris ou le clavier, et qui reste jusqu'a
/// la fin de la sequence.
/// </summary>
/// <remarks>
/// <b>Topmost, transparent autour, Echap pour interrompre.</b> La fenetre
/// est toujours au-dessus, mais le bandeau ne prend que le haut de
/// l'ecran : le reste reste transparent aux clics. Echap est intercepte
/// pour permettre a l'utilisateur de couper net la sequence en cours.
///
/// <para><b>Souris et Clavier verchent <see cref="EstActif"/>.</b> Ce
/// n'est pas un hack UI : c'est le seul moyen d'etre sur que l'Assistant
/// ne bouge pas la souris pendant que tu tapes au clavier. Sans ce
/// verrou, c'est le chaos.</para>
///
/// <para><b>Thread WPF dedie.</b> WPF exige une <see cref="Application"/>
/// et un dispatcher actif pour creer et afficher des fenetres. mcp-saisie
/// etant un subprocess stdio sans GUI, on heberge l'Application WPF sur
/// un thread STA dedie (voir <see cref="WpfHost"/>). Toutes les operations
/// WPF (Show, Close, BeginInvoke) sont marshalee sur ce thread.
/// <see cref="Ouvrir"/> et <see cref="Fermer"/> bloquent l'appelant
/// jusqu'a ce que l'operation soit terminee sur le thread WPF.</para>
/// </remarks>
public sealed class ModeExclusif
{
    private static ModeExclusif? _instance;
    private static readonly object _gate = new();
    private static WpfHost? _host;

    private readonly FenetreBandeau _fenetre;

    private ModeExclusif(string serveur, string sequence)
    {
        _fenetre = new FenetreBandeau(serveur, sequence);
    }

    /// <summary>Une instance est-elle deja ouverte ?</summary>
    public static bool EstActif => _instance is not null;

    /// <summary>Ouvre l'overlay. Si une instance est deja ouverte, on remplace son texte.</summary>
    public static void Ouvrir(string serveur, string sequence)
    {
        var host = ObtenirHost();
        host.Run(() =>
        {
            lock (_gate)
            {
                if (_instance is not null)
                {
                    _instance._fenetre.ChangerTexte(serveur, sequence);
                    return;
                }
                _instance = new ModeExclusif(serveur, sequence);
                _instance._fenetre.Show();
            }
        });
    }

    /// <summary>Ferme l'overlay et desabonne le mode exclusif.</summary>
    public static void Fermer()
    {
        if (_instance is null) return;
        if (_host is null) return;
        _host.Run(() =>
        {
            lock (_gate)
            {
                if (_instance is null) return;
                _instance._fenetre.Fermer();
                _instance = null;
            }
        });
    }

    /// <summary>Demarre le thread WPF dedie a la demande, idempotent.</summary>
    private static WpfHost ObtenirHost()
    {
        if (_host is not null) return _host;
        lock (_gate)
        {
            if (_host is null)
            {
                _host = new WpfHost();
            }
            return _host;
        }
    }
}

/// <summary>
/// Un thread STA dedie qui heberge l'Application WPF et son dispatcher.
/// Sans ca, mcp-saisie (stdio subprocess, pas de GUI) n'a pas d'
/// Application WPF, et <c>Window.Show()</c> bloque indefiniment en
/// attendant un dispatcher qui n'existe pas.
/// </summary>
/// <remarks>
/// Le constructeur bloque jusqu'a ce que le thread ait demarre
/// l'Application et le dispatcher (5 s max, ensuite timeout). Apres
/// ca, <see cref="Run"/> peut etre appele depuis n'importe quel thread ;
/// il marshale l'action sur le thread WPF et bloque jusqu'a la fin.
///
/// <para><b>Background thread.</b> Il est tue automatiquement quand le
/// process mcp-saisie se termine (a la fermeture de stdin).</para>
/// </remarks>
internal sealed class WpfHost
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;
    private Exception? _initError;

    public WpfHost()
    {
        _thread = new Thread(InitializeAndRun)
        {
            Name = "mcp-saisie-wpf",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("WPF host n'a pas reussi a demarrer en 5s");
        }
        if (_initError is not null) throw _initError;
    }

    private void InitializeAndRun()
    {
        try
        {
            var app = new System.Windows.Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _initError = ex;
            _ready.Set();
        }
    }

    /// <summary>Execute une action sur le thread WPF. Bloque l'appelant.</summary>
    public void Run(Action action)
    {
        if (_dispatcher is null)
        {
            throw new InvalidOperationException("WPF host non initialise");
        }
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }
}

/// <summary>
/// La fenetre du bandeau. Seule classe visible de l'exterieur.
/// </summary>
public sealed class FenetreBandeau : Window
{
    private readonly TextBlock _texte;

    public FenetreBandeau(string serveur, string sequence)
    {
        Title = "Assistant pilote";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        ShowActivated = false;
        Width = 600;
        Height = 64;
        Left = (SystemParameters.WorkArea.Width - Width) / 2;
        Top = 0;

        var bordure = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)),
            BorderThickness = new Thickness(2),
            CornerRadius = new System.Windows.CornerRadius(6),
            Padding = new Thickness(16, 8, 16, 8),
        };
        _texte = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Text = $"L'ASSISTANT PILOTE  |  {serveur} / {sequence}",
        };
        bordure.Child = _texte;
        Content = bordure;
    }

    public void ChangerTexte(string serveur, string sequence)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _texte.Text = $"L'ASSISTANT PILOTE  |  {serveur} / {sequence}";
        });
    }

    public void Fermer()
    {
        Dispatcher.BeginInvoke(() => Close());
    }

    /// <summary>Echap interrompt la sequence en cours.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ModeExclusif.Fermer();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
