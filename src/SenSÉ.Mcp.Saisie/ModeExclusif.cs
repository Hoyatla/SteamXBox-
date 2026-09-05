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
/// <para><b>Souris et Clavier verifient <see cref="EstActif"/>.</b> Ce
/// n'est pas un hack UI : c'est le seul moyen d'etre sur que l'Assistant
/// ne bouge pas la souris pendant que tu tapes au clavier. Sans ce
/// verrou, c'est le chaos.</para>
/// </remarks>
public sealed class ModeExclusif
{
    private static ModeExclusif? _instance;
    private static readonly object _gate = new();

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
    }

    /// <summary>Ferme l'overlay et desabonne le mode exclusif.</summary>
    public static void Fermer()
    {
        lock (_gate)
        {
            if (_instance is null) return;
            _instance._fenetre.Fermer();
            _instance = null;
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