using System.Windows;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;
using SenSÉ.Atelier.UI;

namespace SenSÉ.Atelier;

public partial class App : Application
{
    public Mcp.ServeurHttp? Serveur { get; set; }
    public string? Racine { get; set; }
    public Espace EspaceCourant { get; set; } = Espace.Codage;

    /// <summary>
    /// Vrai quand l'Atelier a ete lance avec <c>--no-window</c> : la fenetre
    /// n'est pas affichee au demarrage, elle sera creee a la demande via
    /// le verbe HTTP <c>fenetre/ouvrir</c>.
    /// </summary>
    public bool NoWindow { get; set; } = false;

    private FenetreAtelier? _fenetre;

    public App()
    {
        // Sans ce mode, l'app WPF s'arreterait a la fermeture de la fenetre
        // (OnLastWindowClose), ce qui couperait aussi le serveur HTTP. Or la
        // fenetre peut etre fermee alors que l'Assistant a encore besoin du
        // subprocess via HTTP. On ne quitte que sur Shutdown explicite.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(Racine ?? System.IO.Path.GetTempPath(), "_startup_error.log");
                System.IO.File.AppendAllText(logPath,
                    "[" + System.DateTime.Now.ToString("o") + "] DISPATCHER UNHANDLED\n" + e.Exception.ToString() + "\n\n");
            }
            catch { }
            System.Console.Error.WriteLine("[atelier] UNHANDLED: " + e.Exception);
            e.Handled = true;
        };
    }

    /// <summary>
    /// Cree la fenetre principale si besoin, et l'affiche. Doit etre appele
    /// sur le Dispatcher WPF (typiquement par le verbe HTTP via Invoke).
    /// </summary>
    /// <remarks>
    /// <b>Le subprocess survit a la fermeture de la fenetre.</b> La fenetre
    /// est fermee par <c>Hide()</c>, pas par <c>Close()</c> : le serveur HTTP
    /// continue a repondre, et l'Assistant peut toujours appeler ses verbes.
    /// L'utilisateur rouvre la fenetre en rappellant <see cref="AfficherFenetre"/>,
    /// qui reutilise la meme instance.
    /// </remarks>
    public void AfficherFenetre()
    {
        if (_fenetre is null)
        {
            if (Serveur is null || Racine is null)
            {
                // Securite : si la fenetre est demandee avant que Program.cs
                // n'ait fini d'initialiser, on log et on laisse tomber.
                System.Console.Error.WriteLine("[atelier] AfficherFenetre: Serveur ou Racine null");
                return;
            }
            _fenetre = new FenetreAtelier(Racine, Serveur);
            _fenetre.Closing += (s, e) =>
            {
                // L'utilisateur ferme la fenetre : on la cache, on ne quitte
                // pas. Le subprocess reste en vie pour servir l'Assistant.
                if (NoWindow)
                {
                    e.Cancel = true;
                    _fenetre!.Hide();
                }
                // Sinon, on laisse WPF fermer la fenetre et l'app s'arrete
                // naturellement (comportement historique en mode window).
            };
        }
        if (!_fenetre.IsVisible) _fenetre.Show();
        if (_fenetre.WindowState == WindowState.Minimized)
            _fenetre.WindowState = WindowState.Normal;
        _fenetre.Activate();
    }

    /// <summary>La fenetre WPF est-elle visible en ce moment (mode headless) ?</summary>
    public bool FenetreVisible => _fenetre is { IsVisible: true };
}