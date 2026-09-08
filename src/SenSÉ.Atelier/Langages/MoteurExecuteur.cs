using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Execute un code source via un MoteurSpec : ecrit le fichier, lance le binaire,
/// attend la fin (avec limite), retourne stdout/stderr/code_retour/duree_ms.
/// </summary>
public static class MoteurExecuteur
{
    public sealed record Resultat(
        int CodeRetour,
        string Stdout,
        string Stderr,
        long DureeMs,
        string? SortieFichier = null);

    private static string? _racineExecutions;

    /// <summary>
    /// Calcule la racine des dossiers d execution a partir de la racine de
    /// persistance (Outils/Atelier/). Meme logique que Detecteur.Initialiser.
    /// </summary>
    public static void Initialiser(string racinePersistance)
    {
        var direct = Path.Combine(racinePersistance, "Executions");
        _racineExecutions = Directory.Exists(direct) || Directory.Exists(Path.GetDirectoryName(direct)!)
            ? direct
            : Path.Combine(AppContext.BaseDirectory, "Outils", "Atelier", "Executions");
    }

    /// <summary>Repertoire d execution isole : Executions/&lt;uuid&gt;/</summary>
    public static string CreerDossierExecution()
    {
        var base_ = _racineExecutions ?? Path.Combine(AppContext.BaseDirectory, "Outils", "Atelier", "Executions");
        Directory.CreateDirectory(base_);
        var dir = Path.Combine(base_, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static async Task<Resultat> ExecuterAsync(
        MoteurSpec moteur, string code, int limiteSecondes = 30, CancellationToken ct = default)
    {
        var dirExec = CreerDossierExecution();
        var ext = moteur.Extensions.Count > 0 ? moteur.Extensions[0] : ".txt";
        var fichier = Path.Combine(dirExec, "source" + ext);
        await File.WriteAllTextAsync(fichier, code, ct).ConfigureAwait(false);

        var sortie = moteur.Compile ? Path.Combine(dirExec, "sortie" + (moteur.Id == "c" ? ".exe" : ".exe")) : null;
        var sw = Stopwatch.StartNew();
        var (codeRetour, stdout, stderr) = await LancerAsync(moteur, fichier, sortie, limiteSecondes, ct).ConfigureAwait(false);
        sw.Stop();
        return new Resultat(codeRetour, stdout, stderr, sw.ElapsedMilliseconds, sortie);
    }

    private static async Task<(int code, string stdout, string stderr)> LancerAsync(
        MoteurSpec moteur, string fichier, string? sortie, int limiteSecondes, CancellationToken ct)
    {
        // Si compile : d'abord compiler, puis executer le binaire
        if (moteur.Compile)
        {
            if (sortie is null) sortie = Path.Combine(Path.GetDirectoryName(fichier)!, "sortie.exe");
            var argsC = moteur.ResoudreCommandeCompilation(fichier, sortie);
            var compileResult = await RunAsync(argsC[0], argsC.Skip(1).ToArray(), Path.GetDirectoryName(fichier)!, limiteSecondes, ct).ConfigureAwait(false);
            if (compileResult.code != 0)
            {
                return (compileResult.code, compileResult.stdout, "[compilation] " + compileResult.stderr);
            }
            // Executer le binaire compile
            return await RunAsync(sortie, Array.Empty<string>(), Path.GetDirectoryName(fichier)!, limiteSecondes, ct).ConfigureAwait(false);
        }
        // Sinon : juste executer
        var args = moteur.ResoudreCommande(fichier);
        return await RunAsync(args[0], args.Skip(1).ToArray(), Path.GetDirectoryName(fichier)!, limiteSecondes, ct).ConfigureAwait(false);
    }

    private static async Task<(int code, string stdout, string stderr)> RunAsync(
        string programme, string[] args, string workingDir, int limiteSecondes, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = programme,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Cas special : cmd /c avec un .bat dont le path contient des espaces.
        // ArgumentList produit cmd /c "C:\Program Files\..." arg, mais cmd coupe
        // au premier espace. On construit alors la ligne de commande verbatim
        // avec la forme cmd /c ""path" arg" (extra outer quotes que cmd strip).
        bool isCmdC = (string.Equals(programme, "cmd.exe", System.StringComparison.OrdinalIgnoreCase) || string.Equals(programme, "cmd", System.StringComparison.OrdinalIgnoreCase))
                      && args.Length >= 3
                      && (args[0] == "/c" || (args.Length >= 4 && args[0] == "/s" && args[1] == "/c"));
        if (isCmdC)
        {
            int decale = 1;
            if (args[0] == "/s") decale = 2;
            if (decale < args.Length && args[decale] == "call") decale++;
            var bat = args[decale];
            var restants = new System.Collections.Generic.List<string>();
            for (int j = decale + 1; j < args.Length; j++) restants.Add(QuoteSiBesoin(args[j]));
            // Forme safe : cmd /c ""batPath" arg1 arg2"
            psi.Arguments = "/c call \"" + bat + "\" " + string.Join(" ", restants);
        }
        else
        {
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ctsTimeout.CancelAfter(TimeSpan.FromSeconds(limiteSecondes));
        try
        {
            await p.WaitForExitAsync(ctsTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch { }
            return (-1, stdout.ToString(), "[timeout " + limiteSecondes + "s] " + stderr.ToString());
        }
        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Wrap l'argument en double-quotes s'il contient des espaces ou caracteres speciaux.</summary>
    private static string QuoteSiBesoin(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        bool besoin = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || "\"'`<>|&;()$".IndexOf(c) >= 0) { besoin = true; break; }
        }
        if (!besoin) return s;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

}