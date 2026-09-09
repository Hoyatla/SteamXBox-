using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using SenSÉ.Plugins;
using SenSÉ.Tools.Documents;

namespace SenSÉ.Desktop.Tools;

/// <summary>
/// Turns the tools found on disk into tiles of the control centre.
/// </summary>
/// <remarks>
/// This is the loader the contract in <c>Plugins/README.md</c> was written for. Its measure of
/// success is stated there: it is right on the day it produces the same tile as the compiled
/// calculator and clipboard from a <c>plugin.json</c> alone, with nothing else in the environment
/// changing. It does — a loaded tool becomes a <see cref="ToolDescriptor"/>, the same record the
/// compiled ones are, and the grid cannot tell them apart.
///
/// <para>
/// <b>A tool is a folder.</b> Dropped in, it is installed; thrown away, it is uninstalled. Nothing
/// is compiled, nothing is registered elsewhere, and nothing is left behind — which is the only
/// definition of detachable that can be checked.
/// </para>
///
/// <para>
/// <b>No code runs from a manifest.</b> A tool names an action the host already performs for
/// itself; it cannot bring one. So a tool downloaded from anywhere can do no more than what its
/// manifest shows to whoever reads it, and reading it needs no programmer.
/// </para>
/// </remarks>
public static class PluginTools
{
    /// <summary>Where tools live, beside the executable.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Plugins");

    /// <summary>
    /// The tools SenSÉ ships with, which its own interface will not delete.
    /// </summary>
    /// <remarks>
    /// These are what a customer paid for. They can be switched off and they can be archived —
    /// both reversible, both the user's business — but the uninstall screen does not offer to
    /// destroy them. Somebody who really means it deletes the folder in the file manager, which is
    /// a deliberate act outside the product rather than one click inside it.
    ///
    /// <para>
    /// Known by the host rather than declared in a manifest, and that is the point. A field saying
    /// "I am a default tool" would be written by whoever wrote the manifest, so any downloaded tool
    /// could make itself undeletable from the screen meant to remove it.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> Shipped { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "convertir-document",
        "moniteur",
    };

    /// <summary>
    /// Every usable tool on disk, as tiles.
    /// </summary>
    /// <remarks>
    /// A tool that is missing, corrupt or whose manifest cannot be read is skipped with a line in
    /// the log — never an error that takes the environment or the other tools down with it. That is
    /// the second rule of the contract, and it is enforced here rather than trusted.
    /// </remarks>
    public static IReadOnlyList<ToolDescriptor> Load(Action<string>? log = null)
    {
        var scan = PluginCatalog.Scan(Folder);

        foreach (var rejection in scan.Rejected)
        {
            log?.Invoke($"plugin refused: {Path.GetFileName(rejection.Directory)}: {rejection.Reason}");
        }

        var tools = new List<ToolDescriptor>();

        // Les outils qu'un classeur réunit n'ont plus leur propre tuile : le classeur les y
        // remplace. C'est sa cible qui le dit, de sorte qu'il n'y a pas deux listes à tenir
        // d'accord — retirer un outil du classeur lui rend sa tuile, sans rien d'autre à toucher.
        var reunis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var classeur in scan.Loaded.Where(m =>
                     m.Does.Equals(PluginActions.Atelier, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var id in PluginActions.Reunis(classeur.Target))
            {
                reunis.Add(id);
            }
        }

        foreach (var manifest in scan.Loaded.Where(m => m.Kind == PluginCategory.Tool))
        {
            if (reunis.Contains(manifest.Id))
            {
                log?.Invoke($"plugin tool grouped: {manifest.Id} appears in a workbench.");

                continue;
            }

            // Switched off in the uninstall screen, or shipped idle and never switched on. The
            // folder is untouched either way — this is the difference between "not now" and "gone",
            // and the user should not have to move files to say which one they mean.
            if (!PluginLifecycle.IsEnabled(manifest.Id, manifest.Enabled))
            {
                log?.Invoke($"plugin tool skipped: {manifest.Id} is switched off.");
                continue;
            }

            Rouille(manifest, log);

            tools.Add(new ToolDescriptor(
                manifest.Id,
                manifest.Name,
                manifest.Hint,
                Glyphe(manifest.Glyph),
                Run: environment => Run(manifest, environment, log),
                Icon: Icone(manifest.Icon),

                // Seul le moniteur d'activité a quelque chose à compter, et c'est l'hôte qui le
                // sait — pas le manifeste. Un champ « compteur » déclaré dans le JSON serait une
                // promesse qu'aucun outil ne pourrait tenir : compter suppose de calculer.
                Compte: manifest.Does.Equals(PluginActions.Activite, StringComparison.OrdinalIgnoreCase)
                    ? SenSÉ.Desktop.Activite.ActiviteWindow.Compte
                    : null));

            log?.Invoke($"plugin tool loaded: {manifest.Id} v{manifest.Version} ({manifest.Surface}).");
        }

        return tools;
    }

    /// <summary>
    /// Signale au journal ce qu'un outil fige, sans jamais l'empêcher de tourner.
    /// </summary>
    /// <remarks>
    /// <b>Avertir, et surtout pas refuser.</b> Un outil qui fonctionne aujourd'hui doit continuer de
    /// fonctionner ; le figement n'est pas une faute présente, c'est une panne future. Le refuser
    /// casserait ce qui marche au nom de ce qui cassera peut-être — et l'utilisateur perdrait un
    /// outil sans avoir rien fait.
    ///
    /// <para>
    /// Le jour où le fichier disparaîtra, l'outil échouera au clic sur un nom que plus personne ne
    /// reconnaîtra. Ce qui change ici, c'est que la raison sera déjà écrite quelque part, datée du
    /// démarrage, au lieu d'être à reconstituer.
    /// </para>
    ///
    /// <para>
    /// Les outils livrés sont examinés comme les autres, bien qu'une épreuve les garde déjà à la
    /// compilation : ce qui est éprouvé est le dépôt, ce qui tourne est une copie installée, et rien
    /// n'empêche quelqu'un d'avoir édité la seconde.
    /// </para>
    /// </remarks>
    private static void Rouille(PluginManifest manifeste, Action<string>? log)
    {
        if (log is null)
        {
            return;
        }

        try
        {
            foreach (var gel in SenSÉ.Tools.Generation.FluxGel.Examiner(manifeste, Resoudre))
            {
                log($"plugin rouille : {gel}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un avertissement qui empêche un outil de se charger serait pire que le défaut qu'il
            // annonce.
            log($"plugin rouille : {manifeste.Id} non examiné ({exception.Message})");
        }
    }

    /// <summary>
    /// La geometrie nommee par le manifeste, si le dictionnaire d'icones la porte.
    /// </summary>
    /// <remarks>
    /// Une cle de ressource, jamais un chemin de fichier : WPF ne lit pas le SVG, et embarquer un
    /// lecteur pour dessiner une icone couterait plus que l'icone ne vaut. Les traces sont convertis
    /// une fois dans <c>ControlCentre/PluginIcons.xaml</c>, et un manifeste y renvoie par sa cle.
    ///
    /// <para>
    /// Une cle absente rend null, donc la tuile retombe sur son glyphe de police : un outil dont
    /// l'icone manque reste un outil utilisable.
    /// </para>
    /// </remarks>
    private static System.Windows.Media.Geometry? Icone(string cle)
        => cle.Trim().Length == 0
            ? null
            : System.Windows.Application.Current?.TryFindResource(cle.Trim()) as System.Windows.Media.Geometry;

    /// <summary>
    /// Le glyphe d'un manifeste, converti en caractere.
    /// </summary>
    /// <remarks>
    /// Un outil compile porte le caractere lui-meme, comme les constantes de <c>Glyphs</c>. Un
    /// manifeste, lui, ne peut porter que son numero ecrit en texte — <c>"E8B5"</c> — puisque c'est
    /// du JSON. Passe tel quel a la tuile, ce numero est dessine comme du texte par une police
    /// d'icones qui n'a rien a ces positions : la tuile sort sans icone, et ca ressemble a une icone
    /// manquante alors que c'est un numero non lu.
    ///
    /// <para>
    /// Trois ecritures acceptees — <c>E8B5</c>, <c>U+E8B5</c>, ou le caractere lui-meme. Un numero
    /// illisible rend une chaine vide plutot qu'un carre de remplacement : une tuile sans icone reste
    /// utilisable, une tuile ornee d'un glyphe faux fait douter du reste.
    /// </para>
    /// </remarks>
    private static string Glyphe(string declare)
    {
        declare = declare.Trim();

        if (declare.Length <= 1)
        {
            return declare;
        }

        return int.TryParse(
            declare.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? declare[2..] : declare,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var point)
            ? char.ConvertFromUtf32(point)
            : "";
    }

    /// <summary>
    /// Lance un executable externe (pattern Atelier : Process.Start + inscription au job parent).</summary>
    private static string LancerExterne(string chemin, Action<string>? log, string label)
    {
        if (!File.Exists(chemin))
        {
            log?.Invoke($"{label} est introuvable : {chemin}");
            return $"{label} introuvable.";
        }
        try
        {
            var procR = Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = false });
            JobEnfants.Inscrire(procR);
            log?.Invoke($"{label} lance.");
            return "";
        }
        catch (Exception ex)
        {
            log?.Invoke($"{label} : {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Ouvre un outil comme un clic sur sa tuile le ferait.
    /// </summary>
    /// <remarks>
    /// Le même aiguillage que <see cref="Run"/>, sans la fenêtre d'environnement dont un panneau
    /// n'a que faire. Existe pour l'assistant : « ouvre l'agrandisseur de vidéo » doit poser le
    /// panneau devant l'utilisateur, pas lancer un travail dans son dos.
    /// </remarks>
    public static string Ouvrir(PluginManifest manifeste, Action<string>? log)
    {
        // Un outil réuni s'ouvre dans son classeur, pas dans une fenêtre à part.
        //
        // L'assistant, à qui l'on demande « crée une image », ouvrait le panneau isolé de
        // « Créer une image » — une fenêtre que la grille ne propose plus, à côté du classeur qui
        // porte les quatre outils du même travail. L'utilisateur se retrouvait devant une fenêtre
        // qu'il n'avait aucun moyen de retrouver ensuite, et les onglets voisins lui restaient
        // invisibles.
        if (Classeur(manifeste.Id) is { } classeur)
        {
            // L'onglet demandé est mis au premier plan : ouvrir le classeur sur son premier onglet
            // quand on a demandé le troisième laisserait l'utilisateur chercher.
            return Atelier.AtelierWindow.Ouvrir(classeur.Target, log, manifeste.Id);
        }

        return manifeste.Surface.Equals("panel", StringComparison.OrdinalIgnoreCase)
            ? PluginPanelWindow.Open(manifeste, log)
            : Perform(manifeste.Does, manifeste.Target, log, manifeste.Environnement);
    }

    /// <summary>Cette dépendance est-elle installée sur cette machine ?</summary>
    /// <remarks>
    /// Le manifeste dit où on la cherche, l'hôte constate — la règle du dépôt, appliquée ici pour
    /// que le produit sache s'adapter à ce qui est présent sans qu'aucun chemin ne soit écrit dans
    /// le code. C'est ainsi que la présence de ComfyUI Desktop suffit à faire basculer le générateur
    /// en « installation à part », sans réglage à cocher.
    /// </remarks>
    public static bool Presente(string identifiant)
    {
        var manifeste = PluginCatalog.Scan(Folder).Loaded
            .FirstOrDefault(m => m.Id.Equals(identifiant, StringComparison.OrdinalIgnoreCase));

        return manifeste is not null
               && manifeste.Target.Length > 0
               && File.Exists(Resoudre(manifeste.Target));
    }

    /// <summary>Le classeur qui réunit cet outil, s'il y en a un.</summary>
    private static PluginManifest? Classeur(string outil)
        => PluginCatalog.Scan(Folder).Loaded.FirstOrDefault(m =>
            m.Does.Equals(PluginActions.Atelier, StringComparison.OrdinalIgnoreCase)
            && PluginActions.Reunis(m.Target)
                .Any(id => id.Equals(outil, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Runs a declarative tool, and returns a line for the status area.</summary>
    private static string Run(PluginManifest manifest, Window environment, Action<string>? log)
    {
        if (manifest.Surface.Equals("panel", StringComparison.OrdinalIgnoreCase))
        {
            return PluginPanelWindow.Open(manifest, log);
        }

        return Perform(manifest.Does, manifest.Target, log, manifest.Environnement);
    }

    /// <summary>
    /// Carries out one named action.
    /// </summary>
    /// <remarks>
    /// The whole of what a level-one tool can cause to happen. Each of these is something the
    /// environment already does on its own tiles — which is the test the contract sets before an
    /// action may be published, and the reason this list is short and stays short.
    /// </remarks>
    /// <summary>
    /// Remplace <c>{nom:chemin}</c> par le nom de fichier sans son extension.
    /// </summary>
    /// <remarks>
    /// Complete <c>{dossier:...}</c> : avec les deux, un manifeste peut nommer un fichier que
    /// l'outil va produire — « le meme dossier, le meme nom, un suffixe » — sans que l'hote ait a
    /// connaitre les conventions de nommage du programme appele.
    /// </remarks>
    private static string Noms(string cible)
        => System.Text.RegularExpressions.Regex.Replace(
            cible,
            @"\{nom:([^}]+)\}",
            trouve =>
            {
                try
                {
                    return Path.GetFileNameWithoutExtension(trouve.Groups[1].Value.Trim('"'));
                }
                catch (Exception)
                {
                    return "";
                }
            });

    /// <summary>
    /// Remplace <c>{dossier:chemin}</c> par le dossier qui contient ce chemin.
    /// </summary>
    /// <remarks>
    /// Ce qui permet a un outil d'ecrire son resultat A COTE du fichier que l'utilisateur a designe,
    /// plutot que dans un dossier a lui. C'est la regle que la conversion de documents et la
    /// reconnaissance de caracteres suivent deja, et pour une raison qui vaut ici aussi : le dossier
    /// qu'on a designe est le seul qu'on ait deja accepte, et c'est celui ou on ira chercher le
    /// resultat.
    ///
    /// <para>
    /// Ecrit comme un repere general plutot que comme un cas particulier de l'agrandisseur : tout
    /// outil qui produit un fichier a partir d'un fichier en aura besoin, et il vaut mieux un
    /// vocabulaire qui se lit qu'une option de plus par outil.
    /// </para>
    /// </remarks>
    private static string Dossiers(string cible)
        => System.Text.RegularExpressions.Regex.Replace(
            cible,
            @"\{dossier:([^}]+)\}",
            trouve =>
            {
                try
                {
                    return Path.GetDirectoryName(trouve.Groups[1].Value.Trim('"')) ?? "";
                }
                catch (Exception)
                {
                    // Un chemin illisible rend une chaine vide : l'outil dira que sa cible manque,
                    // ce qui est plus clair qu'un dossier invente.
                    return "";
                }
            });

    /// <summary>
    /// Remplace les reperes d'un manifeste par des chemins reels.
    /// </summary>
    /// <remarks>
    /// Ce qui rend un outil portable. Un manifeste qui ecrit
    /// <c>C:\Users\Machin\Downloads\Real-ESRGAN\inference.py</c> ne marche que sur la machine qui l'a
    /// ecrit : copie sur une cle, sur un autre poste, ou simplement range ailleurs, il ne trouve plus
    /// rien. C'est deja arrive a poppler le 18 aout, dont le dossier a disparu et a emporte la
    /// reconnaissance de caracteres avec lui.
    ///
    /// <list type="bullet">
    ///   <item><c>{app}</c> — le dossier du produit, celui qui contient l'executable.</item>
    ///   <item><c>{tools}</c> — <c>Outils\</c> a cote de lui, ou se deposent les programmes tiers.</item>
    ///   <item><c>{documents}</c> — les documents de l'utilisateur, pour les sorties.</item>
    /// </list>
    ///
    /// <para>
    /// Un chemin absolu reste accepte tel quel : un administrateur qui installe un programme dans
    /// Program Files doit pouvoir le nommer, et forcer le repere ne lui apporterait rien.
    /// </para>
    /// </remarks>
    public static string Resoudre(string cible)
        => Noms(Dossiers(cible
            .Replace("{app}", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)
            .Replace("{tools}", Path.Combine(
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Outils"),
                StringComparison.OrdinalIgnoreCase)
            .Replace("{documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                StringComparison.OrdinalIgnoreCase)

            // Là où Windows installe ce qui n'est pas pour toute la machine — dont ComfyUI Desktop.
            // Sans ce repère, un manifeste devrait écrire « C:\Users\Machin\AppData\Local\… » et ne
            // marcherait que sur le poste de celui qui l'a écrit.
            .Replace("{local}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Ce qu'une action veut dire à l'utilisateur pendant qu'elle travaille.
    /// </summary>
    /// <remarks>
    /// <b>Une tuile qui met deux minutes doit donner signe de vie.</b> Le démarrage du générateur
    /// demande cent trente-cinq secondes ; l'utilisateur a cliqué, n'a rien vu, a cliqué deux fois
    /// de plus, puis a quitté au bout de vingt-neuf secondes en concluant que l'outil était cassé.
    /// Il ne l'était pas — il chargeait.
    ///
    /// <para>
    /// Le journal recevait pourtant l'avancement seconde par seconde. Il lui manquait un chemin
    /// jusqu'à l'écran, et c'est tout ce que cet événement fournit. Les verbes n'ont rien à changer :
    /// ce qu'ils disaient déjà au journal arrive maintenant aussi devant les yeux.
    /// </para>
    /// </remarks>
    public static event Action<string>? Annonce;

    public static string Perform(
        string does,
        string target,
        Action<string>? log,
        EnvironnementOutil? environnement = null)
    {
        // L'issue passe au journal, et pas seulement au panneau.
        //
        // Le journal montrait l'avancement — « Génération en cours, 360 s écoulées » — puis plus
        // rien : le message de fin partait vers le panneau, qui se referme. Relire une session le
        // lendemain ne permettait donc pas de distinguer une réussite d'un échec, ni de savoir où
        // le fichier avait atterri. Le défaut valait pour tous les verbes, pas seulement celui qui
        // l'a révélé, et c'est pourquoi il se corrige ici, où tous passent.
        // Ce qu'un verbe dit part au journal et à l'écran.
        //
        // Les verbes ne disent au journal que des phrases destinées à être lues — « Démarrage en
        // cours, 32 s écoulées », « Terminé en 143 s : … ». Les lignes techniques, elles, viennent
        // d'ici et du chargeur, jamais des verbes. Le même canal sert donc aux deux sans qu'il faille
        // trier quoi que ce soit.
        void Dire(string phrase)
        {
            log?.Invoke(phrase);
            Annonce?.Invoke(phrase);
        }

        var dit = Faire(does, target, Dire, environnement);

        if (dit.Length > 0)
        {
            Dire($"issue de « {does} » : {dit.ReplaceLineEndings(" ")}");
        }

        return dit;
    }

    private static string Faire(
        string does,
        string target,
        Action<string>? log,
        EnvironnementOutil? environnement)
    {
        target = Resoudre(target);

        try
        {
            switch (does.ToLowerInvariant())
            {
                case PluginActions.WindowsSetting:
                    Start($"ms-settings:{target}");
                    return "";

                case PluginActions.Application:
                    return Ouvrir(target, environnement);

                case PluginActions.Path:
                    Start(target);
                    return "";

                case PluginActions.Search:
                    SenSÉ.Core.Runtime.DesktopSignal.Raise(SenSÉ.Core.Runtime.DesktopSignal.Search);
                    return "";

                case PluginActions.ClearScreen:
                    Input.DesktopWindows.Toggle(log);
                    return "";

                case PluginActions.Python:
                    return Python(target, log);

                case PluginActions.Convert:
                    return Convert(target, log);

                case PluginActions.Video:
                    return Video(target, log);

                case PluginActions.Image:
                    return Image(target, log);

                case PluginActions.Generation:
                    return SenSÉ.Tools.Generation.ComfyServer.Ouvrir(log);

                case PluginActions.Flux:
                    return SenSÉ.Tools.Generation.FluxTravail.Lancer(target, log);

                case PluginActions.Sequence:
                    return SenSÉ.Tools.Generation.SequenceAnimee.Lancer(target, log);

                case PluginActions.Assistant:
                    return Assistant.AssistantWindow.Ouvrir(log);

                case PluginActions.Activite:
                    return SenSÉ.Desktop.Activite.ActiviteWindow.Ouvrir(log);

                case PluginActions.Modeles:
                    return SenSÉ.Desktop.Modeles.ModelesWindow.Ouvrir(log);

                case PluginActions.Atelier:
                    return Atelier.AtelierWindow.Ouvrir(target, log);

                case PluginActions.Editor:
                    return LancerExterne(Resoudre("{tools}\\Editeur\\SenSÉ.Editeur.exe"), log, "SenSÉ.Editeur");

                default:
                    // Unreachable through the catalogue, which refuses an unknown action at load.
                    // Kept because an action removed from the vocabulary would otherwise fail here
                    // silently for anyone whose tool still names it.
                    log?.Invoke($"plugin action unknown at run time: '{does}'.");
                    return $"Action inconnue : {does}";
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin action '{does}' failed: {exception.GetType().Name}: {exception.Message}");
            return exception.Message;
        }
    }

    /// <summary>
    /// Converts a document the user designated, and shows where the result went.
    /// </summary>
    /// <remarks>
    /// The target is <c>path|format</c> — the document, then what to turn it into. Written beside
    /// the original rather than somewhere of the host's choosing: the user pointed at that folder,
    /// so it is the one place they already agreed to, and it is where they will look for the result.
    /// </remarks>
    /// <summary>
    /// Lance un script Python, quand la machine en a un interpreteur.
    /// </summary>
    /// <remarks>
    /// La cible s'ecrit <c>chemin\du\script.py|arguments</c>. Les arguments sont decoupes sur les
    /// espaces, hors guillemets, pour qu'un chemin qui en contient reste entier.
    ///
    /// <para>
    /// <b>Trois gardes, et elles ne sont pas decoratives.</b> Le fichier doit exister, finir par
    /// <c>.py</c>, et l'appel complet part dans le journal. C'est le premier verbe qui execute un
    /// programme que le produit ne fournit pas : la propriete qui rendait un manifeste sur — ne rien
    /// pouvoir faire de plus que ce qu'il montre — ne tient plus tout a fait ici, donc ce qui est
    /// lance doit au moins etre nommable, verifiable et trace.
    /// </para>
    ///
    /// <para>
    /// L'interpreteur est cherche sur le PATH, puis via le lanceur <c>py</c> de Windows. Absent, le
    /// verbe se refuse en le disant plutot que d'echouer sans raison lisible — la meme regle que
    /// LibreOffice, tesseract et poppler.
    /// </para>
    /// </remarks>
    private static string Python(string target, Action<string>? log)
    {
        var parts = Segments.Decouper(target, 3);
        var script = parts[0].Trim();

        if (script.Length == 0 || !script.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            return "Cible refusée : un script Python est attendu.";
        }

        if (!File.Exists(script))
        {
            return $"Script introuvable : {Path.GetFileName(script)}";
        }

        var interpreteur = Interpreteur();

        if (interpreteur is null)
        {
            return "Python n'est pas installé sur cette machine : cet outil en a besoin.";
        }

        // Rien qui puisse ouvrir une seconde commande.
        //
        // La ligne est assemblee puis remise a cmd, et les chemins qu'elle porte viennent de
        // l'utilisateur : c'est lui qui a designe le fichier dans le panneau, et le manifeste l'a
        // recopie dans ses arguments avec {dossier:} et {nom:}. Une video nommee « vacances &
        // montagne.mp4 » suffisait alors a couper la ligne en deux — cmd traite le « & » comme un
        // separateur, la premiere moitie echoue, et « montagne.mp4 » part comme une commande.
        //
        // Refuse plutot que echappe, et le message le dit. Echapper demanderait de deviner ce que le
        // manifeste voulait citer et ce qu'il voulait interpreter ; refuser ne demande rien, ne peut
        // pas se tromper, et laisse a l'utilisateur le seul geste qui repare vraiment — renommer son
        // fichier.
        foreach (var libre in new[] { parts.Count > 1 ? parts[1] : "", parts.Count > 2 ? parts[2] : "" })
        {
            if (Perilleux(libre) is { } piege)
            {
                log?.Invoke($"python refuse : {piege} hors guillemets dans « {libre} »");

                return $"Cette commande contient un caractère que l'invite de Windows interprète "
                    + $"({piege}) : renommez le fichier ou le dossier concerné.";
            }
        }

        // Lance a travers cmd, et pas directement l'interpreteur.
        //
        // Trois choses que le lancement direct ne donnait pas, et qui ont chacune coute une fausse
        // piste le 19 aout :
        //
        //   - Une fenetre qui dit tout de suite ce qu'elle fait. PyTorch met une quinzaine de
        //     secondes a se charger sans rien afficher : une console noire pendant ce temps est
        //     indiscernable d'un plantage, et l'essai a ete interrompu avant meme de commencer.
        //   - Python en mode non tamponne (-u), sinon la barre de progression n'apparait qu'a la
        //     fin, ce qui annule l'interet de la fenetre.
        //   - Une pause a la fin, pour que le resultat et les erreurs restent lisibles au lieu de
        //     disparaitre avec la fenetre. C'est ce message qui manquait quand il a fallu deviner.
        // Une seconde passe ffmpeg, et RIEN D'AUTRE.
        //
        // Le manifeste peut ecrire un troisieme segment « script | arguments | ffmpeg:<arguments> ».
        // Ce qui suit n'est jamais un programme choisi par le manifeste : c'est toujours ffmpeg,
        // trouve par l'hote, et le manifeste ne fournit que ses arguments. Autoriser un programme
        // quelconque ici rendrait un manifeste capable de tout lancer, ce qui detruirait la seule
        // propriete qui rend un outil telechargeable sur — ne pouvoir faire que ce qu'il montre.
        //
        // Le nom du fichier a reprendre est ecrit par le manifeste lui-meme, avec {dossier:} et
        // {nom:} : l'hote n'a donc pas a connaitre les conventions de nommage du programme appele.
        var apres = "";

        if (parts.Count > 2 && parts[2].TrimStart().StartsWith("ffmpeg:", StringComparison.OrdinalIgnoreCase))
        {
            var ffmpeg = Trouver("ffmpeg");

            if (ffmpeg is null)
            {
                apres = "& echo. & echo ffmpeg est introuvable : le fichier reste au format d'origine.";
            }
            else
            {
                var arguments = parts[2].TrimStart()["ffmpeg:".Length..].Trim();
                apres = $"& echo. & echo ---- Conversion du format ---- & \"{ffmpeg}\" -hide_banner -loglevel warning -y {arguments}";
            }
        }

        var commande =
            $"title Outil SenSÉ & echo Demarrage, PyTorch met quelques secondes a se charger... & echo. "
            + $"& \"{interpreteur}\" -u \"{script}\" {(parts.Count > 1 ? parts[1] : "")} "
            + apres
            + " & echo. & echo ---- Termine. Fermez cette fenetre. ---- & pause";

        var start = new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c " + commande,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(script) ?? "",
        };

        log?.Invoke($"python: {interpreteur} {script} {(parts.Count > 1 ? parts[1] : "")}");

        var procL = Process.Start(start);
        JobEnfants.Inscrire(procL);
        return $"Lancé : {Path.GetFileName(script)}";
    }

    /// <summary>
    /// Le premier caractère de commande que l'invite de Windows lirait hors guillemets, s'il y en a.
    /// </summary>
    /// <remarks>
    /// Hors guillemets seulement : entre guillemets, cmd ne voit qu'un texte, et c'est précisément
    /// ainsi que le manifeste cite un chemin. Un « &amp; » dans <c>"C:\films\vacances &amp; montagne.mp4"</c>
    /// est inoffensif ; le même sans guillemets coupe la ligne.
    ///
    /// <para>
    /// Les cinq retenus sont ceux qui enchaînent ou détournent : <c>&amp;</c> et <c>|</c> pour une
    /// commande de plus, <c>&lt;</c> et <c>&gt;</c> pour une redirection, <c>^</c> pour l'échappement
    /// de cmd, qui sert à reconstruire les précédents.
    /// </para>
    /// </remarks>
    private static string? Perilleux(string ligne)
    {
        var entreGuillemets = false;

        foreach (var lettre in ligne)
        {
            if (lettre == '"')
            {
                entreGuillemets = !entreGuillemets;

                continue;
            }

            if (!entreGuillemets && lettre is '&' or '|' or '<' or '>' or '^')
            {
                return lettre.ToString();
            }
        }

        return null;
    }

    /// <summary>Cherche un programme sur le PATH, comme les autres outils detectes.</summary>
    private static string? Trouver(string nom)
    {
        foreach (var dossier in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidat = Path.Combine(dossier.Trim(), nom + ".exe");

                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (Exception)
            {
                // Une entree de PATH malformee ne doit pas arreter la recherche des suivantes.
            }
        }

        return null;
    }

    /// <summary>
    /// Cet executable est-il vraiment un interpreteur Python ?
    /// </summary>
    /// <remarks>
    /// Il repond « Python 3.x » en une seconde, ou il n'est pas ce qu'il pretend. Une question posee
    /// plutot qu'une deduction sur le nom ou la taille du fichier : c'est ce qui separe l'alias
    /// d'execution d'un Python installe en version « appli », qui marche, du raccourci de la boutique,
    /// qui ouvre un magasin. Les deux pesent zero octet et portent le meme nom.
    /// </remarks>
    private static bool Repond(string executable)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            start.ArgumentList.Add("--version");

            using var process = Process.Start(start);

            if (process is null)
            {
                return false;
            }

            // Les deux sorties se vident ensemble : voir Tuyaux.Vider. Python annonce sa version
            // sur l'une ou l'autre selon les versions, d'où la concaténation.
            var (sortie, erreur) = SenSÉ.Tools.Serveurs.Tuyaux.Vider(
                process, TimeSpan.FromSeconds(3));

            return process.HasExited
                   && (sortie + erreur).TrimStart()
                       .StartsWith("Python 3", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Les emplacements d'installation habituels de Python, essayes avant le PATH.
    /// </summary>
    /// <remarks>
    /// Mesure du 19 aout : Python installe dans le profil de l'utilisateur, et pourtant introuvable —
    /// le faux interpreteur du Microsoft Store, pose par Windows dans WindowsApps, passe AVANT lui
    /// dans le PATH. Chercher d'abord la ou l'installeur pose vraiment les choses evite de dependre
    /// de l'ordre d'une variable d'environnement que le produit ne controle pas.
    /// </remarks>
    private static IEnumerable<string> PythonInstalle()
    {
        // Le Python embarque du produit passe avant tout le reste : un dossier que SenSÉ porte
        // avec lui, sans registre ni PATH ni alias de Windows. C'est le seul interpreteur dont le
        // produit puisse garantir la presence et la version, et le seul qui survivra au jour ou
        // l'hote ne sera plus Windows — un dossier se remplace, un etat systeme ne se deplace pas.
        //
        // Sa licence PSF autorise la redistribution, y compris dans un produit ferme, a condition
        // d'inclure son texte. C'est ce qui le distingue de poppler et de ComfyUI, sous GPL, qui ne
        // peuvent qu'etre detectes.
        var embarque = Path.Combine(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Outils", "Python", "python.exe");

        if (File.Exists(embarque))
        {
            yield return embarque;
        }

        var racines = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var racine in racines)
        {
            if (!Directory.Exists(racine))
            {
                continue;
            }

            // Les versions les plus recentes d'abord : Python314 avant Python39.
            foreach (var dossier in Directory.EnumerateDirectories(racine, "Python3*")
                         .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var candidat = Path.Combine(dossier, "python.exe");

                if (File.Exists(candidat))
                {
                    yield return candidat;
                }
            }
        }
    }

    /// <summary>Un interpreteur Python, sur le PATH ou par le lanceur de Windows.</summary>
    private static string? Interpreteur()
    {
        foreach (var installe in PythonInstalle())
        {
            return installe;
        }

        foreach (var nom in new[] { "python.exe", "py.exe" })
        {
            foreach (var dossier in (Environment.GetEnvironmentVariable("PATH") ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidat = Path.Combine(dossier.Trim(), nom);

                    // Le faux interpreteur du Microsoft Store, distingue d'un vrai par son
                    // COMPORTEMENT et non par sa taille.
                    //
                    // Windows pose dans WindowsApps un raccourci de zero octet qui ouvre la boutique
                    // au lieu d'executer quoi que ce soit. Mais un Python reellement installe en
                    // version « appli » y pose un alias d'execution qui pese zero octet lui aussi, et
                    // qui marche. Ecarter sur la taille rejetait donc les deux — le piege et
                    // l'installation legitime.
                    //
                    // On lui demande sa version : un interpreteur repond « Python 3.x », le
                    // raccourci ouvre un magasin et ne repond rien.
                    if (candidat.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                        && !Repond(candidat))
                    {
                        continue;
                    }


                    if (File.Exists(candidat))
                    {
                        return candidat;
                    }
                }
                catch (Exception)
                {
                    // Une entree de PATH malformee ne doit pas arreter la recherche des suivantes.
                }
            }
        }

        return null;
    }

    /// <summary>Decoupe une ligne d'arguments en respectant les guillemets.</summary>
    private static IEnumerable<string> Decouper(string ligne)
    {
        var courant = new System.Text.StringBuilder();
        var entreGuillemets = false;

        foreach (var lettre in ligne)
        {
            if (lettre == '"')
            {
                entreGuillemets = !entreGuillemets;
                continue;
            }

            if (lettre == ' ' && !entreGuillemets)
            {
                if (courant.Length > 0)
                {
                    yield return courant.ToString();
                    courant.Clear();
                }

                continue;
            }

            courant.Append(lettre);
        }

        if (courant.Length > 0)
        {
            yield return courant.ToString();
        }
    }

    /// <summary>Agrandit une image : la cible porte le fichier puis les trois réglages.</summary>
    /// <remarks>
    /// L'hôte se contente de découper la cible et de passer la main ; tout ce qui décide appartient
    /// à <c>SenSÉ.Tools</c>, où c'est atteignable par les tests. C'est la même répartition que
    /// pour la conversion de documents.
    /// </remarks>
    private static string Image(string target, Action<string>? log)
    {
        var parts = Segments.Decouper(target);

        if (parts.Count < 4 || parts[0].Length == 0)
        {
            return "Choisissez une image, puis lancez.";
        }

        return SenSÉ.Tools.Images.ImageUpscale.Agrandir(
            parts[0], parts[1], parts[2], parts[3], log);
    }

    /// <summary>
    /// Agrandit une vidéo : la cible porte le fichier puis les cinq réglages du panneau.
    /// </summary>
    /// <remarks>
    /// Même répartition que pour l'image, juste au-dessus : l'hôte découpe et passe la main.
    /// </remarks>
    private static string Video(string target, Action<string>? log)
    {
        var parts = Segments.Decouper(target);

        if (parts.Count < 6 || parts[0].Length == 0)
        {
            return "Choisissez une vidéo, puis lancez.";
        }

        return SenSÉ.Tools.Video.VideoUpscale.Agrandir(
            parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], log);
    }

    private static string Convert(string target, Action<string>? log)
    {
        var parts = Segments.Decouper(target, 2);

        if (parts.Count < 2 || parts[0].Length == 0)
        {
            return "Choisissez un document et un format.";
        }

        var input = parts[0];
        var format = parts[1];

        var folder = Path.GetDirectoryName(input);

        if (string.IsNullOrEmpty(folder))
        {
            return "Le document choisi n'a pas de dossier.";
        }

        // La reconnaissance de caracteres, avant tout le reste : c'est la seule route qui rend un
        // PDF image lisible, et aucune des autres ne peut faire quoi que ce soit d'un document sans
        // couche texte. Mesure sur le PDF de test du projet : l'import Writer en tirait 14 octets,
        // cette route en tire 5 301 caracteres.
        if (SenSÉ.Tools.Documents.PdfOcr.Handles(input, format))
        {
            return SenSÉ.Tools.Documents.PdfOcr.Convert(input, folder);
        }

        // Le rendu de page, avant les routes qui reconstruisent : il ne reconstruit rien, il rend la
        // page telle qu'un lecteur la montre — masques de transparence appliques, ce que ni PdfPig ni
        // pdfimages ne font. Demande explicitement par son format, jamais choisi a la place de
        // l'utilisateur : le document produit est fidele a l'oeil et non editable, et cet arbitrage
        // lui appartient.
        if (SenSÉ.Tools.Documents.PdfPageToDocument.Handles(input, format))
        {
            var rendu = SenSÉ.Tools.Documents.PdfPageToDocument.Convert(input, folder);

            if (!rendu.Worked)
            {
                log?.Invoke($"rendu de page echoue: {rendu.Problem}");
                return rendu.Problem;
            }

            Search.ShellFolders.Reveal(rendu.Produced, log);

            // Le chemin entier, et non le seul nom : c'est ce qui rend le fichier transmissible à
            // l'outil suivant. Un nom nu ne désigne rien pour qui ne sait pas dans quel dossier
            // chercher — et l'assistant, lui, ne le sait pas.
            return $"Écrit : {rendu.Produced}";
        }

        // A PDF takes the text route, never LibreOffice. Its PDF import goes through Draw and
        // rebuilds the page as a drawing: measured on one real document, ten thousand floating
        // objects for four hundred paragraphs, and Word took seconds per turn of the mouse wheel.
        // Taking the text instead loses the layout and gives a document that can actually be edited.
        // A slide is a fixed rectangle holding boxes at coordinates, which is what a PDF page is, so
        // this route keeps the layout instead of throwing it away. Same input, same reader, opposite
        // decision — because the target is different, not because one of them is better written.
        var result = PdfToPresentation.Handles(input, format)
            ? PdfToPresentation.Convert(input, format, folder)
            : PdfToDocument.Handles(input, format)
                ? PdfToDocument.Convert(input, format, folder)
                // Extraction rather than conversion, and the only route here that infers something
                // the file never held. Offered because the alternative was a spreadsheet holding one
                // enormous picture of the page.
                : PdfToTable.Handles(input, format)
                    ? PdfToTable.Convert(input, format, folder)
                    // A message never goes to LibreOffice either: it has no idea what one is, and
                    // the reader that does was already written for the index.
                    : MailToDocument.Handles(input, format)
                        ? MailToDocument.Convert(input, format, folder)
                        : LibreOffice.IsInstalled
                            ? LibreOffice.Convert(input, format, folder, log)
                            : new LibreOffice.Result(
                                "", "LibreOffice n'est pas installé : cette conversion a besoin de lui.");

        if (!result.Worked)
        {
            // Journalise aussi : une conversion qui echoue le disait a l'ecran et nulle part
            // ailleurs, donc le message disparaissait avec la fenetre — et c'est precisement celui
            // dont on a besoin pour comprendre pourquoi elle a echoue.
            log?.Invoke($"conversion echouee: {result.Problem}");
            return result.Problem;
        }

        // Shown where it landed rather than opened. Opening would guess which application the user
        // wants, and the file may be one of several they are producing in a row.
        Search.ShellFolders.Reveal(result.Produced, log);

        // Le compte de la route Word remonte avec le nom du fichier : une conversion qui perd ses
        // images doit le dire au moment ou elle les perd, plutot que d'obliger a ouvrir le document
        // pour s'en apercevoir. Vide pour les autres routes, qui ne comptent rien.
        //
        // Lu SUR LE RESULTAT, et non sur la classe qui l'ecrit. La propriete statique survivait a la
        // conversion qu'elle decrivait : convertir un PDF en .docx puis un .docx en .odt annoncait,
        // pour le second, le compte d'images du premier — chiffre exact, document faux.
        var trace = result.Trace;

        if (trace.Length > 0)
        {
            // Aussi dans le journal, et pas seulement a l'ecran : un diagnostic qui n'existe que
            // dans une fenetre oblige quelqu'un a le recopier, et se perd des qu'elle se ferme.
            log?.Invoke($"conversion: {trace}");
        }

        return trace.Length > 0
            ? $"Écrit : {Path.GetFileName(result.Produced)} — {trace}"
            : $"Écrit : {Path.GetFileName(result.Produced)}";
    }

    /// <summary>Hands something to the shell, the way the launcher does.</summary>
    /// <remarks>
    /// Le processus est inscrit au job des enfants (cf. <c>JobEnfants</c>) pour qu'il ne
    /// survive pas à une fermeture brutale de l'environnement. Les outils du produit en
    /// dépendent : un Atelier orphelin, c'est une fenêtre qui continue de répondre à des
    /// clics et à des frappes que plus personne n'attend. Même les outils lancés via
    /// <c>Ouvrir(cible, env)</c> transitent par ici, donc bénéficient automatiquement du fix.
    /// </remarks>
    private static void Start(string target)
    {
        // Si l'Atelier headless (port 8770) tourne deja, on appelle son verbe
        // /atelier/fenetre/ouvrir au lieu de creer un 2e process en conflit.
        if (AtelierHeadlessDejaActif())
        {
            var url = Environment.GetEnvironmentVariable("SENSE_ATELIER_URL") ?? "http://127.0.0.1:8770";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                http.PostAsync($"{url}/atelier/fenetre/ouvrir",
                    new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                return;
            }
            catch
            {
                // HTTP echoue -> fallback sur Process.Start classique.
            }
        }

        var p = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        JobEnfants.Inscrire(p);
    }

    /// <summary>Teste si l'Atelier headless est deja en ecoute sur 127.0.0.1:8770.</summary>
    private static bool AtelierHeadlessDejaActif()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync("127.0.0.1", 8770);
            return task.Wait(500) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ouvre une application, dans son propre environnement si son manifeste en déclare un.
    /// </summary>
    /// <remarks>
    /// Sans déclaration, on passe par le shell comme avant : c'est ce que veut une application du
    /// système, qui doit hériter de la session de l'utilisateur. Avec déclaration, on compose
    /// l'environnement, ce que le shell ne permet pas — d'où le lancement direct.
    ///
    /// <para>
    /// Un environnement qu'on ne sait pas construire n'annule pas l'ouverture : l'outil démarre
    /// quand même, et l'écran dit que son isolement n'a pas pu être posé. L'inverse — refuser
    /// d'ouvrir — punirait l'utilisateur d'un défaut de manifeste.
    /// </para>
    /// </remarks>
    private static string Ouvrir(string target, EnvironnementOutil? environnement)
    {
        if (environnement is null)
        {
            Start(target);

            return "";
        }

        var depart = new ProcessStartInfo(target);
        var refus = EnvironnementIsole.Preparer(depart, Resoudre(environnement.Dossier), environnement);

        if (refus.Length > 0)
        {
            Start(target);

            return refus;
        }

        var p = Process.Start(depart);
        JobEnfants.Inscrire(p);

        return "";
    }
}
