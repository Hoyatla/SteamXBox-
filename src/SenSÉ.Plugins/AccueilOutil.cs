using System.Diagnostics;

namespace SenSÉ.Plugins;

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
/// Fait tourner l'installeur d'un programme extérieur dans un environnement que SenSÉ lui
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
    /// <c>C:\Program Files\SenSÉ\Outils\Comfy-Desktop</c>, l'installeur a coupé au premier
    /// espace et créé <c>C:\Program</c> à la racine du disque — quatre cent quatre-vingt-huit
    /// mégaoctets au mauvais endroit, sans la moindre erreur signalée. On passe donc la forme
    /// courte, celle qui n'a jamais d'espaces, et le produit vit sous <c>Program Files</c> : ce
    /// n'est pas un cas rare, c'est le cas normal.
    /// </para>
    /// </remarks>
    public static string Ou(string dossier) => $"/D={Court(dossier)}";

    /// <summary>
    /// Les endroits où un programme se répand quand personne ne l'en empêche.
    /// </summary>
    /// <remarks>
    /// <b>Une liste trop courte est pire que pas de mesure.</b> Elle ne se contente pas de rater une
    /// fuite : elle produit une affirmation confiante et fausse. Constaté sur LibreOffice — la mesure
    /// ne regardait que le profil de l'utilisateur, a conclu « aucune trace », et le raccourci était
    /// sur le Bureau public avec un dossier entier dans le menu Démarrer de la machine.
    ///
    /// <para>
    /// Les emplacements « tout le monde » comptent autant que ceux de l'utilisateur, et c'est
    /// contre-intuitif : on avait demandé une installation pour l'utilisateur seul. LibreOffice pose
    /// ses raccourcis à l'échelle de la machine quoi qu'on demande, et rien dans le protocole ne peut
    /// l'en empêcher — on peut seulement le voir et le dire.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Temoins() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    /// <summary>Cet installeur est-il un paquet Windows Installer ?</summary>
    public static bool EstMsi(string setup)
        => Path.GetExtension(setup).Equals(".msi", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ce qu'il faut dire à cet installeur-ci pour qu'il se taise et s'installe là.
    /// </summary>
    /// <remarks>
    /// <b>Il n'y a pas une porte, il y en a une par famille.</b> C'est le cœur du protocole : on ne
    /// force pas un installeur, on lui parle dans sa langue. NSIS — donc Electron et la plupart des
    /// programmes téléchargés — entend <c>/S</c> et <c>/D=</c>. Windows Installer n'entend ni l'un ni
    /// l'autre : il se pilote par <c>msiexec</c>, se tait sur <c>/qn</c>, et reçoit sa destination
    /// dans une propriété.
    ///
    /// <para>
    /// <b><c>MSIINSTALLPERUSER=1 ALLUSERS=2</c> n'est pas décoratif.</b> Un MSI s'installe par défaut
    /// pour toute la machine, donc sous élévation — et Windows refuse de composer l'environnement
    /// d'un processus qu'il élève. Sans ces deux propriétés, l'accueil ne peut pas avoir lieu du
    /// tout : c'est une installation pour l'utilisateur, ou rien.
    /// </para>
    ///
    /// <para>
    /// <b>Et le nom court ne sert que du côté NSIS.</b> <c>msiexec</c> accepte des guillemets, donc
    /// un chemin qui contient des espaces passe tel quel ; NSIS, lui, coupe au premier espace, ce qui
    /// a déjà créé un <c>C:\Program</c> à la racine du disque.
    /// </para>
    /// </remarks>
    public static ProcessStartInfo Commande(string setup, string dossier)
        => EstMsi(setup)
            ? new ProcessStartInfo("msiexec.exe")
            {
                Arguments = $"/i \"{setup}\" /qn /norestart "
                    + $"MSIINSTALLPERUSER=1 ALLUSERS=2 INSTALLLOCATION=\"{dossier}\"",
                WorkingDirectory = dossier,
            }
            : new ProcessStartInfo(setup)
            {
                Arguments = $"{Silence} {Ou(dossier)}",
                WorkingDirectory = dossier,
            };

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

        // Sans liste donnee, on prend la complete : oublier un temoin fait dire « aucune fuite » a
        // une mesure qui n'a pas regarde.
        temoins ??= Temoins();

        var avant = Empreinte(temoins);
        // Un espace qui survit à la forme courte est une impasse, et une impasse silencieuse : la
        // directive serait tronquée et l'installeur s'installerait ailleurs en annonçant une
        // réussite. On refuse ici plutôt que de le découvrir en trouvant un dossier inconnu à la
        // racine du disque.
        if (!EstMsi(setup) && Court(dossier).Contains(' ', StringComparison.Ordinal))
        {
            return new AccueilRapport(-1, dossier, 0,
            [
                $"Accueil impossible dans « {dossier} » : le chemin contient un espace et Windows "
                + "ne lui donne pas de forme courte sur ce disque. Les installeurs coupent leur "
                + "directive au premier espace et s'installent ailleurs sans le dire. Choisissez un "
                + "dossier sans espace.",
            ]);
        }

        var depart = Commande(setup, dossier);

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
    public static string Declarer(ProgrammeReconnu programme, string plugins, string porte = "")
    {
        // Une porte désignée par l'utilisateur tranche : c'est le seul cas où quelqu'un sait, et il
        // n'a pas à le redire une seconde fois.
        var ouvre = porte.Length > 0 ? porte : programme.Executable;

        if (porte.Length == 0 && programme.Forme != FormeProgramme.Application)
        {
            return $"{programme.Nom} : pas de tuile — {programme.Pourquoi}.";
        }

        if (ouvre.Length == 0)
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
            Author = "accueilli par SenSÉ",
            Licence = "celle de son éditeur",
            Glyph = "EA86",
            Hint = $"{programme.Nom}, accueilli dans Outils\\{relatif}. "
                + "Il vit dans le produit : le supprimer, c'est supprimer son dossier.",
            Surface = "tile",
            Does = PluginActions.Application,
            // Le chemin depuis le dossier de l'outil, pas seulement le nom du fichier : la porte
            // d'un programme est souvent d'un cran plus bas — « program\soffice.exe » chez
            // LibreOffice — et ne garder que le nom donnerait une cible qui n'existe pas.
            Target = $"{{tools}}\\{relatif}\\{Path.GetRelativePath(programme.Dossier, ouvre)}",
            Environnement = new EnvironnementOutil { Dossier = $"{{tools}}\\{relatif}" },
            Desinstallation = Desinstalleur(programme.Dossier),
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

            return $"{programme.Nom} : tuile posée, elle ouvrira {Path.GetFileName(ouvre)}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{programme.Nom} : tuile impossible — {exception.Message}";
        }
    }

    /// <summary>
    /// Comment le manifeste écrit est mis en forme.
    /// </summary>
    /// <remarks>
    /// Il doit ressembler à ceux qu'on écrit à la main, parce qu'on le lira à côté d'eux : mêmes
    /// noms de champs en minuscules, et rien de ce qui vaut sa valeur par défaut. Un fichier qui
    /// aligne quinze champs vides oblige à chercher les trois qui disent quelque chose.
    /// </remarks>
    private static readonly System.Text.Json.JsonSerializerOptions Ecriture = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
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

    /// <summary>
    /// Ce que Windows lancerait pour désinstaller le programme qui vit dans ce dossier.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci ferme, mesuré sur LibreOffice.</b> Éjecter effaçait le dossier avec
    /// <c>Directory.Delete</c>. Windows Installer, lui, gardait le produit enregistré avec notre
    /// dossier comme emplacement — donc la réinstallation suivante ne réinstallait rien : elle voyait
    /// un produit déjà présent. Et l'effacement s'était arrêté en chemin sur un fichier verrouillé,
    /// laissant huit cents mégaoctets d'un programme sans son exécutable principal. Le pire état
    /// possible : ni installé, ni absent.
    ///
    /// <para>
    /// <b>On ne devine pas la commande, on la lit.</b> Tout installeur digne du nom écrit sa propre
    /// ligne de désinstallation dans le registre, et c'est celle que Windows lance depuis son écran
    /// des programmes installés. La reprendre telle quelle marche pour Windows Installer comme pour
    /// NSIS, sans que le produit ait à connaître l'un ou l'autre.
    /// </para>
    ///
    /// <para>
    /// La correspondance se fait sur l'emplacement, jamais sur le nom : deux versions d'un même
    /// programme portent le même nom, et c'est celui qui vit <i>dans notre dossier</i> qu'il faut
    /// retirer — pas celui que l'utilisateur a installé ailleurs pour son compte.
    /// </para>
    /// </remarks>
    public static string Desinstalleur(string dossier)
    {
        // Le registre n'existe que sur Windows. Rendre « rien » ailleurs plutot que d'annoter toute
        // la chaine d'appel : l'accueil d'un programme Windows n'a de sens que sur Windows.
        if (!OperatingSystem.IsWindows())
        {
            return "";
        }

        var vise = Path.GetFullPath(dossier).TrimEnd(Path.DirectorySeparatorChar);

        foreach (var (racine, chemin) in Registres())
        {
            using var cle = racine.OpenSubKey(chemin);

            foreach (var nom in cle?.GetSubKeyNames() ?? [])
            {
                using var entree = cle!.OpenSubKey(nom);

                if (entree?.GetValue("UninstallString") is not string commande || commande.Length == 0)
                {
                    continue;
                }

                var ou = (entree.GetValue("InstallLocation") as string ?? "")
                    .Trim('"').TrimEnd(Path.DirectorySeparatorChar);

                if (ou.Length > 0 && ou.Equals(vise, StringComparison.OrdinalIgnoreCase))
                {
                    return commande;
                }
            }
        }

        return "";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<(Microsoft.Win32.RegistryKey Racine, string Chemin)> Registres()
    {
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        yield return (Microsoft.Win32.Registry.CurrentUser, uninstall);
        yield return (Microsoft.Win32.Registry.LocalMachine, uninstall);
        yield return (Microsoft.Win32.Registry.LocalMachine,
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
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
