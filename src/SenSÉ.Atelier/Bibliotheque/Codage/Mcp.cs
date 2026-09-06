using System;
using System.Collections.Generic;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Les 6 noeuds qui font le pont avec les MCPs externes. Lambdas sync
/// qui bloquent sur les appels HTTP locaux (loopback).
/// </summary>
public static class Mcp
{
    private static string Awaiter(System.Threading.Tasks.Task<string> t) => t.GetAwaiter().GetResult();
    private static System.Threading.Tasks.Task AwaiterTask(System.Threading.Tasks.Task t) => t;

    public static void Enregistrer()
    {
        // 20. mcp_saisie_screenshot
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_screenshot", "MCP Saisie : Screenshot", "Screenshot d'une fenetre identifiee par son titre (ou ecran entier).",
            Espace.Codage, "MCPs",
            new List<Port>(),
            new List<Port> { new("chemin", TypePort.Fichier, false) },
            new List<ParametreNoeud> { new("titre", "Titre fenetre (vide = ecran)", "texte", "") },
            ctx =>
            {
                try
                {
                    var c = new ClientSaisie();
                    var titre = ctx.Ch("titre");
                    var data = string.IsNullOrEmpty(titre)
                        ? Awaiter(c.ScreenshotEcranAsync())
                        : Awaiter(c.ScreenshotFenetreAsync(titre));
                    return ResultatExecution.Ok(new() { ["chemin"] = ExtraireChemin(data) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 21. mcp_saisie_cliquer
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_cliquer", "MCP Saisie : Clic souris", "Clic souris a des coordonnees precises (via mcp-saisie).",
            Espace.Codage, "MCPs",
            new List<Port> { new("x", TypePort.Nombre, true), new("y", TypePort.Nombre, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud> { new("bouton", "Bouton", "liste", "gauche", new List<string> { "gauche","droit","milieu" }) },
            ctx =>
            {
                try
                {
                    int.TryParse(ctx.Entree("x"), out var x);
                    int.TryParse(ctx.Entree("y"), out var y);
                    var bouton = ctx.Ch("bouton", "gauche");
                    var data = Awaiter(new ClientSaisie().SourisCliquerAsync(bouton, x, y));
                    return ResultatExecution.Ok(new() { ["ok"] = data.Contains("clic") });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 22. mcp_saisie_taper
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_taper", "MCP Saisie : Taper texte", "Tape une chaine au clavier (via mcp-saisie).",
            Espace.Codage, "MCPs",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>(),
            ctx =>
            {
                try
                {
                    var t = ctx.Entree("texte") ?? "";
                    var data = Awaiter(new ClientSaisie().ClavierTaperAsync(t));
                    return ResultatExecution.Ok(new() { ["ok"] = !data.Contains("rien") });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 23. mcp_cdp_naviguer
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_naviguer", "MCP CDP : Naviguer", "Ouvre une URL dans un navigateur (via mcp-cdp).",
            Espace.Codage, "MCPs",
            new List<Port> { new("url", TypePort.Texte, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>(),
            ctx =>
            {
                try
                {
                    var url = ctx.Entree("url") ?? "";
                    AwaiterTask(new ClientCdp().NaviguerAsync(url));
                    return ResultatExecution.Ok(new() { ["ok"] = true });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 24. mcp_cdp_eval_js
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_eval_js", "MCP CDP : Eval JS", "Execute du code JavaScript dans la page courante.",
            Espace.Codage, "MCPs",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? "";
                    var res = Awaiter(new ClientCdp().EvalJsAsync(code));
                    return ResultatExecution.Ok(new() { ["resultat"] = res });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 25. mcp_cdp_screenshot
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_screenshot", "MCP CDP : Screenshot", "Screenshot de la page courante du navigateur.",
            Espace.Codage, "MCPs",
            new List<Port>(),
            new List<Port> { new("chemin", TypePort.Fichier, false) },
            new List<ParametreNoeud>(),
            ctx =>
            {
                try
                {
                    var data = Awaiter(new ClientCdp().ScreenshotAsync());
                    return ResultatExecution.Ok(new() { ["chemin"] = ExtraireChemin(data) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static string ExtraireChemin(string data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        var trimmed = data.Trim().Trim('"');
        if (trimmed.StartsWith("C:\\") || trimmed.StartsWith("C:/")) return trimmed;
        var idx = data.IndexOf("C:\\", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var end = data.IndexOfAny(new[] { '"', ' ', '\n', '\r', ',' }, idx);
            return end < 0 ? data.Substring(idx) : data.Substring(idx, end - idx);
        }
        return trimmed;
    }
}
