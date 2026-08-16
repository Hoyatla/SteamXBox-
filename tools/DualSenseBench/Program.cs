using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DualSenseBench;
using HidSharp;
using Sc2Xboxed.Windows;

// Banc DualSense — lecture seule.
//
// Une liste de commandes. On presse celle qui est surlignée, on voit à l'écran ce qui a bougé dans
// le rapport, on valide avec la FLÈCHE DROITE. Rien n'est filtré et rien n'est décidé pour
// l'opérateur : tout écart est affiché, c'est lui qui dit quand c'est la bonne commande.
//
// Ce qui a été retiré, et pourquoi : une détection automatique comparait chaque trame à un repos
// avec une marge de cinq crans pour absorber le bruit des sticks. L1, R1 et L2 valent 1, 2 et 4 —
// moins que la marge. Ils n'ont jamais été validés sur une manette qui marchait. Un instrument qui
// décide seul de ce qui mérite d'être vu peut se taire ; celui-ci ne le peut plus.
//
// Un seul fil lit la manette, du début à la fin, et ne fait que ça : pas d'analyse, pas d'écriture.

Console.OutputEncoding = Encoding.UTF8;

var arguments = Environment.GetCommandLineArgs();

if (Drapeau("--aide") || Drapeau("-h"))
{
    Console.WriteLine("DualSenseBench — presser chaque commande, obtenir la correspondance.");
    Console.WriteLine();
    Console.WriteLine("  FLÈCHE DROITE  valider ce qui est affiché et passer à la commande suivante");
    Console.WriteLine("  ÉCHAP          terminer et écrire le rapport");
    Console.WriteLine();
    Console.WriteLine("  --note \"...\"          manette, batterie, lien — consigné dans seance.json");
    Console.WriteLine("  --vid / --pid <hex>   autre manette (Edge : --pid 0x0DF2)");
    Console.WriteLine("  --chemin <DevicePath> interface précise, sinon la plus longue");
    Console.WriteLine("  --complet             demander les rapports 0x31 avant de mesurer");
    Console.WriteLine();
    Console.WriteLine("Exige SteamXBox arrêté (Stop-SteamXBox.cmd) et HidHide désactivé (HidHide-Off.cmd).");
    return 0;
}

var note = Texte("--note", "");
var complet = Drapeau("--complet");
var cheminDemande = Texte("--chemin", "");
var vendeur = Entier("--vid", 0x054C);
var produit = Entier("--pid", 0x0CE6);

var interfaces = DeviceList.Local
    .GetHidDevices(vendeur, produit)
    .Where(d => d.GetMaxInputReportLength() >= 11)
    .ToList();

if (interfaces.Count == 0)
{
    Console.WriteLine("Aucune interface DualSense.");
    Console.WriteLine("La manette est-elle CONNECTÉE (pas seulement appairée) ?");
    Console.WriteLine("Et HidHide désactivé ? Il masque les manettes à tout processus sauf le Core.");
    Attendre();
    return 1;
}

var appareil = cheminDemande.Length > 0
    ? interfaces.FirstOrDefault(d => d.DevicePath.Equals(cheminDemande, StringComparison.OrdinalIgnoreCase))
    : interfaces.OrderByDescending(d => d.GetMaxInputReportLength()).First();

if (appareil is null)
{
    Console.WriteLine($"Aucune interface au chemin {cheminDemande}.");
    Attendre();
    return 1;
}

if (!appareil.TryOpen(out var flux))
{
    Console.WriteLine("Ouverture impossible : la manette est tenue par un autre processus.");
    Console.WriteLine("Arrêter SteamXBox (Stop-SteamXBox.cmd), et fermer Steam s'il tourne.");
    Attendre();
    return 2;
}

var bluetooth = appareil.DevicePath.Contains("VID&0002", StringComparison.OrdinalIgnoreCase);
var dossier = Path.Combine(
    RacineDuProjet(),
    "mesures",
    "dualsense-bt",
    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (bluetooth ? "-bt" : "-usb"));

// Rien n'est conservé : les trames sont consommées au fil de l'eau par le détecteur et jetées.
// Enregistrer six cents trames par seconde pour en tirer une ligne par bouton, c'est archiver du
// bruit.
var file = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
var chrono = Stopwatch.StartNew();
var arret = new CancellationTokenSource();
var releves = new List<Releve>();

using (flux)
{
    flux.ReadTimeout = 50;

    if (complet)
    {
        DemanderRapportsComplets(appareil, flux);
    }

    var lecteur = new Thread(() =>
    {
        var tampon = new byte[Math.Max(16, appareil.GetMaxInputReportLength())];

        while (!arret.IsCancellationRequested)
        {
            try
            {
                var lus = flux.Read(tampon);

                if (lus <= 0)
                {
                    continue;
                }

                file.Enqueue(tampon.AsSpan(0, lus).ToArray());
            }
            catch (TimeoutException)
            {
                // Personne ne touche la manette.
            }
            catch (Exception)
            {
                return;
            }
        }
    })
    {
        IsBackground = true,
        Name = "lecture-dualsense",
        Priority = ThreadPriority.AboveNormal,
    };

    lecteur.Start();

    // La première trame prouve que la manette émet. Sans elle il n'y a rien à mesurer.
    var attente = Stopwatch.StartNew();

    while (file.IsEmpty && attente.Elapsed < TimeSpan.FromSeconds(5))
    {
        Thread.Sleep(20);
    }

    if (file.IsEmpty)
    {
        Console.WriteLine("La manette est ouverte et n'émet rien. Rien à mesurer.");
        arret.Cancel();
        Attendre();
        return 3;
    }

    // Sonde : pas de liste, pas de touche. N secondes, et ce qui a sauté. Sert à vérifier que la
    // lecture et la détection marchent, y compris depuis une console sans clavier.
    var sonde = Entier("--sonde", 0);

    if (sonde > 0)
    {
        Console.WriteLine($"  Sonde de {sonde} s — presser ce qu'on veut, seuls les pics sortiront.");

        var observe = new Detecteur();
        var lues = 0;
        var jusqua = chrono.Elapsed + TimeSpan.FromSeconds(sonde);

        while (chrono.Elapsed < jusqua)
        {
            while (file.TryDequeue(out var trame))
            {
                observe.Observer(trame);
                lues++;
            }

            Thread.Sleep(10);
        }

        Console.WriteLine($"  {lues} trames lues, rien gardé.");

        foreach (var effet in observe.Effets())
        {
            Console.WriteLine($"    {effet.Lisible}");
        }

        arret.Cancel();
        lecteur.Join(TimeSpan.FromSeconds(2));
        return 0;
    }

    if (Console.IsInputRedirected)
    {
        Console.WriteLine("  Pas de clavier sur cette console : la liste ne peut pas être validée.");
        Console.WriteLine("  Lancer Banc-DualSense.exe directement, ou utiliser --sonde <secondes>.");
        arret.Cancel();
        return 4;
    }

    Directory.CreateDirectory(dossier);

    // Ouvert avant la première commande et vidé à chaque ligne : ce qui est validé est sur le disque
    // à la seconde où il est validé.
    using var fichier = new StreamWriter(Path.Combine(dossier, "correspondance.md"), false, new UTF8Encoding(false))
    {
        AutoFlush = true,
    };

    fichier.Write(Rapport.Entete(
        $"- Date : {DateTime.Now:yyyy-MM-dd HH:mm}{Environment.NewLine}"
        + $"- Transport : **{(bluetooth ? "Bluetooth" : "USB")}**{Environment.NewLine}"
        + $"- Chemin : `{appareil.DevicePath}`{Environment.NewLine}"
        + $"- Clé durable : `{DeviceTree.DurableKeyFor(appareil.DevicePath) ?? "(aucune)"}`{Environment.NewLine}"
        + $"- Rapport d'entrée : {appareil.GetMaxInputReportLength()} octets{Environment.NewLine}"
        + (note.Length > 0 ? $"- Note : {note}{Environment.NewLine}" : "")));

    var detecteur = new Detecteur();
    var index = 0;
    var derniereValidation = chrono.Elapsed;

    Console.Clear();
    Afficher(releves, index, []);

    while (index < Controles.Tout.Count)
    {
        while (file.TryDequeue(out var trame))
        {
            detecteur.Observer(trame);
        }

        // La répétition automatique du clavier est ce qui a fait valider des commandes à vide : une
        // flèche droite maintenue un instant, et Windows en envoie trente que la boucle consomme
        // d'affilée. Une validation exige donc que la précédente ait un tiers de seconde, et tout ce
        // qui reste dans le tampon est jeté juste après.
        if (!Console.IsInputRedirected
            && Console.KeyAvailable
            && chrono.Elapsed - derniereValidation > TimeSpan.FromMilliseconds(350))
        {
            var touche = Console.ReadKey(intercept: true).Key;

            if (touche is ConsoleKey.RightArrow or ConsoleKey.Escape)
            {
                var releve = new Releve(Controles.Tout[index], detecteur.Effets(), Ignoree: false);
                releves.Add(releve);
                fichier.WriteLine(Rapport.Ligne(releve));
                index++;

                if (touche == ConsoleKey.Escape)
                {
                    while (index < Controles.Tout.Count)
                    {
                        var passee = new Releve(Controles.Tout[index], [], Ignoree: true);
                        releves.Add(passee);
                        fichier.WriteLine(Rapport.Ligne(passee));
                        index++;
                    }

                    break;
                }

                derniereValidation = chrono.Elapsed;

                while (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                }

                detecteur.Reinitialiser();
                Afficher(releves, index, []);
                continue;
            }
        }

        Afficher(releves, index, detecteur.Effets());
        Thread.Sleep(100);
    }

    fichier.Write(Rapport.Pied());
    arret.Cancel();
    lecteur.Join(TimeSpan.FromSeconds(2));
}

File.WriteAllText(
    Path.Combine(dossier, "seance.json"),
    JsonSerializer.Serialize(
        new
        {
            date = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            transport = bluetooth ? "bluetooth" : "usb",
            mode = complet ? "complet-0x31" : "compat",
            note,
            chemin = appareil.DevicePath,
            cleDurable = DeviceTree.DurableKeyFor(appareil.DevicePath),
            longueurEntree = appareil.GetMaxInputReportLength(),
            windows = Environment.OSVersion.VersionString,
            secondes = chrono.Elapsed.TotalSeconds,
            commandes = releves.Select(r => new
            {
                nom = r.Controle.Nom,
                ignoree = r.Ignoree,
                effets = r.Effets.Select(e => new { octet = e.Octet, avant = e.Avant, extreme = e.Extreme, bits = e.BitsAllumes }),
            }),
        },
        new JsonSerializerOptions { WriteIndented = true }),
    new UTF8Encoding(false));

Console.WriteLine();
Console.WriteLine($"  {Path.Combine(dossier, "correspondance.md")}");
Attendre();
return 0;

// ── affichage ────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// La liste, et sous la commande en cours, tout ce qui bouge en direct.
/// </summary>
/// <remarks>
/// Redessinée entièrement à chaque rafraîchissement. C'est brutal et c'est voulu : une détection
/// muette ne se distingue pas d'une manette morte, et laisser quelqu'un presser des boutons devant
/// un écran figé est ce qui a fait perdre le plus de temps sur cet outil.
/// </remarks>
static void Afficher(IReadOnlyList<Releve> faits, int courant, IReadOnlyList<Effet> direct)
{
    var largeur = Math.Max(40, Console.WindowWidth - 1);
    var hauteur = Math.Max(10, Console.WindowHeight - 1);
    var lignes = new List<string>
    {
        $"  {courant + 1}/{Controles.Tout.Count} — presser la commande surlignée, puis FLÈCHE DROITE pour valider.",
        "  ÉCHAP termine. Tout est écrit au fur et à mesure.",
        "",
    };

    // Une fenêtre glissante autour de la commande en cours. La liste entière ne tient pas dans une
    // console de trente lignes, et une liste qui déborde fait défiler l'écran à chaque
    // rafraîchissement : plus rien n'est lisible, et c'est l'écran qui sert à valider.
    var place = hauteur - lignes.Count - 6;
    var debut = Math.Max(0, Math.Min(courant - place / 2, Controles.Tout.Count - place));

    for (var i = Math.Max(0, debut); i < Controles.Tout.Count && lignes.Count < hauteur - 5; i++)
    {
        var controle = Controles.Tout[i];

        if (i < faits.Count)
        {
            var releve = faits[i];
            var marque = releve.Ignoree ? "—" : releve.Effets.Count == 0 ? "×" : "✓";
            lignes.Add($"  {marque} {controle.Nom,-28} {releve.Lisible}");
        }
        else if (i == courant)
        {
            var aide = controle.Aide.Length > 0 ? $" ({controle.Aide})" : "";
            lignes.Add($"  ▶ {controle.Nom}{aide}");
            lignes.Add(direct.Count == 0 ? "      rien ne bouge pour l'instant" : "");

            if (direct.Count > 0)
            {
                lignes.RemoveAt(lignes.Count - 1);

                foreach (var effet in direct.Take(3))
                {
                    lignes.Add($"      {effet.Lisible}");
                }
            }
        }
        else
        {
            lignes.Add($"    {controle.Nom}");
        }
    }

    var page = new StringBuilder();

    foreach (var ligne in lignes)
    {
        var coupee = ligne.Length > largeur ? ligne[..largeur] : ligne;
        page.Append(coupee).Append(new string(' ', largeur - coupee.Length)).Append('\n');
    }

    Console.SetCursorPosition(0, 0);
    Console.Write(page.ToString());
}

// ── manette ──────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Demande à la manette ses rapports complets 0x31 en lisant le rapport de calibration.
/// </summary>
/// <remarks>
/// C'est la lecture elle-même qui bascule la manette ; les valeurs de calibration sont jetées. À
/// n'utiliser que pour comparer les deux formes de rapport — le mode complet est éteint dans le
/// produit, et pour une raison mesurée.
/// </remarks>
static void DemanderRapportsComplets(HidDevice appareil, HidStream flux)
{
    try
    {
        var longueur = appareil.GetMaxFeatureReportLength();

        if (longueur <= 0)
        {
            Console.WriteLine("  Aucune longueur de rapport de fonctionnalité annoncée : la manette reste en compatibilité.");
            return;
        }

        var fonctionnalite = new byte[longueur];
        fonctionnalite[0] = 0x05;
        flux.GetFeature(fonctionnalite);

        Console.WriteLine("  Rapports complets demandés : attendre id=0x31 à partir d'ici.");
    }
    catch (Exception echec)
    {
        Console.WriteLine($"  Demande refusée ({echec.GetType().Name}). La manette reste en compatibilité.");
    }
}

// ── utilitaires ──────────────────────────────────────────────────────────────────────────────────

/// <summary>La racine du dépôt, pour que les mesures se rangent dedans quel que soit le dossier courant.</summary>
static string RacineDuProjet()
{
    var dossier = new DirectoryInfo(AppContext.BaseDirectory);

    while (dossier is not null)
    {
        if (File.Exists(Path.Combine(dossier.FullName, "Sc2Xboxed.sln")))
        {
            return dossier.FullName;
        }

        dossier = dossier.Parent;
    }

    return Directory.GetCurrentDirectory();
}

/// <summary>Retient la fenêtre ouverte quand l'outil a été lancé par un double-clic.</summary>
static void Attendre()
{
    if (Console.IsInputRedirected)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine("  Appuyer sur une touche pour fermer.");

    try
    {
        Console.ReadKey(intercept: true);
    }
    catch (InvalidOperationException)
    {
        // Pas de console interactive : il n'y a rien à retenir.
    }
}

bool Drapeau(string nom) => arguments.Any(a => a.Equals(nom, StringComparison.OrdinalIgnoreCase));

string Texte(string nom, string defaut)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (arguments[i].Equals(nom, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[i + 1];
        }
    }

    return defaut;
}

/// <summary>Un entier, décimal ou hexadécimal préfixé <c>0x</c> — les identifiants HID s'écrivent ainsi.</summary>
int Entier(string nom, int defaut)
{
    var brut = Texte(nom, "");
    var hexadecimal = brut.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

    return int.TryParse(
        hexadecimal ? brut[2..] : brut,
        hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var valeur)
        ? valeur
        : defaut;
}
