using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Les 9 noeuds primitifs : sources de donnees et operations
/// fichier/processus. Aucun n'invoque le modele ou un MCP externe.
/// </summary>
public static class Primitives
{
    public static void Enregistrer()
    {
        // 1. texte
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte", "Texte", "Une constante texte, editable dans l'inspecteur.",
            Espace.Codage, "Texte",
            new List<Port>(),
            new List<Port> { new("valeur", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("contenu", "Contenu", "multiligne", "") },
            async ctx => ResultatExecution.Ok(new() { ["valeur"] = ctx.Ch("contenu") })
        ));

        // 2. nombre
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "nombre", "Nombre", "Une constante numerique, editable dans l'inspecteur.",
            Espace.Codage, "Texte",
            new List<Port>(),
            new List<Port> { new("valeur", TypePort.Nombre, false) },
            new List<ParametreNoeud> { new("valeur", "Valeur", "nombre", 0.0) },
            async ctx => ResultatExecution.Ok(new() { ["valeur"] = ctx.ChDouble("valeur") })
        ));

        // 3. booleen
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "booleen", "Booléen", "Vrai ou faux, editable dans l'inspecteur.",
            Espace.Codage, "Texte",
            new List<Port>(),
            new List<Port> { new("valeur", TypePort.Booleen, false) },
            new List<ParametreNoeud> { new("valeur", "Valeur", "booleen", false) },
            async ctx => ResultatExecution.Ok(new() { ["valeur"] = ctx.ChBool("valeur") })
        ));

        // 4. fichier_lire
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "fichier_lire", "Fichier → Texte", "Lit un fichier texte et expose son contenu.",
            Espace.Codage, "Fichier",
            new List<Port> { new("chemin", TypePort.Fichier, true) },
            new List<Port> { new("contenu", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var chemin = ctx.Entree("chemin") ?? ctx.Ch("chemin");
                if (string.IsNullOrEmpty(chemin)) return ResultatExecution.Fail("chemin vide");
                if (!File.Exists(chemin)) return ResultatExecution.Fail("fichier introuvable: " + chemin);
                return ResultatExecution.Ok(new() { ["contenu"] = File.ReadAllText(chemin) });
            }
        ));

        // 5. fichier_ecrire
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "fichier_ecrire", "Texte → Fichier", "Ecrit un texte dans un fichier (le cree si besoin).",
            Espace.Codage, "Fichier",
            new List<Port>
            {
                new("contenu", TypePort.Texte, true),
                new("chemin", TypePort.Fichier, true),
            },
            new List<Port> { new("ok", TypePort.Booleen, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var contenu = ctx.Entree("contenu") ?? "";
                var chemin = ctx.Entree("chemin") ?? ctx.Ch("chemin");
                if (string.IsNullOrEmpty(chemin)) return ResultatExecution.Fail("chemin vide");
                var dir = Path.GetDirectoryName(chemin);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(chemin, contenu);
                return ResultatExecution.Ok(new() { ["ok"] = true });
            }
        ));

        // 6. lister_fichiers
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "lister_fichiers", "Lister fichiers", "Liste les fichiers d'un dossier (motif glob optionnel).",
            Espace.Codage, "Fichier",
            new List<Port> { new("dossier", TypePort.Fichier, true) },
            new List<Port> { new("liste", TypePort.Liste, false) },
            new List<ParametreNoeud> { new("motif", "Motif (ex: *.cs)", "texte", "*") },
            async ctx =>
            {
                var dossier = ctx.Entree("dossier") ?? ctx.Ch("dossier");
                var motif = ctx.Ch("motif", "*");
                if (string.IsNullOrEmpty(dossier) || !Directory.Exists(dossier))
                    return ResultatExecution.Fail("dossier introuvable: " + dossier);
                var files = Directory.EnumerateFiles(dossier, motif, SearchOption.AllDirectories).ToList();
                return ResultatExecution.Ok(new() { ["liste"] = files });
            }
        ));

        // 7. concatener
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "concatener", "Concaténer", "Joint plusieurs chaines avec un separateur.",
            Espace.Codage, "Texte",
            new List<Port> { new("textes", TypePort.Liste, true) },
            new List<Port> { new("texte", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("separateur", "Séparateur", "texte", "\n") },
            async ctx =>
            {
                var sep = ctx.Ch("separateur", "\n");
                var raw = ctx.Entree("textes");
                string[] textes;
                if (raw is null) textes = Array.Empty<string>();
                else if (raw is string s) textes = new[] { s };
                else if (raw is System.Collections.IEnumerable e) textes = e.Cast<object?>().Select(o => o?.ToString() ?? "").ToArray();
                else textes = new[] { raw.ToString() ?? "" };
                return ResultatExecution.Ok(new() { ["texte"] = string.Join(sep, textes) });
            }
        ));

        // 8. executer_python
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "executer_python", "Exécuter Python", "Execute un script Python et capture stdout/stderr.",
            Espace.Codage, "Code",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port>
            {
                new("stdout", TypePort.Texte, false),
                new("stderr", TypePort.Texte, false),
            },
            new List<ParametreNoeud> { new("python", "Chemin python.exe", "chemin", "python") },
            async ctx =>
            {
                var code = ctx.Entree("code") ?? ctx.Ch("code");
                if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                var py = ctx.Ch("python", "python");
                var tmp = Path.Combine(Path.GetTempPath(), "atelier_" + Guid.NewGuid().ToString("N") + ".py");
                File.WriteAllText(tmp, code);
                try
                {
                    var psi = new ProcessStartInfo(py, "\"" + tmp + "\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi)!;
                    var so = p.StandardOutput.ReadToEnd();
                    var se = p.StandardError.ReadToEnd();
                    p.WaitForExit(30_000);
                    return ResultatExecution.Ok(new() { ["stdout"] = so, ["stderr"] = se });
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        ));

        // 9. executer_commande
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "executer_commande", "Exécuter commande", "Execute un programme externe avec des arguments.",
            Espace.Codage, "Code",
            new List<Port>
            {
                new("commande", TypePort.Texte, true),
                new("args", TypePort.Texte, true),
            },
            new List<Port> { new("stdout", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var cmd = ctx.Entree("commande") ?? ctx.Ch("commande");
                var args = ctx.Entree("args") ?? ctx.Ch("args");
                if (string.IsNullOrEmpty(cmd)) return ResultatExecution.Fail("commande vide");
                try
                {
                    var psi = new ProcessStartInfo(cmd, args ?? "")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi)!;
                    var so = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30_000);
                    return ResultatExecution.Ok(new() { ["stdout"] = so });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }
}
