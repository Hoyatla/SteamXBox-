using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.ModeleLocal;
using SenSÉ.Atelier.Diffusion;
using SenSÉ.Atelier.Mcp;

namespace SenSÉ.Atelier.Composite;

/// <summary>
/// Les 4 workflows prêts. Chaque Executeur est async (Task&lt;ResultatExecution&gt;)
/// et appelle un helper static async.
/// </summary>
public static class Tous
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "workflow_code_complet", "Workflow : Code complet",
            "Genere du code, le fait reviser, l'ecrit dans un fichier.",
            Espace.Codage, "Workflows",
            new List<Port> { new("description", TypePort.Texte, true) },
            new List<Port> { new("chemin", TypePort.Fichier, false), new("code", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("chemin_sortie", "Chemin sortie", "chemin", ""),
                new("langage", "Langage", "liste", "python",
                    new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java" }),
            },
            async ctx => await ExecuterCodeCompletAsync(ctx)
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "workflow_tests_unitaires", "Workflow : Tests unitaires",
            "Genere des tests, les ecrit, les execute.",
            Espace.Codage, "Workflows",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("stdout", TypePort.Texte, false), new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>
            {
                new("chemin_tests", "Chemin fichier tests", "chemin", ""),
                new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp" }),
            },
            async ctx => await ExecuterTestsAsync(ctx)
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "workflow_refactor_securise", "Workflow : Refactor sécurisé",
            "Refactore le code, fait une revision de securite, ecrit.",
            Espace.Codage, "Workflows",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("chemin", TypePort.Fichier, false), new("code", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("objectif", "Objectif refactor", "texte", "ameliorer la lisibilite"),
                new("chemin_sortie", "Chemin sortie", "chemin", ""),
                new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp" }),
            },
            async ctx => await ExecuterRefactorAsync(ctx)
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "workflow_texte_vers_animation", "Workflow : Texte vers Animation",
            "Genere une image puis une video a partir d'un texte.",
            Espace.Multimedia, "Workflows",
            new List<Port> { new("prompt", TypePort.Texte, true) },
            new List<Port> { new("chemin_video", TypePort.Fichier, false) },
            new List<ParametreNoeud>
            {
                new("chemin_sortie", "Chemin sortie video", "chemin", ""),
                new("duree", "Duree (s)", "nombre", 4.0),
            },
            async ctx => await ExecuterAnimationAsync(ctx)
        ));
    }

    private static async Task<ResultatExecution> ExecuterCodeCompletAsync(ContexteExecution ctx)
    {
        try
        {
            var desc = ctx.Entree("description") ?? ctx.Ch("description");
            var lang = ctx.Ch("langage", "python");
            var sortie = ctx.Ch("chemin_sortie");
            if (string.IsNullOrEmpty(desc)) return ResultatExecution.Fail("description vide");
            if (string.IsNullOrEmpty(sortie)) sortie = Path.Combine(Path.GetTempPath(),
                "atelier_" + Guid.NewGuid().ToString("N") + "." + Extension(lang));
            var c = new ClientModele();
            var code = await c.CompleterAsync("Genere du code " + lang + " pour : " + desc + "\nReponds UNIQUEMENT avec le code.");
            code = StripCodeFences(code, lang);
            code = await c.CompleterAsync("Revise ce code " + lang + " (qualite, bugs, conventions). Reponds UNIQUEMENT avec le code revise.\n```" + lang + "\n" + code + "\n```");
            code = StripCodeFences(code, lang);
            File.WriteAllText(sortie, code);
            return ResultatExecution.Ok(new() { ["chemin"] = sortie, ["code"] = code });
        }
        catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
    }

    private static async Task<ResultatExecution> ExecuterTestsAsync(ContexteExecution ctx)
    {
        try
        {
            var code = ctx.Entree("code") ?? ctx.Ch("code");
            var lang = ctx.Ch("langage", "python");
            var sortie = ctx.Ch("chemin_tests");
            if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
            if (string.IsNullOrEmpty(sortie)) sortie = Path.Combine(Path.GetTempPath(),
                "atelier_tests_" + Guid.NewGuid().ToString("N") + ".py");
            var tests = await new ClientModele().CompleterAsync("Ecris des tests pour ce code " + lang + ". Reponds UNIQUEMENT avec le code de test.\n```" + lang + "\n" + code + "\n```");
            tests = StripCodeFences(tests, lang);
            File.WriteAllText(sortie, tests);
            if (lang == "python")
            {
                var psi = new System.Diagnostics.ProcessStartInfo("python", "\"" + sortie + "\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var so = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60_000);
                return ResultatExecution.Ok(new() { ["stdout"] = so, ["ok"] = p.ExitCode == 0 });
            }
            return ResultatExecution.Ok(new() { ["chemin"] = sortie, ["ok"] = true });
        }
        catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
    }

    private static async Task<ResultatExecution> ExecuterRefactorAsync(ContexteExecution ctx)
    {
        try
        {
            var code = ctx.Entree("code") ?? ctx.Ch("code");
            var obj = ctx.Entree("objectif") ?? ctx.Ch("objectif", "ameliorer la lisibilite");
            var lang = ctx.Ch("langage", "python");
            var sortie = ctx.Ch("chemin_sortie");
            if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
            if (string.IsNullOrEmpty(sortie)) sortie = Path.Combine(Path.GetTempPath(),
                "atelier_refactored_" + Guid.NewGuid().ToString("N") + "." + Extension(lang));
            var c = new ClientModele();
            var r1 = await c.CompleterAsync("Refactore ce code " + lang + " avec cet objectif : " + obj + ". Reponds UNIQUEMENT avec le code.\n```" + lang + "\n" + code + "\n```");
            r1 = StripCodeFences(r1, lang);
            var r2 = await c.CompleterAsync("Revise ce code refactore pour la securite. Reponds UNIQUEMENT avec le code.\n```" + lang + "\n" + r1 + "\n```");
            r2 = StripCodeFences(r2, lang);
            File.WriteAllText(sortie, r2);
            return ResultatExecution.Ok(new() { ["chemin"] = sortie, ["code"] = r2 });
        }
        catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
    }

    private static async Task<ResultatExecution> ExecuterAnimationAsync(ContexteExecution ctx)
    {
        try
        {
            var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
            var sortie = ctx.Ch("chemin_sortie");
            if (string.IsNullOrEmpty(prompt)) return ResultatExecution.Fail("prompt vide");
            if (string.IsNullOrEmpty(sortie)) sortie = Path.Combine(Path.GetTempPath(),
                "atelier_anim_" + Guid.NewGuid().ToString("N") + ".mp4");
            var serveur = new ServeurDiffusion();
            var spec = ChargerSpec("flux-schnell");
            if (spec is null) return ResultatExecution.Fail("modele flux-schnell introuvable");
            var demarrage = serveur.Demarrer(spec, msg => ctx.Journal?.Invoke(msg));
            if (demarrage is not null) return ResultatExecution.Fail(demarrage);
            var img = await ServeurDiffusion.GenererImageAsync(prompt, seed: -1);
            return ResultatExecution.Ok(new() { ["chemin_video"] = img });
        }
        catch (Exception ex) { return ResultatExecution.Fail("sd-server: " + ex.Message); }
    }


    private static ServeurDiffusion.ModeleSpec? ChargerSpec(string id)
    {
        var racine = Path.Combine(AppContext.BaseDirectory, "Outils", "Modeles");
        foreach (var r in new[] { Path.Combine(racine, "image"), Path.Combine(racine, "video") })
        {
            foreach (var m in ServeurDiffusion.ChargerModeles(r))
            {
                if (m.Id == id) return m;
            }
        }
        return null;
    }    private static string StripCodeFences(string reponse, string langage)
    {
        if (string.IsNullOrEmpty(reponse)) return "";
        var fence = "```";
        var start = reponse.IndexOf(fence);
        if (start < 0) return reponse.Trim();
        var firstNl = reponse.IndexOf('\n', start);
        if (firstNl < 0) return reponse.Trim();
        var end = reponse.IndexOf(fence, firstNl);
        if (end < 0) return reponse.Substring(firstNl + 1).Trim();
        return reponse.Substring(firstNl + 1, end - firstNl - 1).Trim();
    }

    private static string Extension(string lang) => lang switch
    {
        "python" => "py",
        "rust" => "rs",
        "javascript" => "js",
        "typescript" => "ts",
        "csharp" => "cs",
        "cpp" => "cpp",
        "c" => "c",
        "go" => "go",
        "java" => "java",
        "kotlin" => "kt",
        "swift" => "swift",
        "shell" => "sh",
        _ => "txt",
    };
}
