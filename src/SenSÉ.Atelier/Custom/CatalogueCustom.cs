using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Custom;

/// <summary>
/// Recharge les noeuds custom depuis le disque et les enregistre dans
/// le CatalogueNoeuds. L'execution d'un noeud custom sauvegarde le code
/// dans un .py temp et fait tourner Python.
/// </summary>
public static class CatalogueCustom
{
    public static List<NoeudCustom> Tous { get; private set; } = new();

    public static void Recharger(string racinePersistance)
    {
        Tous = new Persistance(racinePersistance).ListerCustom();
        // Re-enregistrer dans le catalogue
        foreach (var nc in Tous)
        {
            EnregistrerNoeud(nc);
        }
    }

    public static string Creer(NoeudCustom nc, string racine)
    {
        if (string.IsNullOrEmpty(nc.Id)) nc.Id = Slug(nc.Nom.Length > 0 ? nc.Nom : "noeud");
        var p = new Persistance(racine);
        var id = p.SauvegarderCustom(nc);
        Recharger(racine);
        return id;
    }

    public static void Supprimer(string id, string racine)
    {
        new Persistance(racine).SupprimerCustom(id);
        Recharger(racine);
    }

    private static void EnregistrerNoeud(NoeudCustom nc)
    {
        EspaceExtensions.TryParse(nc.Espace, out var espace);
        var portsEntree = nc.PortsEntree
            .Select(p => new Port(p.Nom, ParseType(p.Type), true)).ToList();
        var portsSortie = nc.PortsSortie
            .Select(p => new Port(p.Nom, ParseType(p.Type), false)).ToList();
        var paramsNoeud = nc.Params
            .Select(p => new ParametreNoeud(p.Nom, p.Libelle.Length > 0 ? p.Libelle : p.Nom, p.Type, p.Defaut))
            .ToList();

        // Capture du code et de la liste des sorties pour l'executeur
        var code = nc.Code;
        var sorties = portsSortie;

        var def = new DefinitionNoeud(
            nc.Id,
            nc.Nom,
            nc.Description,
            espace,
            nc.Categorie.Length > 0 ? nc.Categorie : "Perso",
            portsEntree,
            portsSortie,
            paramsNoeud,
            async ctx => ExecuterPython(ctx, code, sorties)
        );
        CatalogueNoeuds.Enregistrer(def);
    }

    private static TypePort ParseType(string s)
        => Enum.TryParse<TypePort>(s, true, out var t) ? t : TypePort.Texte;

    private static ResultatExecution ExecuterPython(
        ContexteExecution ctx, string code, IReadOnlyList<Port> sorties)
    {
        try
        {
            // Injecter les entrees comme variables Python
            var sb = new System.Text.StringBuilder();
            foreach (var (cle, val) in ctx.Entrees)
            {
                var repr = val is null ? "None"
                    : val is string s ? System.Text.Json.JsonSerializer.Serialize(s)
                    : val is bool b ? (b ? "True" : "False")
                    : val is double || val is float || val is int || val is long ? val.ToString()!.Replace(',', '.')
                    : System.Text.Json.JsonSerializer.Serialize(val);
                sb.Append(cle).Append(" = ").Append(repr).Append('\n');
            }
            foreach (var (cle, val) in ctx.Params)
            {
                var repr = val is null ? "None"
                    : val is string s ? System.Text.Json.JsonSerializer.Serialize(s)
                    : val is bool b ? (b ? "True" : "False")
                    : val is double || val is float || val is int || val is long ? val.ToString()!.Replace(',', '.')
                    : System.Text.Json.JsonSerializer.Serialize(val);
                sb.Append("p_").Append(cle).Append(" = ").Append(repr).Append('\n');
            }
            // Recupere chaque sortie attendue dans le namespace
            foreach (var p in sorties)
            {
                sb.Append("__captures__ = dict(__captures__)\n");
                sb.Append("if '").Append(p.Nom).Append("' in dir():\n");
                sb.Append("    __captures__['").Append(p.Nom).Append("'] = ").Append(p.Nom).Append('\n');
            }
            sb.Append(code);

            var tmp = Path.Combine(Path.GetTempPath(),
                "atelier_custom_" + Guid.NewGuid().ToString("N") + ".py");
            File.WriteAllText(tmp, sb.ToString());

            // Wrapper qui declare __captures__ et fait un exec
            var wrapper =
                "__captures__ = {}\n" +
                "try:\n" +
                "    exec(open(r'" + tmp.Replace("\\", "\\\\") + "', encoding='utf-8').read(), globals())\n" +
                "except Exception as __e:\n" +
                "    import traceback; traceback.print_exc()\n" +
                "    __captures__['__erreur__'] = str(__e)\n" +
                "import json\n" +
                "print('@@ATELIER_OUT@@' + json.dumps(__captures__, default=str, ensure_ascii=False))\n";

            var wrapperPath = Path.Combine(Path.GetTempPath(),
                "atelier_run_" + Guid.NewGuid().ToString("N") + ".py");
            File.WriteAllText(wrapperPath, wrapper);

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("python", $"\"{wrapperPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var so = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30_000);

                // Parse la ligne @@ATELIER_OUT@@{...}
                var marker = "@@ATELIER_OUT@@";
                var idx = so.IndexOf(marker);
                if (idx < 0) return ResultatExecution.Fail("aucune sortie du script", so);
                var json = so.Substring(idx + marker.Length).Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var dict = new Dictionary<string, object?>();
                foreach (var el in doc.RootElement.EnumerateObject())
                {
                    object? val = el.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => el.Value.GetString(),
                        System.Text.Json.JsonValueKind.Number => el.Value.GetDouble(),
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        System.Text.Json.JsonValueKind.Null => null,
                        _ => el.Value.GetRawText(),
                    };
                    dict[el.Name] = val;
                }
                if (dict.TryGetValue("__erreur__", out var err))
                    return ResultatExecution.Fail(err?.ToString() ?? "erreur", so);
                dict.Remove("__erreur__");
                return ResultatExecution.Ok(dict, so);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
                try { File.Delete(wrapperPath); } catch { }
            }
        }
        catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
    }

    private static string Slug(string s)
    {
        var slug = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            slug.Append(char.IsLetterOrDigit(c) ? c : '_');
        var r = slug.ToString().Trim('_');
        return r.Length == 0 ? "noeud_" + Guid.NewGuid().ToString("N").Substring(0, 6) : r;
    }
}