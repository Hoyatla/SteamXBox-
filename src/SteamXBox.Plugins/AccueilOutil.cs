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
    /// programmes livrés ainsi. Elle doit venir en dernier et sans guillemets.
    /// </para>
    ///
    /// <para>
    /// <b>Et le chemin doit être court, au sens de Windows.</b> La documentation de NSIS promet que
    /// <c>/D=</c> prend tout jusqu'à la fin de la ligne, espaces compris. À l'essai, non : donné
    /// <c>C:\Program Files\SteamXbox\Outils\Comfy-Desktop</c>, l'installeur a coupé au premier
    /// espace et créé <c>C:\Program</c> à la racine du disque — quatre cent quatre-vingt-huit
    /// mégaoctets au mauvais endroit, sans la moindre erreur signalée. On passe donc la forme
    /// courte, celle qui n'a jamais d'espaces, et le produit vit sous <c>Program Files</c> : ce
    /// n'est pas un cas rare, c'est le cas normal.
    /// </para>
    /// </remarks>
    public static string Ou(string dossier) => $"/D={Court(dossier)}";

    /// <summary>Le nom court du dossier, celui qui ne porte pas d'espaces.</summary>
    /// <remarks>
    /// Windows ne donne un nom court qu'à ce qui existe : le dossier est donc créé d'abord. Si la
    /// forme courte est désactivée sur le volume — cela se règle, et certains l'éteignent — on rend
    /// le chemin tel quel plutôt que rien, en sachant qu'un espace le fera échouer. Mieux vaut un
    /// échec visible qu'un silence.
    /// </remarks>
    public static string Court(string dossier)
    {
        Directory.CreateDirectory(dossier);

        var tampon = new System.Text.StringBuilder(512);
        var taille = GetShortPathName(dossier, tampon, tampon.Capacity);

        return taille > 0 && taille < tampon.Capacity ? tampon.ToString() : dossier;
    }

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet =
            System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(
        string lpszLongPath, System.Text.StringBuilder lpszShortPath, int cchBuffer);

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
        string? plugins = null,
        CancellationToken arret = default)
    {
        var dits = new List<string>();

        if (!File.Exists(setup))
        {
            return new AccueilRapport(-1, dossier, 0, [$"Installeur introuvable : {setup}"]);
        }

        var avant = Empreinte(temoins);
        // Un espace qui survit à la forme courte est une impasse, et une impasse silencieuse : la
        // directive serait tronquée et l'installeur s'installerait ailleurs en annonçant une
        // réussite. On refuse ici plutôt que de le découvrir en trouvant un dossier inconnu à la
        // racine du disque.
        if (Court(dossier).Contains(' ', StringComparison.Ordinal))
        {
            return new AccueilRapport(-1, dossier, 0,
            [
                $"Accueil impossible dans « {dossier} » : le chemin contient un espace et Windows "
                + "ne lui donne pas de forme courte sur ce disque. Les installeurs coupent leur "
                + "directive au premier espace et s'installent ailleurs sans le dire. Choisissez un "
                + "dossier sans espace.",
            ]);
        }

        // L'ordre compte : /D prend tout ce qui suit jusqu'à la fin de la ligne, donc il vient en
        // dernier, et sans guillemets.
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

        // La tuile vient de ce qu'on trouve après coup, pas de ce qu'on espérait installer : c'est
        // le dossier qui dit ce qu'il est devenu, et un installeur qui a échoué n'en laisse pas de
        // quoi en faire une.
        if (plugins is { Length: > 0 } && code == 0 && Reconnaissance.Regarder(dossier) is { } trouve)
        {
            dits.Add(Declarer(trouve, plugins));
        }

        return new AccueilRapport(code, dossier, Poids(dossier), dits);
    }

    /// <summary>
    /// Écrit le manifeste qui donne sa tuile à un programme accueilli.
    /// </summary>
    /// <param name="programme">Ce que la reconnaissance a trouvé dans le dossier.</param>
    /// <param name="plugins">Le dossier des manifestes.</param>
    /// <returns>Ce qui s'est passé, en une phrase.</returns>
    /// <remarks>
    /// <b>Pourquoi un manifeste, et pas une tuile posée en mémoire.</b> Une tuile qui n'existe que
    /// dans le programme disparaîtrait au redémarrage, et rien ne dirait à l'écran des outils quoi
    /// archiver ni quoi éteindre. Le manifeste est ce qui rend l'outil visible <i>et</i> gouvernable
    /// — c'est le même fichier que pour un outil livré, écrit par nous au lieu de l'être à la main.
    ///
    /// <para>
    /// Il nomme son corps, et c'est le point : <c>environnement.dossier</c> dit où vit le programme,
    /// de sorte qu'archiver, éteindre ou éjecter agissent sur les cinq cents mégaoctets et non sur
    /// le kilooctet de description.
    /// </para>
    ///
    /// <para>
    /// Une bibliothèque n'en reçoit pas. Python et ffmpeg sont des programmes, l'assistant doit les
    /// connaître, mais une tuile qui ouvre une console n'a rien à faire sur la grille.
    /// </para>
    /// </remarks>
    public static string Declarer(ProgrammeReconnu programme, string plugins)
    {
        if (programme.Forme != FormeProgramme.Application)
        {
            return $"{programme.Nom} : pas de tuile — {programme.Pourquoi}.";
        }

        if (programme.Executable.Length == 0)
        {
            return $"{programme.Nom} : pas de tuile — rien à ouvrir dans ce dossier.";
        }

        var id = Identifiant(programme.Id);

        // Un outil déjà déclaré ne l'est pas deux fois. Sans cela, accueillir à nouveau un programme
        // pour le mettre à jour poserait une seconde tuile à côté de la première, et l'utilisateur
        // aurait deux boutons pour la même chose sans savoir lequel meurt en premier.
        if (Deja(plugins, programme.Dossier) is { Length: > 0 } tenu)
        {
            return $"{programme.Nom} : déjà déclaré par {tenu}, sa tuile est laissée telle quelle.";
        }

        var dossier = Path.Combine(plugins, id);
        var relatif = Path.GetFileName(programme.Dossier.TrimEnd(Path.DirectorySeparatorChar));

        var manifeste = new PluginManifest
        {
            Id = id,
            Name = programme.Nom,
            Category = "tool",
            Version = "1.0.0",
            Author = "accueilli par SteamXBox",
            Licence = "celle de son éditeur",
            Glyph = "EA86",
            Hint = $"{programme.Nom}, accueilli dans Outils\\{relatif}. "
                + "Il vit dans le produit : le supprimer, c'est supprimer son dossier.",
            Surface = "tile",
            Does = PluginActions.Application,
            Target = $"{{tools}}\\{relatif}\\{Path.GetFileName(programme.Executable)}",
            Environnement = new EnvironnementOutil { Dossier = $"{{tools}}\\{relatif}" },
            Enabled = true,
        };

        try
        {
            Directory.CreateDirectory(dossier);

            // Sans guillemets échappés sur les accents, et sans marque d'ordre d'octets : un
            // manifeste illisible est un outil perdu, et ce fichier a déjà été abîmé une fois par
            // un outil d'édition trop serviable.
            File.WriteAllText(
                Path.Combine(dossier, "plugin.json"),
                System.Text.Json.JsonSerializer.Serialize(manifeste, Ecriture),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return $"{programme.Nom} : tuile posée, elle ouvrira {Path.GetFileName(programme.Executable)}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{programme.Nom} : tuile impossible — {exception.Message}";
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions Ecriture = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Un identifiant tenable : minuscules, sans espace ni accent.</summary>
    private static string Identifiant(string brut)
    {
        var propre = new string([.. brut.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')]);

        while (propre.Contains("--", StringComparison.Ordinal))
        {
            propre = propre.Replace("--", "-", StringComparison.Ordinal);
        }

        return propre.Trim('-') is { Length: > 0 } net ? net : "outil-accueilli";
    }

    /// <summary>Le manifeste qui désigne déjà ce dossier, s'il y en a un.</summary>
    private static string Deja(string plugins, string corps)
    {
        var nom = Path.GetFileName(corps.TrimEnd(Path.DirectorySeparatorChar));

        if (!Directory.Exists(plugins))
        {
            return "";
        }

        foreach (var fichier in Directory.EnumerateFiles(plugins, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                if (File.ReadAllText(fichier).Contains($"\\\\{nom}\\\\", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(Path.GetDirectoryName(fichier)!);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Un manifeste illisible ne prouve pas qu'il désigne ce dossier : on continue.
            }
        }

        return "";
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
