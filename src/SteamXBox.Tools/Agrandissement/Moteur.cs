using System.Diagnostics;
using System.Text;

namespace SteamXBox.Tools.Agrandissement;

/// <summary>
/// Ce que l'agrandissement d'une image et celui d'une vidéo ont en commun.
/// </summary>
/// <remarks>
/// Les deux outils appellent le même moteur, cherchent le même ffmpeg et résolvent les mêmes noms
/// de modèles. Décrit une fois : deux copies de cette plomberie divergeraient, et c'est toujours la
/// copie qu'on oublie qui garde le défaut.
/// </remarks>
internal static class Moteur
{
    /// <summary>
    /// Chargement, calcul, écriture. Mesuré : 19 s contre 32 s au réglage d'origine « 1:2:2 ».
    /// </summary>
    internal const string Threads = "4:2:4";

    internal static string Outils => Path.Combine(AppContext.BaseDirectory, "Outils");

    internal static string Agrandisseur
        => Path.Combine(Outils, "Real-ESRGAN-ncnn", "realesrgan-ncnn-vulkan.exe");

    internal static string DossierModeles => Path.Combine(Outils, "Real-ESRGAN-ncnn", "models");

    internal static string Interpolateur
        => Path.Combine(Outils, "rife-ncnn-vulkan", "rife-ncnn-vulkan.exe");

    internal static string ModeleInterpolation
        => Path.Combine(Outils, "rife-ncnn-vulkan", "rife-v4.6");

    /// <summary>Les formats que le moteur d'agrandissement lit et écrit lui-même.</summary>
    /// <remarks>
    /// Tout le reste passe par une conversion en PNG avant, et une reconversion après. Sans elle,
    /// un outil qui annonce dix extensions n'en traiterait que trois.
    /// </remarks>
    internal static readonly string[] FormatsNatifs = ["png", "jpg", "jpeg", "webp"];

    /// <summary>
    /// L'échelle fait partie du nom du modèle chez ncnn ; elle est résolue ici.
    /// </summary>
    /// <remarks>
    /// Le manifeste n'a donc pas à connaître les combinaisons valides, et un couple incohérent —
    /// un modèle photo qui n'existe qu'en quatre fois, demandé en deux fois — est corrigé et
    /// annoncé plutôt que subi.
    /// </remarks>
    internal static string? ResoudreModele(ref string modele, ref string echelle, Action<string>? journal)
    {
        if (!Directory.Exists(DossierModeles))
        {
            return $"Modèles absents : {DossierModeles}";
        }

        foreach (var candidat in new[] { $"{modele}-x{echelle}", modele })
        {
            if (File.Exists(Path.Combine(DossierModeles, candidat + ".param")))
            {
                modele = candidat;

                if (!modele.EndsWith($"-x{echelle}", StringComparison.Ordinal))
                {
                    journal?.Invoke("Ce modèle n'existe qu'en quatre fois : échelle ajustée.");
                    echelle = "4";
                }

                return null;
            }
        }

        var disponibles = Directory.GetFiles(DossierModeles, "*.param")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        return $"Modèle inconnu : {modele}. Disponibles : {string.Join(", ", disponibles)}";
    }

    /// <summary>
    /// Le programme, cherché dans <c>Outils</c> avant le PATH.
    /// </summary>
    /// <remarks>
    /// L'ordre compte : une copie posée dans <c>Outils\ffmpeg</c> doit gagner sur celle du système,
    /// sinon on ne peut ni figer une version connue ni dépanner une machine dont le PATH est muet.
    /// C'est aussi ce qui permet d'accompagner le produit d'un ffmpeg sans toucher aux réglages de
    /// Windows.
    /// </remarks>
    internal static string? Trouver(string nom)
    {
        foreach (var dossier in new[] { Path.Combine(Outils, "ffmpeg", "bin"), Path.Combine(Outils, "ffmpeg") })
        {
            var local = Path.Combine(dossier, nom + ".exe");

            if (File.Exists(local))
            {
                return local;
            }
        }

        var chemin = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(chemin))
        {
            return null;
        }

        foreach (var dossier in chemin.Split(Path.PathSeparator))
        {
            if (dossier.Length == 0)
            {
                continue;
            }

            try
            {
                var candidat = Path.Combine(dossier, nom + ".exe");

                if (File.Exists(candidat))
                {
                    return candidat;
                }
            }
            catch (ArgumentException)
            {
                // Une entrée de PATH mal formée ne doit pas arrêter la recherche.
            }
        }

        return null;
    }

    /// <summary>
    /// Lance un programme et attend, en vidant ses deux sorties.
    /// </summary>
    /// <remarks>
    /// Les deux flux sont drainés, et ce n'est pas un détail de style : les moteurs écrivent leur
    /// avancement sur la sortie d'erreur, et un tampon plein arrête le processus qui l'écrit. Sans
    /// cela, un long tronçon se fige au lieu de finir.
    ///
    /// <para>
    /// Aucun délai maximum : un film légitime occupe la machine pendant des heures, et une patience
    /// bornée transformerait un travail long en échec silencieux.
    /// </para>
    /// </remarks>
    internal static bool Lancer(string programme, out string erreur, params string[] arguments)
    {
        var depart = Depart(programme, arguments);

        using var processus = Process.Start(depart);

        if (processus is null)
        {
            erreur = $"{Path.GetFileName(programme)} n'a pas démarré.";

            return false;
        }

        var plaintes = new StringBuilder();

        processus.OutputDataReceived += (_, _) => { };
        processus.ErrorDataReceived += (_, donnee) =>
        {
            if (donnee.Data is { Length: > 0 } ligne && plaintes.Length < 2000)
            {
                plaintes.Append(ligne).Append(' ');
            }
        };

        processus.BeginOutputReadLine();
        processus.BeginErrorReadLine();
        processus.WaitForExit();

        erreur = processus.ExitCode == 0 ? "" : plaintes.ToString().Trim();

        return processus.ExitCode == 0;
    }

    /// <summary>Lance un programme court et rend ce qu'il a écrit.</summary>
    internal static string Lire(string programme, params string[] arguments)
    {
        using var processus = Process.Start(Depart(programme, arguments));

        if (processus is null)
        {
            return "";
        }

        // Les deux sorties se vident ensemble : voir Tuyaux.Vider. Les moteurs ncnn annoncent
        // leur avancement sur la sortie d erreur, et en lire une avant l autre les bloque.
        return Serveurs.Tuyaux.Vider(processus).Sortie;
    }

    internal static string Neuf(string dossier)
    {
        Effacer(dossier);
        Directory.CreateDirectory(dossier);

        return dossier;
    }

    /// <summary>Le nom du témoin qui distingue notre dossier de travail de celui d'un autre.</summary>
    private const string Temoin = ".steamxbox-travail";

    /// <summary>
    /// Un dossier de travail qui n'écrasera rien : celui demandé, ou le premier nom libre à côté.
    /// </summary>
    /// <remarks>
    /// <b>Le défaut que ceci corrige.</b> Le dossier de travail se nommait
    /// <c>&lt;nom du fichier&gt;_travail</c>, dans le dossier de l'utilisateur, et il était effacé
    /// récursivement en fin de traitement — comme au début, avant d'être créé. Quelqu'un qui garde
    /// ses rushes dans « vacances_travail\ » à côté de « vacances.mp4 » perdait le dossier en
    /// lançant l'agrandissement, sans confirmation ni message.
    ///
    /// <para>
    /// Un témoin plutôt qu'un simple test d'existence. Un dossier laissé par un traitement
    /// interrompu nous appartient et doit être repris ; il porte donc ce fichier vide, et c'est lui
    /// qu'on lit. Sans témoin, le dossier est à quelqu'un d'autre : on prend le nom suivant, et rien
    /// n'est touché.
    /// </para>
    ///
    /// <para>
    /// À côté du fichier source et non dans le dossier temporaire de Windows : une vidéo agrandie
    /// écrit des dizaines de gigaoctets d'images intermédiaires, et le disque qui porte le film est
    /// le seul dont on sache qu'il a la place.
    /// </para>
    /// </remarks>
    internal static string Reserver(string dossier)
    {
        for (var essai = 0; essai < 100; essai++)
        {
            var candidat = essai == 0 ? dossier : $"{dossier}-{essai + 1}";

            if (File.Exists(candidat))
            {
                continue;
            }

            if (Directory.Exists(candidat) && !File.Exists(Path.Combine(candidat, Temoin)))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidat);
                File.WriteAllText(Path.Combine(candidat, Temoin), "");

                return candidat;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Ce nom-là ne se laisse pas prendre : le suivant, plutôt que d'abandonner le
                // travail pour un dossier.
            }
        }

        // Cent noms pris d'affilée n'arrive pas ; si cela arrivait, le dossier temporaire de Windows
        // vaut mieux qu'un refus, même quand la place y est moins sûre.
        var repli = Path.Combine(Path.GetTempPath(), "SteamXBox", Path.GetFileName(dossier));

        Directory.CreateDirectory(repli);
        File.WriteAllText(Path.Combine(repli, Temoin), "");

        return repli;
    }

    internal static void Effacer(string dossier)
    {
        try
        {
            if (Directory.Exists(dossier))
            {
                Directory.Delete(dossier, recursive: true);
            }
        }
        catch (IOException)
        {
            // Un fichier encore ouvert par un moteur qui vient de rendre la main. L'étape suivante
            // repart d'un dossier neuf de toute façon, et le dossier de travail est effacé à la fin.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ProcessStartInfo Depart(string programme, string[] arguments)
    {
        var depart = new ProcessStartInfo(programme)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            depart.ArgumentList.Add(argument);
        }

        return depart;
    }
}
