using System.Diagnostics;

namespace SteamXBox.Plugins;

/// <summary>Ce qu'a donné l'accueil d'un outil.</summary>
/// <param name="Code">Ce que l'installeur a rendu en sortant. Zéro veut dire réussi.</param>
/// <param name="Dossier">Où l'outil a été accueilli.</param>
/// <param name="Octets">Ce qui pèse dans ce dossier une fois l'installeur parti.</param>
/// <param name="Dits">Le récit, ligne par ligne, y compris ce qui a échappé au dossier.</param>
public readonly record struct AccueilRapport(
    int Code,
    string Dossier,
    long Octets,
    IReadOnlyList<string> Dits);

/// <summary>
/// Fait tourner l'installeur d'un programme extérieur dans un environnement que SteamXBox lui
/// compose, pour qu'il s'installe dans le produit plutôt que dans le dossier de l'utilisateur.
/// </summary>
/// <remarks>
/// <b>Pourquoi passer par l'installeur du tiers plutôt que de le contourner.</b> Un programme
/// moderne n'est pas une pile de fichiers à copier : il enregistre des associations, écrit sa
/// version, pose son désinstalleur. Le recopier à la main donne quelque chose qui démarre une fois
/// et se met à jour jamais. On garde donc l'installeur officiel, et l'on change seulement ce qu'il
/// croit être le disque de l'utilisateur.
///
/// <para>
/// <b>Ce que l'expérience a appris, et qui est encodé ici.</b> L'installeur est lancé en silence,
/// sinon il attend un clic que personne ne donnera dans un écran de réglages. Le profil de
/// l'utilisateur n'est pas détourné, parce que cela le tue. Et ce qui a échappé au dossier est
/// mesuré puis dit — un accueil qui laisse des traces ailleurs doit se voir, pas se supposer.
/// </para>
/// </remarks>
public static class AccueilOutil
{
    /// <summary>
    /// Les arguments qui demandent à un installeur de se taire.
    /// </summary>
    /// <remarks>
    /// <c>/S</c> est la convention NSIS, sur laquelle reposent Electron et donc la plupart des
    /// programmes livrés ainsi. Un installeur qui ne la connaît pas ignore l'argument.
    /// </remarks>
    public const string Silence = "/S";

    /// <summary>
    /// Les arguments qui disent à un installeur où s'installer.
    /// </summary>
    /// <remarks>
    /// <b>Pourquoi ceci et pas le détournement d'environnement.</b> Mesuré sur l'installeur de Comfy
    /// Desktop : détourner <c>LOCALAPPDATA</c> ne l'atteint pas. NSIS ne lit pas la variable — sa
    /// constante <c>$LOCALAPPDATA</c> vient de l'interface Windows des dossiers spéciaux. L'essai
    /// s'est terminé sur un code zéro trompeur, dossier d'accueil vide et application installée dans
    /// le profil de l'utilisateur.
    ///
    /// <para>
    /// <c>/D=</c> est la porte d'entrée officielle de NSIS, donc d'Electron et de la plupart des
    /// programmes livrés ainsi. Elle doit venir en dernier, sans guillemets, et prend tout ce qui
    /// suit jusqu'à la fin de la ligne — un chemin qui contient des espaces passe donc tel quel.
    /// C'est le premier vrai membre du protocole : on demande, on ne détourne pas.
    /// </para>
    /// </remarks>
    public static string Ou(string dossier) => $"/D={dossier}";

    /// <summary>
    /// Installe un programme extérieur dans un dossier qui n'est qu'à lui.
    /// </summary>
    /// <param name="setup">L'installeur à faire tourner, où qu'il soit.</param>
    /// <param name="dossier">Le dossier d'accueil, dans le produit.</param>
    /// <param name="declare">Ce que l'outil demande en plus, s'il demande quelque chose.</param>
    /// <param name="temoins">
    /// Des dossiers du vrai profil à surveiller : ce qui y apparaît pendant l'accueil a échappé au
    /// détournement, et c'est la seule mesure qui dise si l'isolement a tenu.
    /// </param>
    /// <param name="arret">Pour renoncer, si l'installeur s'éternise.</param>
    public static AccueilRapport Installer(
        string setup,
        string dossier,
        EnvironnementOutil? declare = null,
        IReadOnlyList<string>? temoins = null,
        CancellationToken arret = default)
    {
        var dits = new List<string>();

        if (!File.Exists(setup))
        {
            return new AccueilRapport(-1, dossier, 0, [$"Installeur introuvable : {setup}"]);
        }

        var avant = Empreinte(temoins);
        // L'ordre compte : /D prend tout ce qui suit jusqu'à la fin de la ligne, donc il vient en
        // dernier, et sans guillemets même quand le chemin porte des espaces.
        var depart = new ProcessStartInfo(setup)
        {
            Arguments = $"{Silence} {Ou(dossier)}",
            WorkingDirectory = dossier,
        };

        if (EnvironnementIsole.Preparer(depart, dossier, declare) is { Length: > 0 } refus)
        {
            return new AccueilRapport(-1, dossier, 0, [refus]);
        }

        dits.Add($"Installeur lancé en silence, tout écrit dans {dossier}.");

        int code;

        try
        {
            using var installeur = Process.Start(depart)
                ?? throw new InvalidOperationException("l'installeur n'a pas démarré");

            installeur.WaitForExitAsync(arret).GetAwaiter().GetResult();
            code = installeur.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return new AccueilRapport(-1, dossier, Poids(dossier), [.. dits, "Accueil interrompu."]);
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new AccueilRapport(-1, dossier, 0, [.. dits, $"Accueil impossible : {exception.Message}"]);
        }

        dits.Add(code == 0
            ? "L'installeur s'est terminé normalement."
            : $"L'installeur est sorti sur le code {code}" + (code == -1073741819
                ? " — une violation d'accès, c'est-à-dire qu'il a planté."
                : "."));

        foreach (var echappe in Echappees(avant, temoins))
        {
            dits.Add($"Échappé au détournement : {echappe}");
        }

        return new AccueilRapport(code, dossier, Poids(dossier), dits);
    }

    /// <summary>Ce que contiennent les dossiers témoins avant l'accueil.</summary>
    private static Dictionary<string, HashSet<string>> Empreinte(IReadOnlyList<string>? temoins)
    {
        var vue = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var temoin in temoins ?? [])
        {
            vue[temoin] = Directory.Exists(temoin)
                ? [.. Directory.EnumerateFileSystemEntries(temoin).Select(f => Path.GetFileName(f) ?? "")]
                : [];
        }

        return vue;
    }

    /// <summary>Ce qui est apparu dans les dossiers témoins malgré le détournement.</summary>
    private static IEnumerable<string> Echappees(
        Dictionary<string, HashSet<string>> avant,
        IReadOnlyList<string>? temoins)
    {
        foreach (var temoin in temoins ?? [])
        {
            if (!Directory.Exists(temoin))
            {
                continue;
            }

            foreach (var nom in Directory.EnumerateFileSystemEntries(temoin).Select(Path.GetFileName))
            {
                if (nom is not null && !avant[temoin].Contains(nom))
                {
                    yield return Path.Combine(temoin, nom);
                }
            }
        }
    }

    private static long Poids(string dossier)
    {
        try
        {
            return Directory.Exists(dossier)
                ? Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length)
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
