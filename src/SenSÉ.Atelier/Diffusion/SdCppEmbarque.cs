using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Diffusion;

/// <summary>
/// Le binaire <c>sd-server.exe</c> de <c>stable-diffusion.cpp</c>, telecharge une fois
/// dans <c>Outils\sd.cpp\</c> et epingle.
/// </summary>
/// <remarks>
/// <b>Meme politique que <c>ChromiumEmbarque</c>.</b>
/// L'archive fait ~70 Mo, on la telecharge a la demande. La revision est resolue une fois
/// au premier telechargement, puis epinglee dans <c>revision.txt</c>. Un coup de reseau
/// laisse un <c>.part</c> qui n'est jamais pris pour une installation valide.
///
/// <para><b>Pas de chemin avec accent dans l'archive.</b> Le produit est deploye dans
/// <c>Program Files\SenSÉ\</c>, et le binaire refuse les chemins non-ASCII sur la ligne
/// de commande. On extrait dans <c>Outils\sd.cpp\</c> (ASCII).</para>
/// </remarks>
public static class SdCppEmbarque
{
    /// <summary>URL par defaut. A surcharger via <c>SD_CPP_RELEASE_URL</c> si besoin.</summary>
    public const string UrlReleaseParDefaut =
        "https://github.com/leejet/stable-diffusion.cpp/releases/download/v0.9.1/sd-v0.9.1-win64.zip";

    public static string Dossier
        => Path.Combine(AppContext.BaseDirectory, "Outils", "sd.cpp");

    private static string FichierRevision
        => Path.Combine(Dossier, "revision.txt");

    /// <summary>Le binaire, ou null s'il n'est pas encore telecharge.</summary>
    public static string? Exe()
    {
        var p = Path.Combine(Dossier, "sd-server.exe");
        return File.Exists(p) ? p : null;
    }

    public static bool EstInstalle() => Exe() is not null;

    public static async Task<string?> InstallerAsync(
        Action<string>? journal = null, CancellationToken arret = default)
    {
        if (Exe() is { } deja)
        {
            journal?.Invoke("sd-server deja installe : " + deja);
            return deja;
        }

        var dossier = Dossier;
        Directory.CreateDirectory(dossier);

        var url = Environment.GetEnvironmentVariable("SD_CPP_RELEASE_URL");
        if (string.IsNullOrEmpty(url)) url = UrlReleaseParDefaut;

        var archive = Path.Combine(dossier, "sd.zip");
        var partiel = archive + ".part";

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            journal?.Invoke("sd.cpp : telechargement " + url);
            using (var reponse = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, arret).ConfigureAwait(false))
            {
                reponse.EnsureSuccessStatusCode();
                var attendu = reponse.Content.Headers.ContentLength ?? 0;
                journal?.Invoke("sd.cpp : " + (attendu / (1024 * 1024)) + " Mo a recevoir");
                await using var source = await reponse.Content.ReadAsStreamAsync(arret).ConfigureAwait(false);
                await using var cible = File.Create(partiel);
                await source.CopyToAsync(cible, arret).ConfigureAwait(false);
            }
            if (File.Exists(archive)) File.Delete(archive);
            File.Move(partiel, archive);
            journal?.Invoke("sd.cpp : extraction...");
            ZipFile.ExtractToDirectory(archive, dossier, overwriteFiles: true);
            File.Delete(archive);

            var exe = Exe();
            if (exe is null)
            {
                journal?.Invoke("sd.cpp : archive ne contient pas sd-server.exe.");
                return null;
            }

            await File.WriteAllTextAsync(FichierRevision, url, arret).ConfigureAwait(false);
            journal?.Invoke("sd.cpp installe : " + exe);
            return exe;
        }
        catch (Exception ex)
        {
            journal?.Invoke("sd.cpp : installation echouee : " + ex.GetType().Name + ": " + ex.Message);
            try { if (File.Exists(partiel)) File.Delete(partiel); } catch { }
            return null;
        }
    }
}