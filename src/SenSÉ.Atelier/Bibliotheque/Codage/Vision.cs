using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Mcp;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 4 noeuds vision delegues a pc-agent (YOLOX-Nano + YuNet + ByteTrack).
/// Meme contrat que les MCPs Saisie/CDP : un ClientVision HttpClient partage,
/// timeouts respectent Annulation, erreurs remontees telles quelles.
/// </summary>
/// <remarks>
/// <b>Backend :</b> pc-agent sur <c>PC_AGENT_URL</c> (defaut <c>http://127.0.0.1:8765</c>).
/// Il faut que pc-agent soit lance separement — l'Atelier ne le demarre pas.
///
/// <para><b>Licences modeles (toutes compatibles SenSE MIT) :</b>
/// YOLOX-Nano (Apache 2.0, 4 Mo), YuNet (MIT, 1 Mo), ByteTrack (MIT, algo pur).</para>
/// </remarks>
public static class Vision
{
    public static void Enregistrer()
    {
        EnregistrerDetecterFrame();
        EnregistrerCompterAnimaux();
        EnregistrerSurveillerZone();
        EnregistrerTrackerVideo();
    }

    // ----------------------------------------------------------------------------
    // vision_detecter_frame
    // ----------------------------------------------------------------------------
    private static void EnregistrerDetecterFrame()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "vision_detecter_frame", "Vision : Détecter frame",
            "Detecte tous les objets d'une image via YOLOX-Nano (80 classes COCO, dont animaux, personnes, vehicules). Delivre a pc-agent, recoit les boites.",
            Espace.Codage, "Vision (détection)",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port>
            {
                new("detections", TypePort.Liste, false),
                new("nb", TypePort.Nombre, false),
                new("ms", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("detecter_visages", "Detecter aussi les visages (YuNet sur les personnes)", "booleen", false),
                new("filtre_classes", "Filtre classes (CSV, vide = toutes). Ex: cat,dog,bird", "texte", ""),
                new("confiance_min", "Confiance minimum (0.0-1.0)", "nombre", 0.30),
            },
            async ctx =>
            {
                var image = ctx.Entree("image");
                if (string.IsNullOrEmpty(image)) return ResultatExecution.Fail("image: chemin vide");
                if (!File.Exists(image)) return ResultatExecution.Fail($"image introuvable : {image}");
                try
                {
                    var cli = new ClientVision();
                    var filtre = ParseFiltre(ctx.Ch("filtre_classes"));
                    var conf = ctx.ChDouble("confiance_min", 0.30);
                    var res = await cli.DetecterFrameAsync(
                        image,
                        detecterVisages: ctx.ChBool("detecter_visages", false),
                        filtreClasses: filtre,
                        confianceMin: conf,
                        ct: ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["detections"] = res.Detections.Select(DetectionVersDict).ToList(),
                        ["nb"] = res.Detections.Count,
                        ["ms"] = res.ElapsedMs,
                    });
                }
                catch (Exception ex) { return ResultatExecution.Fail("pc-agent: " + ex.Message); }
            }
        ));
    }

    // ----------------------------------------------------------------------------
    // vision_compter_animaux
    // ----------------------------------------------------------------------------
    private static void EnregistrerCompterAnimaux()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "vision_compter_animaux", "Vision : Compter animaux",
            "Compte les animaux detectes dans une image. Espece optionnelle (cat, dog, bird, horse, sheep, cow, elephant, bear, zebra, giraffe). Vide = tous.",
            Espace.Codage, "Vision (détection)",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port>
            {
                new("nombre", TypePort.Nombre, false),
                new("positions", TypePort.Liste, false),
                new("especes", TypePort.Liste, false),
            },
            new List<ParametreNoeud>
            {
                new("espece", "Espece (vide = tous, sinon: cat/dog/bird/horse/sheep/cow/elephant/bear/zebra/giraffe)", "texte", ""),
                new("confiance_min", "Confiance minimum", "nombre", 0.30),
            },
            async ctx =>
            {
                var image = ctx.Entree("image");
                if (string.IsNullOrEmpty(image)) return ResultatExecution.Fail("image: chemin vide");
                if (!File.Exists(image)) return ResultatExecution.Fail($"image introuvable : {image}");
                var espece = ctx.Ch("espece").Trim();
                // Construit le filtre : soit l'espece precise, soit la liste des 10 animaux COCO.
                List<string>? filtre = string.IsNullOrEmpty(espece)
                    ? new List<string> { "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe" }
                    : new List<string> { espece };
                try
                {
                    var cli = new ClientVision();
                    var res = await cli.DetecterFrameAsync(
                        image,
                        detecterVisages: false,
                        filtreClasses: filtre,
                        confianceMin: ctx.ChDouble("confiance_min", 0.30),
                        ct: ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["nombre"] = res.Detections.Count,
                        ["positions"] = res.Detections.Select(d => new Dictionary<string, object?>
                        {
                            ["espece"] = d.ClassName,
                            ["score"] = d.Score,
                            ["x"] = d.X, ["y"] = d.Y, ["w"] = d.Width, ["h"] = d.Height,
                        }).ToList(),
                        ["especes"] = res.Detections.Select(d => d.ClassName).Distinct().ToList(),
                    });
                }
                catch (Exception ex) { return ResultatExecution.Fail("pc-agent: " + ex.Message); }
            }
        ));
    }

    // ----------------------------------------------------------------------------
    // vision_surveiller_zone
    // ----------------------------------------------------------------------------
    private static void EnregistrerSurveillerZone()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "vision_surveiller_zone", "Vision : Surveiller zone",
            "Surveille une zone rectangulaire d'une image. Renvoie present=true et la liste des detections si au moins un objet de la classe demandee est dans la zone.",
            Espace.Codage, "Vision (détection)",
            new List<Port>
            {
                new("image", TypePort.Image, true),
                new("zone", TypePort.Json, true), // {x,y,w,h}
            },
            new List<Port>
            {
                new("present", TypePort.Booleen, false),
                new("objets", TypePort.Liste, false),
            },
            new List<ParametreNoeud>
            {
                new("classe", "Classe a surveiller (vide = toute classe)", "texte", ""),
                new("confiance_min", "Confiance minimum", "nombre", 0.30),
            },
            async ctx =>
            {
                var image = ctx.Entree("image");
                if (string.IsNullOrEmpty(image)) return ResultatExecution.Fail("image: chemin vide");
                if (!File.Exists(image)) return ResultatExecution.Fail($"image introuvable : {image}");
                var zoneJson = ctx.Entree("zone");
                if (string.IsNullOrEmpty(zoneJson)) return ResultatExecution.Fail("zone: JSON vide (attendu {x,y,w,h})");
                (float x, float y, float w, float h)? zone = null;
                try { zone = ParseZone(zoneJson); }
                catch (Exception ex) { return ResultatExecution.Fail("zone invalide : " + ex.Message); }
                if (zone is null) return ResultatExecution.Fail("zone: parsing a renvoye null");
                var classe = ctx.Ch("classe").Trim();
                List<string>? filtre = string.IsNullOrEmpty(classe) ? null : new List<string> { classe };
                try
                {
                    var cli = new ClientVision();
                    var res = await cli.DetecterFrameAsync(
                        image,
                        detecterVisages: false,
                        filtreClasses: filtre,
                        confianceMin: ctx.ChDouble("confiance_min", 0.30),
                        ct: ctx.Annulation);
                    var (zx, zy, zw, zh) = zone.Value;
                    var zx2 = zx + zw; var zy2 = zy + zh;
                    // Une detection est "dans la zone" si sa box chevauche significativement.
                    var dansZone = res.Detections.Where(d =>
                    {
                        var dx2 = d.X + d.Width; var dy2 = d.Y + d.Height;
                        var ix0 = MathF.Max(d.X, zx); var iy0 = MathF.Max(d.Y, zy);
                        var ix1 = MathF.Min(dx2, zx2); var iy1 = MathF.Min(dy2, zy2);
                        var iw = MathF.Max(0f, ix1 - ix0); var ih = MathF.Max(0f, iy1 - iy0);
                        var inter = iw * ih;
                        var aireDet = d.Width * d.Height;
                        return aireDet > 0 && inter / aireDet > 0.3; // 30% de la box dans la zone
                    }).ToList();
                    return ResultatExecution.Ok(new()
                    {
                        ["present"] = dansZone.Count > 0,
                        ["objets"] = dansZone.Select(d => new Dictionary<string, object?>
                        {
                            ["classe"] = d.ClassName,
                            ["score"] = d.Score,
                            ["x"] = d.X, ["y"] = d.Y, ["w"] = d.Width, ["h"] = d.Height,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return ResultatExecution.Fail("pc-agent: " + ex.Message); }
            }
        ));
    }

    // ----------------------------------------------------------------------------
    // vision_tracker_video
    // ----------------------------------------------------------------------------
    private static void EnregistrerTrackerVideo()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "vision_tracker_video", "Vision : Tracker vidéo",
            "Envoie une sequence de frames a pc-agent qui execute YOLOX-Nano + ByteTrack. Renvoie la liste d'evenements (apparitions, continuations, disparitions) avec track_id stable.",
            Espace.Codage, "Vision (détection)",
            new List<Port> { new("frames", TypePort.Liste, true) },
            new List<Port>
            {
                new("evenements", TypePort.Liste, false),
                new("nb_tracks", TypePort.Nombre, false),
                new("ms", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("filtre_classes", "Filtre classes (CSV, vide = toutes)", "texte", ""),
                new("confiance_min", "Confiance minimum", "nombre", 0.30),
                new("max_frames", "Limite dure (defaut 600 = 1 min a 10 fps)", "nombre", 600),
            },
            async ctx =>
            {
                var raw = ctx.Entrees.TryGetValue("frames", out var v) ? v : null;
                if (raw is null) return ResultatExecution.Fail("frames: liste vide");
                List<string> chemins;
                if (raw is System.Collections.IEnumerable e) chemins = e.Cast<object?>().Select(o => o?.ToString() ?? "").ToList();
                else if (raw is string s) chemins = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                else return ResultatExecution.Fail("frames: type invalide (attendu liste de chemins)");
                if (chemins.Count == 0) return ResultatExecution.Fail("frames: liste vide");
                foreach (var p in chemins)
                    if (!string.IsNullOrEmpty(p) && !File.Exists(p))
                        return ResultatExecution.Fail($"frame introuvable : {p}");
                var filtre = ParseFiltre(ctx.Ch("filtre_classes"));
                var max = ctx.ChInt("max_frames", 600);
                try
                {
                    var cli = new ClientVision();
                    var res = await cli.TrackerVideoAsync(
                        chemins,
                        detecterVisages: false,
                        filtreClasses: filtre,
                        confianceMin: ctx.ChDouble("confiance_min", 0.30),
                        maxFrames: max,
                        ct: ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["evenements"] = res.Events.Select(e => new Dictionary<string, object?>
                        {
                            ["frame"] = e.FrameIdx,
                            ["track_id"] = e.TrackId,
                            ["classe"] = e.ClassName,
                            ["kind"] = e.Kind,
                            ["score"] = 0f, // pc-agent n'expose pas le score dans les events
                            ["x"] = e.X, ["y"] = e.Y, ["w"] = e.Width, ["h"] = e.Height,
                        }).ToList(),
                        ["nb_tracks"] = res.TotalTracks,
                        ["ms"] = res.ElapsedTotalMs,
                    });
                }
                catch (Exception ex) { return ResultatExecution.Fail("pc-agent: " + ex.Message); }
            }
        ));
    }

    // ----------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------

    /// <summary>Convertit un Detection (DTO) en dict sérialisable pour le port de sortie.</summary>
    private static Dictionary<string, object?> DetectionVersDict(Detection d) => new()
    {
        ["classe_id"] = d.ClassId,
        ["classe"] = d.ClassName,
        ["score"] = d.Score,
        ["x"] = d.X, ["y"] = d.Y, ["w"] = d.Width, ["h"] = d.Height,
    };

    /// <summary>Parse un CSV "cat, dog ,bird" en liste propre.</summary>
    private static List<string>? ParseFiltre(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Parse "{x,y,w,h}" en tuple. Accepte aussi "x,y,w,h" sans accolades.</summary>
    private static (float, float, float, float) ParseZone(string json)
    {
        var s = json.Trim().Trim('{', '}');
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) throw new FormatException("attendu {x,y,w,h}");
        return (
            float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
    }
}
