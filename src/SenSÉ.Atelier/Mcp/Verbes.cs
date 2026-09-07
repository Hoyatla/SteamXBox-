using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Custom;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Diffusion;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.Mcp;

/// <summary>
/// Dispatch des verbes HTTP. Chaque verbe prend un JsonObject (le body)
/// et renvoie un JsonObject (la reponse). Format de retour uniforme :
/// <c>{ok:true, data:...}</c> ou <c>{ok:false, error:"..."}</c>.
/// </summary>
public sealed class Verbes
{
    private readonly Persistance _persistance;
    public Verbes(string racine) { _persistance = new Persistance(racine); }

    public string Racine => _persistance.Racine;

    public object Appeler(string verbe, JsonObject? body, IReadOnlyDictionary<string, string> query)
    {
        try
        {
            body ??= new JsonObject();
            return verbe switch
            {
                "catalogue/espaces"        => CatalogueEspaces(),
                "catalogue/types"          => CatalogueTypes(query),
                "catalogue/type"           => CatalogueType(query),
                "graphe/nouveau"           => GrapheNouveau(body!),
                "graphe/dupliquer"         => GrapheDupliquer(body!),
                "graphe/supprimer"         => GrapheSupprimer(body!),
                "graphe/renommer"          => GrapheRenommer(body!),
                "graphe/lister"            => GrapheLister(),
                "graphe/charger"           => GrapheCharger(body!),
                "graphe/exporter"          => GrapheExporter(body!),
                "graphe/importer"          => GrapheImporter(body!),
                "noeud/ajouter"            => NoeudAjouter(body!),
                "noeud/supprimer"          => NoeudSupprimer(body!),
                "noeud/deplacer"           => NoeudDeplacer(body!),
                "noeud/modifier"           => NoeudModifier(body!),
                "noeud/dupliquer"          => NoeudDupliquer(body!),
                "lien/creer"               => LienCreer(body!),
                "lien/supprimer"           => LienSupprimer(body!),
                "executer"                 => Executer(body!),
                "executer_noeud"           => ExecuterNoeud(body!),
                "execution/etat"           => ExecutionEtat(query),
                "execution/annuler"        => ExecutionAnnuler(body!),
                "annuler"                  => Annuler(body!),
                "refaire"                  => Refaire(body!),
                "custom/creer"             => CustomCreer(body!),
                "custom/supprimer"         => CustomSupprimer(body!),
                "custom/lister"            => CustomLister(),
                "modeles"                  => ModelesLister(),
                "etat"                     => ExecutionEtat(query),
                _ => new { ok = false, error = "verbe inconnu: " + verbe },
            };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    // =============== CATALOGUE ===============

    private object CatalogueEspaces()
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        var espaces = new List<object>();
        foreach (Espace e in Enum.GetValues<Espace>())
        {
            var nb = CatalogueNoeuds.ParEspace(e).Count;
            espaces.Add(new { id = e.Id(), nom = e.Libelle(), nb_types = nb });
        }
        return new { ok = true, data = new { espaces } };
    }

    private object CatalogueTypes(IReadOnlyDictionary<string, string> query)
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        Espace espace = Espace.Codage;
        if (query.TryGetValue("espace", out var es) && !EspaceExtensions.TryParse(es, out espace)) { }
        var types = CatalogueNoeuds.ParEspace(espace).Select(ToTypeDto).ToList();
        return new { ok = true, data = new { espace = espace.Id(), types } };
    }

    private object CatalogueType(IReadOnlyDictionary<string, string> query)
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        if (!query.TryGetValue("type_id", out var id))
            return new { ok = false, error = "type_id manquant" };
        var def = CatalogueNoeuds.Trouver(id);
        if (def is null) return new { ok = false, error = "type inconnu: " + id };
        return new { ok = true, data = ToTypeDto(def) };
    }

    private static object ToTypeDto(DefinitionNoeud d) => new
    {
        id = d.Id,
        nom = d.Nom,
        description = d.Description,
        espace = d.Espace.Id(),
        categorie = d.Categorie,
        ports_entree = d.PortsEntree.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
        ports_sortie = d.PortsSortie.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
        params_ = d.Params.Select(p => new {
            nom = p.Nom, libelle = p.Libelle, type = p.Type, defaut = p.Defaut,
            valeurs = p.Valeurs, indice = p.Indice, min = p.Min, max = p.Max,
        }),
    };

    // =============== GRAPHES ===============

    private object GrapheNouveau(JsonObject body)
    {
        var espace = Espace.Codage;
        if (body.TryGetPropertyValue("espace", out var e) && e is JsonValue ev)
            EspaceExtensions.TryParse(ev.GetValue<string>(), out espace);
        var nom = body["nom"]?.GetValue<string>() ?? "Sans nom";
        var g = new Graphe { Espace = espace, Nom = nom };
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { graphe_id = g.Id } };
    }

    private object GrapheDupliquer(JsonObject body)
    {
        var id = body["graphe_id"]?.GetValue<string>();
        if (id is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(id);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        g.Id = Guid.NewGuid().ToString("N");
        g.Nom += " (copie)";
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { graphe_id = g.Id } };
    }

    private object GrapheSupprimer(JsonObject body)
    {
        var id = body["graphe_id"]?.GetValue<string>();
        var espace = Espace.Codage;
        if (body.TryGetPropertyValue("espace", out var e) && e is JsonValue ev)
            EspaceExtensions.TryParse(ev.GetValue<string>(), out espace);
        if (id is null) return new { ok = false, error = "graphe_id manquant" };
        _persistance.SupprimerGraphe(id, espace);
        return new { ok = true, data = new { } };
    }

    private object GrapheRenommer(JsonObject body)
    {
        var id = body["graphe_id"]?.GetValue<string>();
        var nom = body["nom"]?.GetValue<string>();
        if (id is null || nom is null) return new { ok = false, error = "graphe_id/nom manquant" };
        var g = _persistance.ChargerGraphe(id);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        g.Nom = nom;
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { } };
    }

    private object GrapheLister()
    {
        var liste = _persistance.ListerGraphes().Select(t => new {
            id = t.id, espace = t.espace.Id(), nom = t.nom, modifie_le = t.modifieLe,
        }).ToList();
        return new { ok = true, data = new { graphes = liste } };
    }

    private object GrapheCharger(JsonObject body)
    {
        var id = body["graphe_id"]?.GetValue<string>();
        if (id is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(id);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        return new { ok = true, data = new {
            id = g.Id, espace = g.Espace.Id(), nom = g.Nom, modifie_le = g.ModifieLe,
            noeuds = g.Noeuds.Select(n => new {
                id = n.Id, type = n.Type, x = n.X, y = n.Y, params_ = n.Params,
                ports_entree = n.PortsEntree.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
                ports_sortie = n.PortsSortie.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
            }),
            liens = g.Liens.Select(l => new {
                id = l.Id, noeud_source_id = l.NoeudSourceId, port_source_nom = l.PortSourceNom,
                noeud_cible_id = l.NoeudCibleId, port_cible_nom = l.PortCibleNom,
            }),
        }};
    }

    private object GrapheExporter(JsonObject body)
    {
        var id = body["graphe_id"]?.GetValue<string>();
        var chemin = body["chemin"]?.GetValue<string>();
        if (id is null || chemin is null) return new { ok = false, error = "graphe_id/chemin manquant" };
        var g = _persistance.ChargerGraphe(id);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        _persistance.ExporterGraphe(g, chemin);
        return new { ok = true, data = new { chemin } };
    }

    private object GrapheImporter(JsonObject body)
    {
        var chemin = body["chemin"]?.GetValue<string>();
        if (chemin is null) return new { ok = false, error = "chemin manquant" };
        var g = _persistance.ImporterGraphe(chemin);
        if (g is null) return new { ok = false, error = "fichier introuvable" };
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { graphe_id = g.Id } };
    }

    // =============== NOEUDS ===============

    private object NoeudAjouter(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var type = body["type"]?.GetValue<string>();
        if (gid is null || type is null) return new { ok = false, error = "graphe_id/type manquant" };
        var def = CatalogueNoeuds.Trouver(type);
        if (def is null) return new { ok = false, error = "type inconnu: " + type };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        Historique.Pousser(g);
        var n = new Noeud
        {
            Type = type,
            X = body["x"]?.GetValue<double>() ?? 0,
            Y = body["y"]?.GetValue<double>() ?? 0,
            PortsEntree = def.PortsEntree.ToList(),
            PortsSortie = def.PortsSortie.ToList(),
        };
        if (body["params"] is JsonObject pp)
        {
            foreach (var kv in pp) n.Params[kv.Key] = kv.Value;
        }
        g.Noeuds.Add(n);
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { noeud_id = n.Id } };
    }

    private object NoeudSupprimer(JsonObject body)
    {
        var nid = body["noeud_id"]?.GetValue<string>();
        if (nid is null) return new { ok = false, error = "noeud_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverNoeud(nid) is not null)
            {
                Historique.Pousser(g);
                g.Noeuds.RemoveAll(n => n.Id == nid);
                g.Liens.RemoveAll(l => l.NoeudSourceId == nid || l.NoeudCibleId == nid);
                _persistance.SauvegarderGraphe(g);
                return new { ok = true, data = new { } };
            }
        }
        return new { ok = false, error = "noeud introuvable" };
    }

    private object NoeudDeplacer(JsonObject body)
    {
        var nid = body["noeud_id"]?.GetValue<string>();
        if (nid is null) return new { ok = false, error = "noeud_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverNoeud(nid) is { } n)
            {
                n.X = body["x"]?.GetValue<double>() ?? n.X;
                n.Y = body["y"]?.GetValue<double>() ?? n.Y;
                _persistance.SauvegarderGraphe(g);
                return new { ok = true, data = new { } };
            }
        }
        return new { ok = false, error = "noeud introuvable" };
    }

    private object NoeudModifier(JsonObject body)
    {
        var nid = body["noeud_id"]?.GetValue<string>();
        if (nid is null) return new { ok = false, error = "noeud_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverNoeud(nid) is { } n)
            {
                if (body["params"] is JsonObject pp)
                {
                    n.Params.Clear();
                    foreach (var kv in pp) n.Params[kv.Key] = kv.Value;
                }
                _persistance.SauvegarderGraphe(g);
                return new { ok = true, data = new { } };
            }
        }
        return new { ok = false, error = "noeud introuvable" };
    }

    private object NoeudDupliquer(JsonObject body)
    {
        var nid = body["noeud_id"]?.GetValue<string>();
        if (nid is null) return new { ok = false, error = "noeud_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverNoeud(nid) is { } n)
            {
                var clone = n.Clone();
                g.Noeuds.Add(clone);
                _persistance.SauvegarderGraphe(g);
                return new { ok = true, data = new { noeud_id = clone.Id } };
            }
        }
        return new { ok = false, error = "noeud introuvable" };
    }

    // =============== LIENS ===============

    private object LienCreer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (gid is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var srcId = body["port_source"]?["noeud_id"]?.GetValue<string>();
        var srcNom = body["port_source"]?["port_nom"]?.GetValue<string>();
        var cblId = body["port_cible"]?["noeud_id"]?.GetValue<string>();
        var cblNom = body["port_cible"]?["port_nom"]?.GetValue<string>();
        if (srcId is null || srcNom is null || cblId is null || cblNom is null)
            return new { ok = false, error = "port_source/port_cible incomplets" };
        var src = g.TrouverNoeud(srcId);
        var cbl = g.TrouverNoeud(cblId);
        if (src is null || cbl is null) return new { ok = false, error = "noeud source/cible introuvable" };
        var srcPort = src.PortsSortie.FirstOrDefault(p => p.Nom == srcNom);
        var cblPort = cbl.PortsEntree.FirstOrDefault(p => p.Nom == cblNom);
        if (srcPort is null || cblPort is null) return new { ok = false, error = "port introuvable" };
        if (!srcPort.Type.Compatible(cblPort.Type))
            return new { ok = false, error = $"types incompatibles: {srcPort.Type} -> {cblPort.Type}" };
        Historique.Pousser(g);
        var lien = new Lien
        {
            NoeudSourceId = srcId, PortSourceNom = srcNom,
            NoeudCibleId = cblId, PortCibleNom = cblNom,
        };
        g.Liens.Add(lien);
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { lien_id = lien.Id } };
    }

    private object LienSupprimer(JsonObject body)
    {
        var lid = body["lien_id"]?.GetValue<string>();
        if (lid is null) return new { ok = false, error = "lien_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverLien(lid) is not null)
            {
                Historique.Pousser(g);
                g.Liens.RemoveAll(l => l.Id == lid);
                _persistance.SauvegarderGraphe(g);
                return new { ok = true, data = new { } };
            }
        }
        return new { ok = false, error = "lien introuvable" };
    }

    // =============== EXECUTION ===============

    private object Executer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (gid is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var entree = new Dictionary<string, object?>();
        if (body["entree"] is JsonObject eobj)
        {
            foreach (var kv in eobj) entree[kv.Key] = kv.Value;
        }
        var exec = Moteur.Instance.LancerAsync(g, entree);
        return new { ok = true, data = new { execution_id = exec.ExecutionId } };
    }

    private object ExecuterNoeud(JsonObject body)
    {
        var nid = body["noeud_id"]?.GetValue<string>();
        if (nid is null) return new { ok = false, error = "noeud_id manquant" };
        foreach (var g in ChargerTous())
        {
            if (g.TrouverNoeud(nid) is { } n)
            {
                var entree = new Dictionary<string, object?>();
                if (body["entree"] is JsonObject eobj)
                    foreach (var kv in eobj) entree[kv.Key] = kv.Value;
                var exec = Moteur.Instance.ExecuterNoeudAsync(n, entree);
                return new { ok = true, data = new { execution_id = exec.ExecutionId } };
            }
        }
        return new { ok = false, error = "noeud introuvable" };
    }

    private object ExecutionEtat(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("execution_id", out var eid))
            return new { ok = false, error = "execution_id manquant" };
        var exec = Moteur.Instance.EtatExecution(eid);
        if (exec is null) return new { ok = false, error = "execution introuvable" };
        return new { ok = true, data = new {
            execution_id = exec.ExecutionId,
            graphe_id = exec.GrapheId,
            statut = exec.Statut.ToString(),
            noeuds = exec.Noeuds.Select(n => new {
                noeud_id = n.NoeudId, type = n.Type, statut = n.Statut.ToString(),
                erreur = n.Erreur, duree_ms = n.DureeMs,
                sorties = n.Sorties.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            }),
            sorties_finales = exec.SortiesFinales,
            erreur = exec.Erreur,
            demarree_le = exec.DemarreeLe, terminee_le = exec.TermineeLe,
        }};
    }

    private object ExecutionAnnuler(JsonObject body)
    {
        var eid = body["execution_id"]?.GetValue<string>();
        if (eid is null) return new { ok = false, error = "execution_id manquant" };
        Moteur.Instance.Annuler(eid);
        return new { ok = true, data = new { } };
    }

    // =============== UNDO/REDO ===============

    private object Annuler(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (gid is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        if (!Historique.Annuler(g)) return new { ok = false, error = "rien a annuler" };
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { peut_annuler = Historique.PeutAnnuler(gid), peut_refaire = Historique.PeutRefaire(gid) } };
    }

    private object Refaire(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (gid is null) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        if (!Historique.Refaire(g)) return new { ok = false, error = "rien a refaire" };
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { peut_annuler = Historique.PeutAnnuler(gid), peut_refaire = Historique.PeutRefaire(gid) } };
    }

    // =============== CUSTOM ===============

    private object CustomCreer(JsonObject body)
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        var nc = new NoeudCustom
        {
            Nom = body["nom"]?.GetValue<string>() ?? "Sans nom",
            Espace = body["espace"]?.GetValue<string>() ?? "codage",
            Description = body["description"]?.GetValue<string>() ?? "",
            Langage = body["langage"]?.GetValue<string>() ?? "python",
            Code = body["code"]?.GetValue<string>() ?? "",
        };
        if (body["ports"] is JsonObject ports)
        {
            foreach (var p in ports)
            {
                if (p.Value is not JsonObject po) continue;
                var isEntree = p.Key.StartsWith("in:", StringComparison.Ordinal);
                var nom = isEntree ? p.Key[3..] : (p.Key.StartsWith("out:") ? p.Key[4..] : p.Key);
                var type = po["type"]?.GetValue<string>() ?? "Texte";
                if (isEntree)
                    nc.PortsEntree.Add(new NoeudCustom.PortDto { Nom = nom, Type = type });
                else
                    nc.PortsSortie.Add(new NoeudCustom.PortDto { Nom = nom, Type = type });
            }
        }
        if (body["params"] is JsonArray pArr)
        {
            foreach (var p in pArr)
            {
                if (p is not JsonObject po) continue;
                nc.Params.Add(new NoeudCustom.ParamDto
                {
                    Nom = po["nom"]?.GetValue<string>() ?? "",
                    Libelle = po["libelle"]?.GetValue<string>() ?? "",
                    Type = po["type"]?.GetValue<string>() ?? "texte",
                    Defaut = po["defaut"],
                });
            }
        }
        var id = CatalogueCustom.Creer(nc, _persistance.Racine);
        return new { ok = true, data = new { type_id = id } };
    }

    private object CustomSupprimer(JsonObject body)
    {
        var id = body["type_id"]?.GetValue<string>();
        if (id is null) return new { ok = false, error = "type_id manquant" };
        CatalogueCustom.Supprimer(id, _persistance.Racine);
        return new { ok = true, data = new { } };
    }

    private object CustomLister()
    {
        var liste = CatalogueCustom.Tous.Select(c => new {
            id = c.Id, nom = c.Nom, espace = c.Espace, description = c.Description, langage = c.Langage,
        });
        return new { ok = true, data = new { custom = liste } };
    }

    // =============== Helpers ===============

    private IEnumerable<Graphe> ChargerTous()
    {
        foreach (var t in _persistance.ListerGraphes())
        {
            var g = _persistance.ChargerGraphe(t.id, t.espace);
            if (g is not null) yield return g;
        }
    }

    /// <summary>Liste tous les modeles installes (image/ et video/) avec leur manifeste.</summary>
    private object ModelesLister()
    {
        var racine = Path.Combine(AppContext.BaseDirectory, "Outils", "Modeles");
        var racineImg = Path.Combine(racine, "image");
        var racineVid = Path.Combine(racine, "video");
        var img = ServeurDiffusion.ChargerModeles(racineImg);
        var vid = ServeurDiffusion.ChargerModeles(racineVid);
        var liste = img.Select(m => new {
            id = m.Id, nom = m.Nom, espace = m.Produit, moteur = m.Moteur, vram_mo = m.VramMo,
            racine = m.Racine, fichier_diffusion = m.Fichiers.GetValueOrDefault("diffusion"),
        }).Concat(vid.Select(m => new {
            id = m.Id, nom = m.Nom, espace = m.Produit, moteur = m.Moteur, vram_mo = m.VramMo,
            racine = m.Racine, fichier_diffusion = m.Fichiers.GetValueOrDefault("diffusion"),
        }));
        return new { ok = true, data = new { modeles = liste } };
    }
}