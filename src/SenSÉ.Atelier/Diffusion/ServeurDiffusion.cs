using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Diffusion;

/// <summary>
/// Le serveur de diffusion stable, lance depuis <c>Outils\sd.cpp\sd-server.exe</c>.
/// Un seul modele charge a la fois, swap explicite entre modeles (etape 2 bis).
/// </summary>
/// <remarks>
/// <b>Meme contrat que <c>ComfyUI</c>, API OpenAI-compatible.</b>
/// <c>POST /v1/images/generations</c> renvoie un JSON, le binaire ecrit le fichier
/// sur disque dans son dossier de sortie, et on relit le chemin depuis la reponse HTTP
/// (et SUR le disque, parce que le binaire peut ignorer silencieusement certains flags).
///
/// <para><b>Jamais de chemin absolu accentue dans la ligne de commande.</b>
/// <c>sd-server</c> est connu pour mal parser les chemins sur la ligne de commande
/// quand ils contiennent des caracteres non-ASCII. On passe par
/// <c>psi.WorkingDirectory</c> et des chemins relatifs au dossier du modele.</para>
///
/// <para><b>Wan 2.2 necessite <c>--params-backend diffusion=cpu</c>.</b> Sans ca, le
/// binaire tente d'offloader sur GPU et explose en VRAM. Flux laisse l'auto-fit decider.</para>
///
/// <para><b>Le lancement est direct (pas de ServeurLocal).</b> Le projet ne reference pas
/// <c>SenSÉ.Tools</c> et la classe <c>ServeurLocal</c> y est <c>internal</c>. On lance
/// directement le process et on garde la main sur son cycle de vie ici.</para>
/// </remarks>
public sealed class ServeurDiffusion : IDisposable
{
    public const int Port = 8082;

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(12);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static string Racine => Path.Combine(AppContext.BaseDirectory, "Outils");
    private static string DossierBinaires => Path.Combine(Racine, "sd.cpp");
    private static string Programme => Path.Combine(DossierBinaires, "sd-server.exe");
    private static string DossierModeles => Path.Combine(Racine, "Modeles");

    public static string Adresse => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture);

    private Process? _process;
    private string? _modeleActif;

    /// <summary>Spec d'un modele tel qu'il est dans son manifeste JSON.</summary>
    public sealed record ModeleSpec(
        string Id,
        string Nom,
        string Moteur,
        string Espace,
        string Racine,        // "image" ou "video"
        Dictionary<string, string> Fichiers,
        int VramMo,
        string? ParamsBackend = null,
        string Produit = "image");

    public enum Etat { Absent, Charge, Pret }

    public static Etat Ou()
    {
        try
        {
            var reponse = Http.GetAsync(Adresse + "/health").GetAwaiter().GetResult();
            var lu = reponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (reponse.IsSuccessStatusCode && lu.Contains("\"ok\"", StringComparison.Ordinal)) return Etat.Pret;
            return Etat.Charge;
        }
        catch { return Etat.Absent; }
    }

    /// <summary>
    /// Demarre le serveur pour un modele precis, attend qu'il soit pret.
    /// Si un autre modele est deja charge, on l'arrete d'abord (swap).
    /// </summary>
    public string? Demarrer(ModeleSpec spec, Action<string>? journal, CancellationToken arret = default)
    {
        if (!File.Exists(Programme))
        {
            var installer = SdCppEmbarque.InstallerAsync(journal).GetAwaiter().GetResult();
            if (installer is null) return $"sd-server introuvable et telechargement impossible ({Programme})";
        }

        var racineModele = CheminRacineModele(spec);
        if (!Directory.Exists(racineModele))
            return $"Dossier modele introuvable : {racineModele}";

        var etat = Ou();
        if (etat == Etat.Pret && _modeleActif == spec.Id) return null;

        // Pas le bon modele (ou pas de serveur) : on arrete puis relance
        Arreter(journal);
        if (etat == Etat.Charge) return Attendre(journal, arret);

        var args = ConstruireArgs(spec);
        var journalier = Path.Combine(DossierBinaires, "serveur.log");
        Directory.CreateDirectory(DossierBinaires);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Programme,
                WorkingDirectory = racineModele,   // chemins relatifs au dossier du modele
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,    // redirection shell pour eviter le pipe casse
                RedirectStandardError = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            // Redir stdout/stderr vers le journalier via cmd /c
            var cmd = new StringBuilder("/c \"");
            cmd.Append('"').Append(Programme).Append('"');
            foreach (var argument in args)
            {
                cmd.Append(' ');
                cmd.Append(argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument);
            }
            cmd.Append(" > \"").Append(journalier).Append("\" 2>&1\"");

            var psiCmd = new ProcessStartInfo("cmd", cmd.ToString())
            {
                WorkingDirectory = racineModele,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _process = Process.Start(psiCmd);
            _modeleActif = spec.Id;
            journal?.Invoke($"sd-server lance : {spec.Nom} ({spec.Id})");
            return Attendre(journal, arret);
        }
        catch (Exception ex)
        {
            return "sd-server n'a pas demarre : " + ex.Message;
        }
    }

    public void Arreter(Action<string>? journal)
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
        }
        catch { /* rien de mieux a tenter */ }
        _process = null;
        _modeleActif = null;

        // Au cas ou un autre process externe ecoute encore
        try
        {
            foreach (var p in Process.GetProcessesByName("sd-server"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }
    }

    private static List<string> ConstruireArgs(ModeleSpec spec)
    {
        var args = new List<string>
        {
            "--host", "127.0.0.1",
            "--port", Port.ToString(CultureInfo.InvariantCulture),
            "--diffusion-model", spec.Fichiers["diffusion"],
        };

        if (spec.Fichiers.TryGetValue("diffusion_haut_bruit", out var haut) && !string.IsNullOrEmpty(haut))
            args.AddRange(new[] { "--diffusion-model-high-noise", haut });

        if (spec.Fichiers.TryGetValue("vae", out var vae) && !string.IsNullOrEmpty(vae))
            args.AddRange(new[] { "--vae", vae });

        foreach (var cleTextEnc in new[] { "t5xxl", "clip_l", "clip_g", "umt5" })
        {
            if (spec.Fichiers.TryGetValue(cleTextEnc, out var te) && !string.IsNullOrEmpty(te))
            {
                args.AddRange(new[] { "--text-encoder", te });
                break;
            }
        }

        // Wan 2.2 a besoin de ce flag ou il explose en VRAM.
        if (!string.IsNullOrEmpty(spec.ParamsBackend))
            args.AddRange(new[] { "--params-backend", spec.ParamsBackend });

        args.AddRange(new[] { "--cache-dir", "cache" });
        return args;
    }

    private static string CheminRacineModele(ModeleSpec spec)
        => Path.Combine(DossierModeles, spec.Racine);

    private static string? Attendre(Action<string>? journal, CancellationToken arret)
    {
        var debut = DateTime.UtcNow;
        while (DateTime.UtcNow - debut < Patience)
        {
            if (arret.IsCancellationRequested) return "Annulation demandee pendant l'attente.";
            var etat = Ou();
            if (etat == Etat.Pret)
            {
                journal?.Invoke("sd-server pret.");
                return null;
            }
            Thread.Sleep(2000);
        }
        return "Timeout : sd-server n'a pas repondu en " + Patience.TotalMinutes + " min.";
    }

    /// <summary>
    /// Genere une image. Le binaire ecrit le PNG dans son dossier de sortie, on l'envoie
    /// en b64 et on l'ecrit dans %TEMP%.
    /// </summary>
    public static async Task<string> GenererImageAsync(
        string prompt, string? negatif = null, int largeur = 1024, int hauteur = 1024,
        int steps = 20, double cfg = 7.0, long seed = -1, CancellationToken ct = default)
    {
        var body = new
        {
            prompt,
            negative_prompt = negatif ?? "low quality, blurry",
            size = largeur + "x" + hauteur,
            n = 1,
            steps,
            cfg_scale = cfg,
            seed,
        };
        return await AppelerGenerationAsync(body, "image", ct);
    }

    /// <summary>
    /// Genere une video. Wan 2.2 produit un .mp4 qui est en realite du MJPEG .avi,
    /// on convertit via ffmpeg si necessaire.
    /// </summary>
    public static async Task<string> GenererVideoAsync(
        string prompt, string? imageInitiale = null, string? negatif = null,
        int frames = 16, int largeur = 832, int hauteur = 480,
        int steps = 20, double cfg = 7.0, long seed = -1, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["negative_prompt"] = negatif ?? "low quality, blurry, distorted",
            ["size"] = largeur + "x" + hauteur,
            ["n"] = 1,
            ["steps"] = steps,
            ["cfg_scale"] = cfg,
            ["seed"] = seed,
            ["response_format"] = "b64_json",
        };
        if (!string.IsNullOrEmpty(imageInitiale) && File.Exists(imageInitiale))
        {
            var bytes = File.ReadAllBytes(imageInitiale);
            body["image"] = Convert.ToBase64String(bytes);
        }
        var sortie = await AppelerGenerationAsync(body, "video", ct);
        return VerifierSortieVideo(sortie);
    }

    private static async Task<string> AppelerGenerationAsync(object body, string type, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        using var resp = await Http.PostAsync(Adresse + "/v1/images/generations",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception($"sd-server HTTP {(int)resp.StatusCode}: {text}");

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
        {
            var item = data[0];
            if (item.TryGetProperty("b64_json", out var b64))
            {
                var ext = type == "video" ? ".mp4" : ".png";
                var outPath = Path.Combine(Path.GetTempPath(), "atelier_diff_" + Guid.NewGuid().ToString("N") + ext);
                File.WriteAllBytes(outPath, Convert.FromBase64String(b64.GetString() ?? ""));
                return outPath;
            }
            if (item.TryGetProperty("url", out var url)) return url.GetString() ?? "";
        }
        throw new Exception("sd-server n'a pas renvoye de donnee de generation");
    }

    private static string VerifierSortieVideo(string chemin)
    {
        if (!chemin.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return chemin;
        if (!File.Exists(chemin) || new FileInfo(chemin).Length < 1024) return chemin;
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "Outils", "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) return chemin;
        var mp4 = Path.ChangeExtension(chemin, null) + "_h264.mp4";
        var psi = new ProcessStartInfo(ffmpeg, "-y -i \"" + chemin + "\" -c:v libx264 -pix_fmt yuv420p \"" + mp4 + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit(60_000);
            if (File.Exists(mp4) && new FileInfo(mp4).Length > 1024) return mp4;
        }
        catch { /* tant pis, on garde le .mp4 brut */ }
        return chemin;
    }

    /// <summary>Charge tous les manifestes modele.json du dossier specifie (image/ ou video/).</summary>
    public static List<ModeleSpec> ChargerModeles(string racine)
    {
        var liste = new List<ModeleSpec>();
        if (!Directory.Exists(racine)) return liste;
        foreach (var f in Directory.EnumerateFiles(racine, "modele.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(f);
                var spec = JsonSerializer.Deserialize<ModeleSpec>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (spec is not null) liste.Add(spec);
            }
            catch { /* manifeste corrompu, on l'ignore */ }
        }
        return liste;
    }

    public void Dispose() => Arreter(null);
}