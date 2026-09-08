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
using SenSÉ.Atelier.Langages;
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
    public Verbes(string racine) { _persistance = new Persistance(racine); Detecteur.Initialiser(racine); MoteurExecuteur.Initialiser(racine); }

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
                "planificateur/ajouter"     => PlanificateurAjouter(body!),
                "planificateur/lister"      => PlanificateurLister(),
                "planificateur/retirer"     => PlanificateurRetirer(body!),
                "versionning/creer"         => VersionningCreer(body!),
                "versionning/lister"        => VersionningLister(body!),
                "versionning/restaurer"     => VersionningRestaurer(body!),
                "versionning/comparer"      => VersionningComparer(body!),
                "layout/auto"               => LayoutAuto(body!),
                "groupe/creer"               => GroupeCreer(body!),
                "groupe/lister"              => GroupeLister(body!),
                "groupe/supprimer"           => GroupeSupprimer(body!),
                "commentaire/creer"          => CommentaireCreer(body!),
                "commentaire/lister"         => CommentaireLister(body!),
                "commentaire/supprimer"      => CommentaireSupprimer(body!),
                "langages"                => LangagesLister(),
                "etat"                     => ExecutionEtat(query),
                "fenetre/ouvrir"           => FenetreOuvrir(),
                "fenetre/etat"             => FenetreEtat(),
                "exemples/lister"           => ExemplesLister(),
                "catalogue/aide"           => CatalogueAide(),
                "exemples/charger"          => ExemplesCharger(body!),
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
        nom_vulgarise = d.NomAffichage,
        description = d.Description,
        description_longue = d.DescriptionAffichage,
        espace = d.Espace.Id(),
        categorie = d.Categorie,
        // Duree estimee : categorie + texte vulgarise pour le tooltip UI.
        // Sert a l'indicateur visuel (point colore) et au DTO HTTP.
        duree_estimee = d.DureeEstimeeEffective.ToString(),
        duree_vulgarisee = SenSÉ.Atelier.Bibliotheque.Vulgarisation.DureeTexte(d.DureeEstimeeEffective),
        ports_entree = d.PortsEntree.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
        ports_sortie = d.PortsSortie.Select(p => new { nom = p.Nom, type = p.Type.ToString() }),
        params_ = d.Params.Select(p => new {
            nom = p.Nom,
            libelle = p.Libelle,
            libelle_affiche = p.LibelleAffichage,
            tooltip = p.DescriptionAffichage,
            type = p.Type, defaut = p.Defaut,
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

    /// <summary>Liste les langages detectes sur la machine (executer_code).</summary>
    private object LangagesLister()
    {
        var liste = SenSÉ.Atelier.Langages.Detecteur.Moteurs.Select(m => new {
            id = m.Id, nom = m.Nom, rang = m.Rang, extensions = m.Extensions,
            latence_ms = m.LatenceMs, taille_mo = m.TailleMo, executable = m.Executable,
        });
        return new { ok = true, data = new { langages = liste } };
    }

    // =============== FENETRE (mode headless) ===============

    /// <summary>
    /// Affiche la fenetre WPF de l'Atelier si elle ne l'est pas deja.
    /// </summary>
    /// <remarks>
    /// En mode headless (<c>--no-window</c>), l'Atelier sert le HTTP sans
    /// fenetre visible. Ce verbe permet a l'Assistant ou a un outil tiers
    /// de demander l'affichage de la fenetre sans avoir a la creer. La
    /// fenetre est re-utilisee si elle existe deja, et son cycle de vie
    /// est gere par l'App WPF (cf. SenSÉ.Atelier.App.AfficherFenetre).
    /// </remarks>
    private object FenetreOuvrir()
    {
        if (System.Windows.Application.Current is not SenSÉ.Atelier.App app)
            return new { ok = false, error = "Atelier non WPF ou App non initialisee" };
        try
        {
            app.Dispatcher.Invoke(() => app.AfficherFenetre());
            return new { ok = true, data = new { visible = app.FenetreVisible } };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    /// <summary>Indique si l'Atelier tourne en headless et si sa fenetre est visible.</summary>
    private object FenetreEtat()
    {
        if (System.Windows.Application.Current is SenSÉ.Atelier.App app)
        {
            return new { ok = true, data = new {
                headless = app.NoWindow,
                fenetre_visible = app.FenetreVisible,
            }};
        }
        return new { ok = true, data = new { headless = false, fenetre_visible = false } };
    }

    // =============== EXEMPLES ===============

    /// <summary>
    /// Liste les exemples de graphes prets a l'emploi, charges depuis
    /// <c>Outils/Atelier/Exemples/index.json</c>. Chaque exemple est un
    /// fichier JSON qui suit le meme format qu'un graphe sauvegarde.
    /// </summary>
    private object ExemplesLister()
    {
        var dossier = System.IO.Path.Combine(_persistance.Racine, "Exemples");
        var indexPath = System.IO.Path.Combine(dossier, "index.json");
        if (!System.IO.File.Exists(indexPath))
            return new { ok = false, error = "index.json absent dans " + dossier };
        try
        {
            var json = System.IO.File.ReadAllText(indexPath);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
            if (node is null) return new { ok = false, error = "index.json vide ou invalide" };
            return new { ok = true, data = node };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    /// <summary>
    /// Charge un exemple par son id, cree un nouveau graphe avec un id frais,
    /// le sauvegarde sur disque, et retourne son id. Cote UI, l'exemple
    /// apparait comme un onglet pret a executer.
    /// </summary>
    private object ExemplesCharger(System.Text.Json.Nodes.JsonObject body)
    {
        var exempleId = body["exemple_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(exempleId))
            return new { ok = false, error = "exemple_id manquant" };

        var dossier = System.IO.Path.Combine(_persistance.Racine, "Exemples");
        var indexPath = System.IO.Path.Combine(dossier, "index.json");
        if (!System.IO.File.Exists(indexPath))
            return new { ok = false, error = "index.json absent dans " + dossier };

        try
        {
            var indexNode = System.Text.Json.Nodes.JsonNode.Parse(
                System.IO.File.ReadAllText(indexPath))?.AsObject();
            var exemples = indexNode?["exemples"] as System.Text.Json.Nodes.JsonArray;
            if (exemples is null) return new { ok = false, error = "index.exemples invalide" };

            string? fichier = null;
            string? nomAffiche = null;
            string espace = "codage";
            foreach (var item in exemples)
            {
                if (item is not System.Text.Json.Nodes.JsonObject jo) continue;
                if (jo["id"]?.GetValue<string>() == exempleId)
                {
                    fichier = jo["fichier"]?.GetValue<string>();
                    nomAffiche = jo["titre"]?.GetValue<string>();
                    if (jo["espace"]?.GetValue<string>() is { } esp) espace = esp;
                    break;
                }
            }
            if (fichier is null)
                return new { ok = false, error = "exemple inconnu: " + exempleId };

            var chemin = System.IO.Path.Combine(dossier, fichier);
            if (!System.IO.File.Exists(chemin))
                return new { ok = false, error = "fichier exemple introuvable: " + fichier };

            // Charge, force un nouvel id, sauvegarde comme nouveau graphe.
            var nouveau = _persistance.ImporterGraphe(chemin);
            if (nouveau is null)
                return new { ok = false, error = "importation du fichier exemple a echoue" };
            // Re-applique le titre affiche (le fichier peut avoir un autre nom).
            if (nomAffiche is not null) nouveau.Nom = nomAffiche;
            _persistance.SauvegarderGraphe(nouveau);
            return new { ok = true, data = new { graphe_id = nouveau.Id, nom = nouveau.Nom, espace = nouveau.Espace.Id() } };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    /// <summary>
    /// Retourne un resume en Markdown de tous les types de noeuds : id,
    /// nom, description, categorie, ports, params. Sert a l'Assistant pour
    /// decouvrir ce que l'Atelier sait faire, et genere Outils/Atelier/README.md
    /// au premier demarrage si le fichier est absent.
    /// </summary>
    private object CatalogueAide()
    {
        CatalogueNoeuds.InitialiserSiNecessaire();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Catalogue Atelier SenSÉ");
        sb.AppendLine();
        sb.AppendLine("> Généré automatiquement au démarrage. Pour forcer la regénération, supprimer ce fichier et relancer l'Atelier.");
        sb.AppendLine();
        foreach (Espace esp in System.Enum.GetValues<Espace>())
        {
            sb.AppendLine("## Espace : " + esp.Libelle() + " (`" + esp.Id() + "`)");
            sb.AppendLine();
            var types = CatalogueNoeuds.ParEspace(esp).OrderBy(t => t.Categorie).ThenBy(t => t.Nom);
            foreach (var t in types)
            {
                // Nom vulgarise (via Vulgarisation.LookupNoeud) avec id technique en sub.
                sb.AppendLine("### " + t.NomAffichage + " (`" + t.Id + "`)");
                if (!string.IsNullOrEmpty(t.DescriptionAffichage))
                {
                    sb.AppendLine();
                    sb.AppendLine(t.DescriptionAffichage);
                }
                sb.AppendLine();
                sb.AppendLine("- **Catégorie** : " + t.Categorie);
                if (t.PortsEntree.Count > 0)
                {
                    sb.AppendLine("- **Entrées** : " + string.Join(", ", t.PortsEntree.Select(p => "`" + p.Nom + "` (" + p.Type + ")")));
                } else {
                    sb.AppendLine("- **Entrées** : aucune");
                }
                if (t.PortsSortie.Count > 0)
                {
                    sb.AppendLine("- **Sorties** : " + string.Join(", ", t.PortsSortie.Select(p => "`" + p.Nom + "` (" + p.Type + ")")));
                } else {
                    sb.AppendLine("- **Sorties** : aucune");
                }
                if (t.Params.Count > 0)
                {
                    sb.AppendLine("- **Paramètres** :");
                    foreach (var p in t.Params)
                    {
                        // Libelle vulgarise (via Vulgarisation.LookupParametre) en titre,
                        // nom technique entre parentheses pour les devs.
                        var ligne = "  - **" + p.LibelleAffichage + "** (`" + p.Nom + "`, " + p.Type + ")";
                        if (p.Defaut is not null) ligne += ", défaut `" + p.Defaut + "`";
                        if (p.Valeurs is { Count: > 0 }) ligne += " ∈ {" + string.Join(", ", p.Valeurs.Select(v => "`" + v + "`")) + "}";
                        sb.AppendLine(ligne);
                    }
                }
                if (!string.IsNullOrEmpty(t.ModeleId))
                {
                    sb.AppendLine("- **Modèle** : `" + t.ModeleId + "`");
                }
                sb.AppendLine();
            }
        }
        return new { ok = true, data = new { markdown = sb.ToString() } };
    }
    // =============== PLANIFICATEUR ===============

    private object PlanificateurAjouter(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var cron = body["cron"]?.GetValue<string>() ?? "0 9 * * *";
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        if (!SenSÉ.Atelier.Planificateur.Planificateur.Instance.Ajouter(gid, cron, out var prochain, out var err))
            return new { ok = false, error = err };
        return new { ok = true, data = new { graphe_id = gid, cron, prochain = prochain.ToString("o") } };
    }

    private object PlanificateurLister()
    {
        var taches = SenSÉ.Atelier.Planificateur.Planificateur.Instance.Lister();
        return new { ok = true, data = new { taches = taches.Select(t => new { graphe_id = t.GrapheId, cron = t.Cron, dernierdeclenchement = t.DernierDeclenchement?.ToString("o") }).ToList() } };
    }

    private object PlanificateurRetirer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var ok = SenSÉ.Atelier.Planificateur.Planificateur.Instance.Retirer(gid);
        return new { ok, data = new { graphe_id = gid } };
    }

    // =============== VERSIONNING ===============

    private object VersionningCreer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var msg = body["message"]?.GetValue<string>() ?? "Auto-save";
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var v = new SenSÉ.Atelier.Versionning.Versionneur(Racine, _persistance);
        var vid = v.Versionner(gid, msg);
        return new { ok = true, data = new { version_id = vid } };
    }

    private object VersionningLister(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var v = new SenSÉ.Atelier.Versionning.Versionneur(Racine, _persistance);
        var list = v.Lister(gid);
        return new { ok = true, data = new { versions = list.Select(x => new { version_id = x.VersionId, message = x.Message, created_at = x.CreatedAt.ToString("o"), nb_noeuds = x.NbNoeuds, nb_liens = x.NbLiens }).ToList() } };
    }

    private object VersionningRestaurer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var vid = body["version_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(vid)) return new { ok = false, error = "graphe_id et version_id requis" };
        var v = new SenSÉ.Atelier.Versionning.Versionneur(Racine, _persistance);
        if (!v.Restaurer(gid, vid, out var err)) return new { ok = false, error = err };
        return new { ok = true, data = new { version_id = vid } };
    }

    private object VersionningComparer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var va = body["version_a"]?.GetValue<string>();
        var vb = body["version_b"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(va) || string.IsNullOrEmpty(vb)) return new { ok = false, error = "graphe_id, version_a, version_b requis" };
        var v = new SenSÉ.Atelier.Versionning.Versionneur(Racine, _persistance);
        return v.Comparer(gid, va, vb);
    }

    private object LayoutAuto(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var sens = body["sens"]?.GetValue<string>() ?? "horizontal";
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable : " + gid };
        var s2 = sens == "vertical" ? SenSÉ.Atelier.CanvasLayout.AutoLayout.Sens.Vertical : SenSÉ.Atelier.CanvasLayout.AutoLayout.Sens.Horizontal;
        var nb = SenSÉ.Atelier.CanvasLayout.AutoLayout.Appliquer(g, s2);
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { nb_deplaces = nb, sens } };
    }

    // =============== GROUPES ===============

    private object GroupeCreer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var couleur = body["couleur"]?.GetValue<string>() ?? "blue";
        var label = body["label"]?.GetValue<string>();
        var ids = body["noeud_ids"] as JsonArray;
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        if (ids is null || ids.Count == 0) return new { ok = false, error = "noeud_ids manquant ou vide" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var grp = new SenSÉ.Atelier.Modele.Groupe { Couleur = couleur, Label = label };
        foreach (var n in ids) if (n is JsonValue jv) grp.NoeudIds.Add(jv.GetValue<string>() ?? "");
        g.Groupes.Add(grp);
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { groupe_id = grp.Id } };
    }

    private object GroupeLister(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        return new { ok = true, data = new { groupes = g.Groupes.Select(x => new { id = x.Id, couleur = x.Couleur, label = x.Label, noeud_ids = x.NoeudIds }).ToList() } };
    }

    private object GroupeSupprimer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var gpid = body["groupe_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(gpid)) return new { ok = false, error = "graphe_id et groupe_id requis" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var ok = g.Groupes.RemoveAll(x => x.Id == gpid) > 0;
        if (ok) _persistance.SauvegarderGraphe(g);
        return new { ok, data = new { groupe_id = gpid } };
    }

    // =============== COMMENTAIRES ===============

    private object CommentaireCreer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var texte = body["texte"]?.GetValue<string>() ?? "Commentaire";
        var x = body["x"]?.GetValue<double>() ?? 100;
        var y = body["y"]?.GetValue<double>() ?? 100;
        var taille = body["taille"]?.GetValue<int>() ?? 12;
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var c = new SenSÉ.Atelier.Modele.Commentaire { Texte = texte, X = x, Y = y, Taille = taille };
        g.Commentaires.Add(c);
        _persistance.SauvegarderGraphe(g);
        return new { ok = true, data = new { commentaire_id = c.Id } };
    }

    private object CommentaireLister(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid)) return new { ok = false, error = "graphe_id manquant" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        return new { ok = true, data = new { commentaires = g.Commentaires.Select(c => new { id = c.Id, texte = c.Texte, x = c.X, y = c.Y, taille = c.Taille, couleur = c.Couleur }).ToList() } };
    }

    private object CommentaireSupprimer(JsonObject body)
    {
        var gid = body["graphe_id"]?.GetValue<string>();
        var cid = body["commentaire_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(gid) || string.IsNullOrEmpty(cid)) return new { ok = false, error = "graphe_id et commentaire_id requis" };
        var g = _persistance.ChargerGraphe(gid);
        if (g is null) return new { ok = false, error = "graphe introuvable" };
        var ok = g.Commentaires.RemoveAll(c => c.Id == cid) > 0;
        if (ok) _persistance.SauvegarderGraphe(g);
        return new { ok, data = new { commentaire_id = cid } };
    }

}