using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Desktop;

/// <summary>
/// L'instance de recherche locale, qui vit avec la fenêtre de l'Assistant.
/// </summary>
/// <remarks>
/// <b>Elle existe parce que la recherche par navigateur ne tient pas dans la durée.</b> Mesure du
/// 9 septembre 2026 : la façade publique du moteur rend dix résultats en 0,65 s sur une requête
/// HTTP, puis, après une dizaine de requêtes, un <c>HTTP 202</c> portant « complete the following
/// challenge to confirm this search was made by a human ». Ce contrôle n'est pas contourné. Un
/// service rendu à l'utilisateur ne peut pas reposer sur une porte qui se ferme sans prévenir.
///
/// <para>
/// <b>Une instance à soi est la seule fondation.</b> Elle interroge les moteurs pour son compte,
/// n'écoute que la boucle locale, et ne rend de comptes à personne sur le nombre de recherches.
/// C'est aussi ce que le produit préférait déjà : <c>chercher_web</c> est déclaré en premier lieu
/// pour le fournisseur « Instance », le navigateur n'étant qu'un repli.
/// </para>
///
/// <para>
/// <b>Absente, elle ne manque à personne.</b> Sans venv ni configuration, rien ne démarre et rien
/// n'échoue : la recherche retombe sur le navigateur, comme avant. C'est la règle déjà tenue pour
/// les moteurs de la table — une voie absente vaut mieux qu'une voie qui échoue au premier appel.
/// </para>
///
/// <para>
/// <b>Elle se lève avec l'Assistant, pas avec l'environnement.</b> Elle pèse deux cents mégaoctets
/// et personne d'autre ne s'en sert ; la garder ouverte pour toute la session la ferait tourner des
/// heures pour une conversation refermée. C'est la règle que <c>ServeursMcp</c> énonce déjà.
/// </para>
/// </remarks>
internal static class ServeurRecherche
{
    /// <summary>Le port de l'instance. Celui que <c>search.json</c> désigne.</summary>
    private const int Port = 8888;

    private static readonly object _verrou = new();
    private static Process? _serveur;

    private static string Dossier => Path.Combine(AppContext.BaseDirectory, "Outils", "SearXNG");

    private static string Python => Path.Combine(Dossier, "venv", "Scripts", "python.exe");

    private static string Reglages => Path.Combine(Dossier, "settings.yml");

    private static string Source => Path.Combine(Dossier, "source");

    /// <summary>Démarre l'instance si elle est installée et ne tourne pas déjà.</summary>
    public static void Demarrer()
    {
        lock (_verrou)
        {
            if (Vivant() || !Installee())
            {
                return;
            }

            // Un serveur deja en ecoute est adopte plutot que double : deux instances sur le meme
            // port, l'une gagnerait et l'autre echouerait au demarrage sans que rien ne le dise.
            if (Ecoute())
            {
                UiLog.Info($"recherche : une instance repond deja sur {Port}, adoptee.");

                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Python,
                    UseShellExecute = false,
                    CreateNoWindow = true,

                    // Le repertoire de travail, et des chemins relatifs ensuite : les binaires
                    // natifs de ce produit refusent l'accent de « SenSÉ » dans leurs ARGUMENTS,
                    // jamais dans leur repertoire courant. C'est la lecon deja payee ailleurs.
                    WorkingDirectory = Source,
                };

                psi.ArgumentList.Add("-m");
                psi.ArgumentList.Add("searx.webapp");
                psi.Environment["SEARXNG_SETTINGS_PATH"] = Reglages;

                var lance = Process.Start(psi);

                if (lance is null)
                {
                    UiLog.Warn("recherche : Process.Start a renvoye null.");

                    return;
                }

                _serveur = lance;
                JobEnfants.Inscrire(lance);

                UiLog.Info($"recherche : instance locale demarree, PID {lance.Id}, port {Port}.");
            }
            catch (Exception ex)
            {
                UiLog.Failure("demarrage de l'instance de recherche", ex);
            }
        }
    }

    /// <summary>Arrête l'instance et ce qu'elle a ouvert.</summary>
    public static void Arreter()
    {
        lock (_verrou)
        {
            if (_serveur is null)
            {
                return;
            }

            try
            {
                if (!_serveur.HasExited)
                {
                    UiLog.Info($"recherche : arret de l'instance (PID {_serveur.Id}).");
                    _serveur.Kill(entireProcessTree: true);
                }

                _serveur.Dispose();
            }
            catch (Exception ex)
            {
                UiLog.Failure("arret de l'instance de recherche", ex);
            }

            _serveur = null;
        }
    }

    /// <summary>Vrai si l'instance est installée : le venv, la configuration et la source.</summary>
    /// <remarks>
    /// Les trois, et pas seulement le premier. Un venv sans configuration démarrerait sur les
    /// réglages par défaut de SearXNG — qui ne servent <b>pas</b> le format JSON, celui que le
    /// produit interroge. L'instance répondrait alors 403 à chaque recherche.
    /// </remarks>
    private static bool Installee()
        => File.Exists(Python) && File.Exists(Reglages) && Directory.Exists(Source);

    private static bool Vivant()
    {
        try { return _serveur is not null && !_serveur.HasExited; }
        catch { return false; }
    }

    /// <summary>Vrai si quelque chose écoute déjà sur le port.</summary>
    private static bool Ecoute()
    {
        try
        {
            using var essai = new TcpClient();

            return essai.ConnectAsync("127.0.0.1", Port).Wait(TimeSpan.FromMilliseconds(400))
                   && essai.Connected;
        }
        catch (Exception exception) when (exception is SocketException or AggregateException)
        {
            return false;
        }
    }
}
