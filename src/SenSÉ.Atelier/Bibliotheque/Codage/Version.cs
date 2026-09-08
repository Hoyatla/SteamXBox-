using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Versionning;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 3 noeuds de versioning : version_creer, version_lister, version_restaurer.
/// Accedent au Versionneur partage (initialise au boot de l'Atelier).
/// </summary>
public static class Version
{
    private static Versionneur? _versionneur;

    public static void Initialiser(string racine, SenSÉ.Atelier.Serialisation.Persistance persistance)
    {
        _versionneur = new Versionneur(racine, persistance);
    }

    public static void Enregistrer()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "version_creer", "version_creer",
            "Cree un snapshot du graphe courant dans Outils/Atelier/Versionning/. Chaque appel = une nouvelle version.",
            Espace.Codage, "Versionning",
            new List<Port>(),
            new List<Port>
            {
                new("version_id", TypePort.Texte, false),
                new("nb_versions", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "ID du graphe (vide = ce noeud)", "texte", ""),
                new("message", "Message", "texte", "Auto-save"),
            },
            ctx =>
            {
                if (_versionneur is null) return Task.FromResult(ResultatExecution.Fail("version_creer: versionneur non initialise"));
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid)) return Task.FromResult(ResultatExecution.Fail("version_creer: graphe_id vide"));
                var msg = ctx.Ch("message", "Auto-save");
                try
                {
                    var vid = _versionneur.Versionner(gid, msg);
                    var n = _versionneur.Lister(gid).Count;
                    return Task.FromResult(ResultatExecution.Ok(new() { ["version_id"] = vid, ["nb_versions"] = n }));
                }
                catch (Exception ex) { return Task.FromResult(ResultatExecution.Fail("version_creer: " + ex.Message)); }
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "version_lister", "version_lister",
            "Liste les versions precedentes d'un graphe (sortie JSON array avec version_id, message, created_at, nb_noeuds, nb_liens).",
            Espace.Codage, "Versionning",
            new List<Port>(),
            new List<Port>
            {
                new("liste", TypePort.Texte, false),
                new("nb", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "ID du graphe", "texte", ""),
            },
            ctx =>
            {
                if (_versionneur is null) return Task.FromResult(ResultatExecution.Fail("version_lister: versionneur non initialise"));
                var gid = ctx.Ch("graphe_id", "");
                if (string.IsNullOrEmpty(gid)) return Task.FromResult(ResultatExecution.Fail("version_lister: graphe_id vide"));
                var versions = _versionneur.Lister(gid);
                var arr = new JsonArray();
                foreach (var v in versions) arr.Add(new JsonObject
                {
                    ["version_id"] = v.VersionId,
                    ["message"] = v.Message,
                    ["created_at"] = v.CreatedAt.ToString("o"),
                    ["nb_noeuds"] = v.NbNoeuds,
                    ["nb_liens"] = v.NbLiens,
                });
                return Task.FromResult(ResultatExecution.Ok(new() { ["liste"] = arr.ToJsonString(), ["nb"] = versions.Count }));
            }
        ));

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "version_restaurer", "version_restaurer",
            "Restaure un graphe depuis une version. Le fichier actuel est ecrase.",
            Espace.Codage, "Versionning",
            new List<Port>(),
            new List<Port>
            {
                new("ok", TypePort.Booleen, false),
                new("nb_noeuds", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("graphe_id", "ID du graphe", "texte", ""),
                new("version_id", "ID de la version a restaurer", "texte", ""),
            },
            ctx =>
            {
                if (_versionneur is null) return Task.FromResult(ResultatExecution.Fail("version_restaurer: versionneur non initialise"));
                var gid = ctx.Ch("graphe_id", "");
                var vid = ctx.Ch("version_id", "");
                if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(vid))
                    return Task.FromResult(ResultatExecution.Fail("version_restaurer: graphe_id et version_id requis"));
                if (!_versionneur.Restaurer(gid, vid, out var err))
                    return Task.FromResult(ResultatExecution.Fail("version_restaurer: " + err));
                // Recharge le graphe pour recuperer le nb de noeuds
                var g = _versionneur.Persistance?.ChargerGraphe(gid);
                return Task.FromResult(ResultatExecution.Ok(new() { ["ok"] = true, ["nb_noeuds"] = g?.Noeuds.Count ?? 0 }));
            }
        ));
    }
}
