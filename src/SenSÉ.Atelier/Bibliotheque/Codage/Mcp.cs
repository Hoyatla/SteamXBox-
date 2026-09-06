using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Les 6 noeuds MCP codage. Lambdas async (Executeur retourne Task&lt;ResultatExecution&gt;).
/// </summary>
public static class Mcp
{
    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_screenshot", "MCP Saisie : Screenshot", "Screenshot d'une fenetre identifiee par son titre (ou ecran entier).",
            Espace.Codage, "MCPs",
            new List<Port>(),
            new List<Port> { new("chemin", TypePort.Fichier, false) },
            new List<ParametreNoeud> { new("titre", "Titre fenetre (vide = ecran)", "texte", "") },
            async ctx =>
            {
                try
                {
                    var c = new ClientSaisie();
                    var titre = ctx.Ch("titre");
                    var data = string.IsNullOrEmpty(titre)
                        ? await c.ScreenshotEcranAsync()
                        : await c.ScreenshotFenetreAsync(titre);
                    return ResultatExecution.Ok(new() { ["chemin"] = ExtraireChemin(data) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_cliquer", "MCP Saisie : Clic souris", "Clic souris a des coordonnees precises.",
            Espace.Codage, "MCPs",
            new List<Port> { new("x", TypePort.Nombre, true), new("y", TypePort.Nombre, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud> { new("bouton", "Bouton", "liste", "gauche", new List<string> { "gauche","droit","milieu" }) },
            async ctx =>
            {
                try
                {
                    int.TryParse(ctx.Entree("x"), out var x);
                    int.TryParse(ctx.Entree("y"), out var y);
                    var bouton = ctx.Ch("bouton", "gauche");
                    var data = await new ClientSaisie().SourisCliquerAsync(bouton, x, y);
                    return ResultatExecution.Ok(new() { ["ok"] = data.Contains("clic") });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_saisie_taper", "MCP Saisie : Taper texte", "Tape une chaine au clavier.",
            Espace.Codage, "MCPs",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var t = ctx.Entree("texte") ?? "";
                    var data = await new ClientSaisie().ClavierTaperAsync(t);
                    return ResultatExecution.Ok(new() { ["ok"] = !data.Contains("rien") });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_naviguer", "MCP CDP : Naviguer", "Ouvre une URL dans un navigateur.",
            Espace.Codage, "MCPs",
            new List<Port> { new("url", TypePort.Texte, true) },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var url = ctx.Entree("url") ?? "";
                    await new ClientCdp().NaviguerAsync(url);
                    return ResultatExecution.Ok(new() { ["ok"] = true });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_eval_js", "MCP CDP : Eval JS", "Execute du JavaScript dans la page courante.",
            Espace.Codage, "MCPs",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("resultat", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? "";
                    var res = await new ClientCdp().EvalJsAsync(code);
                    return ResultatExecution.Ok(new() { ["resultat"] = res });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "mcp_cdp_screenshot", "MCP CDP : Screenshot", "Screenshot de la page courante du navigateur.",
            Espace.Codage, "MCPs",
            new List<Port>(),
            new List<Port> { new("chemin", TypePort.Fichier, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var data = await new ClientCdp().ScreenshotAsync();
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
