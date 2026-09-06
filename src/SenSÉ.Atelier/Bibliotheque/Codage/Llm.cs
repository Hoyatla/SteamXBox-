using System;
using System.Collections.Generic;
using System.Threading;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.ModeleLocal;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Les 10 noeuds qui deleguent au modele local via ClientModele.
/// Lambdas sync (le client est awaitable, on bloque en local).
/// </summary>
public static class Llm
{
    private static ClientModele Client() => new();

    private static string Completer(ClientModele c, string prompt, int maxTokens = 2048)
        => c.CompleterAsync(prompt, maxTokens).GetAwaiter().GetResult();

    public static void Enregistrer()
    {
        // 10. llm_generer_code
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_generer_code", "LLM : Générer code", "Demande au modele local de generer du code a partir d'une description.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("description", TypePort.Texte, true) },
            new List<Port> { new("code", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("langage", "Langage", "liste", "python",
                    new List<string> { "python", "rust", "javascript", "typescript", "csharp", "cpp", "c", "go", "java", "kotlin", "swift", "shell" }),
                new("temperature", "Temperature", "nombre", 0.2, Min: 0.0, Max: 1.0),
            },
            async ctx =>
            {
                try
                {
                    var desc = ctx.Entree("description") ?? ctx.Ch("description");
                    var lang = ctx.Ch("langage", "python");
                    if (string.IsNullOrEmpty(desc)) return ResultatExecution.Fail("description vide");
                    var prompt = "Genere du code " + lang + " pour :\n\n" + desc + "\n\nReponds UNIQUEMENT avec le code, pas d'explication.";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 11. llm_completer_code
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_completer_code", "LLM : Compléter code", "Complete un bout de code la ou il s'arrete.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code_partiel", TypePort.Texte, true) },
            new List<Port> { new("code", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java","kotlin","swift","shell" }) },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code_partiel") ?? ctx.Ch("code_partiel");
                    var lang = ctx.Ch("langage", "python");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Complete ce code " + lang + " (ajoute ce qui manque, garde le style, ne repete pas ce qui est deja la) :\n\n```" + lang + "\n" + code + "\n```\n\nReponds UNIQUEMENT avec le code complet.";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 12. llm_expliquer_code
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_expliquer_code", "LLM : Expliquer code", "Demande une explication en francais d'un bout de code.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("explication", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Explique ce code en francais, de maniere claire et structuree :\n\n```\n" + code + "\n```";
                    return ResultatExecution.Ok(new() { ["explication"] = Completer(Client(), prompt) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 13. llm_reviser_code
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_reviser_code", "LLM : Réviser code", "Reecrit le code en corrigeant bugs, lisibilite, conventions.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("code_revise", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java","kotlin","swift","shell" }),
                new("focus", "Focus", "liste", "qualite", new List<string> { "qualite","performance","securite","style","bugs" }),
            },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    var lang = ctx.Ch("langage", "python");
                    var focus = ctx.Ch("focus", "qualite");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Revise ce code " + lang + " en ameliorant le focus '" + focus + "'. Reponds UNIQUEMENT avec le code revise.\n\n```" + lang + "\n" + code + "\n```";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code_revise"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 14. llm_generer_tests
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_generer_tests", "LLM : Générer tests", "Produit une suite de tests pour le code fourni.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("tests", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","java","go" }),
                new("framework", "Framework", "liste", "auto", new List<string> { "auto","pytest","unittest","xunit","jest","vitest","rust" }),
            },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    var lang = ctx.Ch("langage", "python");
                    var fw = ctx.Ch("framework", "auto");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Ecris une suite de tests pour ce code " + lang + (fw != "auto" ? " avec " + fw : "") + ". Reponds UNIQUEMENT avec le code de test.\n\n```" + lang + "\n" + code + "\n```";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["tests"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 15. llm_traduire_code
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_traduire_code", "LLM : Traduire code", "Traduit du code d'un langage a un autre.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("code_traduit", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("langage_source", "Langage source", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java","kotlin","swift" }),
                new("langage_cible", "Langage cible", "liste", "rust", new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java","kotlin","swift" }),
            },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    var src = ctx.Ch("langage_source", "python");
                    var dst = ctx.Ch("langage_cible", "rust");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Traduis ce code " + src + " en " + dst + ". Reponds UNIQUEMENT avec le code traduit.\n\n```" + src + "\n" + code + "\n```";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code_traduit"] = ExtraireCode(outp, dst) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 16. llm_documenter
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_documenter", "LLM : Documenter", "Ajoute des docstrings/commentaires au code.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port> { new("code_documente", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","java","go" }) },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    var lang = ctx.Ch("langage", "python");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var prompt = "Ajoute des docstrings et commentaires au code " + lang + " suivant. Reponds UNIQUEMENT avec le code.\n\n```" + lang + "\n" + code + "\n```";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code_documente"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 17. llm_refactorer
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_refactorer", "LLM : Refactorer", "Refactore le code selon un objectif (lisibilite, performance, etc).",
            Espace.Codage, "Code (LLM)",
            new List<Port>
            {
                new("code", TypePort.Texte, true),
                new("objectif", TypePort.Texte, true),
            },
            new List<Port> { new("code_refactore", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("langage", "Langage", "liste", "python", new List<string> { "python","rust","javascript","typescript","csharp","cpp","c","go","java" }) },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    var obj = ctx.Entree("objectif") ?? ctx.Ch("objectif");
                    var lang = ctx.Ch("langage", "python");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    if (string.IsNullOrEmpty(obj)) obj = "ameliorer la lisibilite";
                    var prompt = "Refactore ce code " + lang + " avec cet objectif : " + obj + ". Reponds UNIQUEMENT avec le code.\n\n```" + lang + "\n" + code + "\n```";
                    var outp = Completer(Client(), prompt);
                    return ResultatExecution.Ok(new() { ["code_refactore"] = ExtraireCode(outp, lang) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 18. llm_generer_nom
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_generer_nom", "LLM : Générer nom", "Propose un nom de variable/fonction pertinent a partir d'une description.",
            Espace.Codage, "Code (LLM)",
            new List<Port> { new("description", TypePort.Texte, true) },
            new List<Port> { new("nom", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("style", "Style", "liste", "snake_case", new List<string> { "snake_case","camelCase","PascalCase","kebab-case" }) },
            async ctx =>
            {
                try
                {
                    var desc = ctx.Entree("description") ?? ctx.Ch("description");
                    var style = ctx.Ch("style", "snake_case");
                    if (string.IsNullOrEmpty(desc)) return ResultatExecution.Fail("description vide");
                    var prompt = "Propose UN nom de variable en " + style + " pour : " + desc + ". Reponds UNIQUEMENT avec le nom, rien d'autre.";
                    var outp = Completer(Client(), prompt, 64);
                    return ResultatExecution.Ok(new() { ["nom"] = outp.Trim().Trim('`','"','\'') });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 19. llm_repondre_question
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "llm_repondre_question", "LLM : Répondre question", "Pose une question au modele en lui donnant un contexte.",
            Espace.Codage, "Code (LLM)",
            new List<Port>
            {
                new("contexte", TypePort.Texte, true),
                new("question", TypePort.Texte, true),
            },
            new List<Port> { new("reponse", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                try
                {
                    var c = ctx.Entree("contexte") ?? ctx.Ch("contexte");
                    var q = ctx.Entree("question") ?? ctx.Ch("question");
                    if (string.IsNullOrEmpty(q)) return ResultatExecution.Fail("question vide");
                    var prompt = "Contexte :\n" + c + "\n\nQuestion : " + q + "\n\nReponds en francais, de maniere concise.";
                    return ResultatExecution.Ok(new() { ["reponse"] = Completer(Client(), prompt) });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static string ExtraireCode(string reponse, string langage)
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
}
