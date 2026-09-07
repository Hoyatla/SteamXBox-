using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SenSÉ.Atelier.Diffusion;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.ModeleLocal;

namespace SenSÉ.Atelier.Bibliotheque.Multimedia;

/// <summary>
/// Les 10 noeuds multimedia. Les 4 qui touchent au GPU passent par
/// <see cref="ServeurDiffusion"/> ; les 6 restants (TTS, ffmpeg, LLM vision)
/// restent locaux.
/// </summary>
public static class Tous
{
    /// <summary>Chemin racine des manifestes modele.json. Partage par les 4 noeuds GPU.</summary>
    private static string ModelesRacine => Path.Combine(AppContext.BaseDirectory, "Outils", "Modeles");

    public static void Enregistrer()
    {
        EnregistrerTexteVersImage();
        EnregistrerTexteVersVideo();
        EnregistrerImageVersVideo();
        EnregistrerChargerModele();
        EnregistrerTexteVersSon();
        EnregistrerAudioVersTexte();
        EnregistrerImageVersTexte();
        EnregistrerExtraireFrames();
        EnregistrerFusionnerVideos();
        EnregistrerDecouperVideo();
        EnregistrerRedimensionnerImage();
    }

    private static void EnregistrerTexteVersImage()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_image", "Texte vers Image", "Genere une image via Flux schnell (sd-server).",
            Espace.Multimedia, "Generation",
            new List<Port> { new("prompt", TypePort.Texte, true) },
            new List<Port> { new("image", TypePort.Image, false) },
            new List<ParametreNoeud>
            {
                new("modele", "Modele", "texte", "flux-schnell"),
                new("largeur", "Largeur (px)", "nombre", 1024),
                new("hauteur", "Hauteur (px)", "nombre", 1024),
                new("steps", "Steps", "nombre", 4),
                new("cfg", "CFG scale", "nombre", 1.0),
                new("seed", "Seed (-1 = aleatoire)", "nombre", -1L),
            },
            async ctx =>
            {
                try
                {
                    var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
                    if (string.IsNullOrEmpty(prompt)) return ResultatExecution.Fail("prompt vide");
                    var modeleId = ctx.Ch("modele");
                    var spec = TrouverModele(modeleId) ?? TrouverPremierModele("image")
                        ?? throw new Exception("aucun modele image installe dans Outils/Modeles/image/");
                    var serveur = new ServeurDiffusion();
                    var demarrage = serveur.Demarrer(spec, msg => ctx.Journal?.Invoke(msg));
                    if (demarrage is not null) return ResultatExecution.Fail(demarrage);

                    var path = await ServeurDiffusion.GenererImageAsync(
                        prompt, negatif: null,
                        largeur: ctx.ChInt("largeur", 1024),
                        hauteur: ctx.ChInt("hauteur", 1024),
                        steps: ctx.ChInt("steps", 4),
                        cfg: ctx.ChDouble("cfg", 1.0),
                        seed: ctx.ChInt("seed", -1));

                    // Verifie la sortie SUR LE DISQUE, pas dans le message du binaire
                    if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                        return ResultatExecution.Fail("sortie invalide : " + path);
                    return ResultatExecution.Ok(new() { ["image"] = path });
                }
                catch (Exception ex) { return ResultatExecution.Fail("sd-server: " + ex.Message); }
            },
            ModeleId: "flux-schnell"
        ));
    }

    private static void EnregistrerTexteVersVideo()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_video", "Texte vers Vidéo", "Genere une video via Wan 2.2 T2V (sd-server).",
            Espace.Multimedia, "Generation",
            new List<Port> { new("prompt", TypePort.Texte, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>
            {
                new("modele", "Modele", "texte", "wan22-t2v"),
                new("largeur", "Largeur (px)", "nombre", 832),
                new("hauteur", "Hauteur (px)", "nombre", 480),
                new("frames", "Frames", "nombre", 16),
                new("steps", "Steps", "nombre", 20),
                new("cfg", "CFG scale", "nombre", 7.0),
                new("seed", "Seed (-1 = aleatoire)", "nombre", -1L),
            },
            async ctx =>
            {
                try
                {
                    var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
                    if (string.IsNullOrEmpty(prompt)) return ResultatExecution.Fail("prompt vide");
                    var modeleId = ctx.Ch("modele");
                    var spec = TrouverModele(modeleId) ?? TrouverPremierModele("video")
                        ?? throw new Exception("aucun modele video installe dans Outils/Modeles/video/");
                    var serveur = new ServeurDiffusion();
                    var demarrage = serveur.Demarrer(spec, msg => ctx.Journal?.Invoke(msg));
                    if (demarrage is not null) return ResultatExecution.Fail(demarrage);

                    var path = await ServeurDiffusion.GenererVideoAsync(
                        prompt, imageInitiale: null,
                        frames: ctx.ChInt("frames", 16),
                        largeur: ctx.ChInt("largeur", 832),
                        hauteur: ctx.ChInt("hauteur", 480),
                        steps: ctx.ChInt("steps", 20),
                        cfg: ctx.ChDouble("cfg", 7.0),
                        seed: ctx.ChInt("seed", -1));

                    if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                        return ResultatExecution.Fail("sortie invalide : " + path);
                    return ResultatExecution.Ok(new() { ["video"] = path });
                }
                catch (Exception ex) { return ResultatExecution.Fail("sd-server: " + ex.Message); }
            },
            ModeleId: "wan22-t2v"
        ));
    }

    private static void EnregistrerImageVersVideo()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "image_vers_video", "Image vers Vidéo", "Anime une image via Wan 2.2 I2V (sd-server).",
            Espace.Multimedia, "Generation",
            new List<Port>
            {
                new("image", TypePort.Image, true),
                new("prompt", TypePort.Texte, true),
            },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>
            {
                new("modele", "Modele", "texte", "wan22-i2v"),
                new("largeur", "Largeur (px)", "nombre", 832),
                new("hauteur", "Hauteur (px)", "nombre", 480),
                new("frames", "Frames", "nombre", 16),
                new("steps", "Steps", "nombre", 20),
                new("cfg", "CFG scale", "nombre", 7.0),
                new("seed", "Seed (-1 = aleatoire)", "nombre", -1L),
            },
            async ctx =>
            {
                try
                {
                    var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
                    var image = ctx.Entree("image") ?? ctx.Ch("image");
                    if (string.IsNullOrEmpty(prompt)) return ResultatExecution.Fail("prompt vide");
                    if (string.IsNullOrEmpty(image) || !File.Exists(image))
                        return ResultatExecution.Fail("image introuvable : " + image);
                    var modeleId = ctx.Ch("modele");
                    var spec = TrouverModele(modeleId) ?? TrouverPremierModeleI2V()
                        ?? throw new Exception("aucun modele I2V installe dans Outils/Modeles/video/");
                    var serveur = new ServeurDiffusion();
                    var demarrage = serveur.Demarrer(spec, msg => ctx.Journal?.Invoke(msg));
                    if (demarrage is not null) return ResultatExecution.Fail(demarrage);

                    var path = await ServeurDiffusion.GenererVideoAsync(
                        prompt, imageInitiale: image,
                        frames: ctx.ChInt("frames", 16),
                        largeur: ctx.ChInt("largeur", 832),
                        hauteur: ctx.ChInt("hauteur", 480),
                        steps: ctx.ChInt("steps", 20),
                        cfg: ctx.ChDouble("cfg", 7.0),
                        seed: ctx.ChInt("seed", -1));

                    if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                        return ResultatExecution.Fail("sortie invalide : " + path);
                    return ResultatExecution.Ok(new() { ["video"] = path });
                }
                catch (Exception ex) { return ResultatExecution.Fail("sd-server: " + ex.Message); }
            },
            ModeleId: "wan22-i2v"
        ));
    }

    private static void EnregistrerChargerModele()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "charger_modele", "Charger modele", "Force le pre-chargement d'un modele (utile en debut de graphe).",
            Espace.Multimedia, "Generation",
            new List<Port>(),
            new List<Port> { new("ok", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("modele", "Modele (id)", "texte", "flux-schnell"),
            },
            async ctx =>
            {
                try
                {
                    var modeleId = ctx.Ch("modele");
                    var spec = TrouverModele(modeleId) ?? throw new Exception("modele introuvable : " + modeleId);
                    var serveur = new ServeurDiffusion();
                    var demarrage = serveur.Demarrer(spec, msg => ctx.Journal?.Invoke(msg));
                    if (demarrage is not null) return ResultatExecution.Fail(demarrage);
                    return ResultatExecution.Ok(new() { ["ok"] = spec.Id });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            },
            ModeleId: "__charger__"
        ));
    }

    private static void EnregistrerTexteVersSon()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_son", "Texte vers Son", "Synthese vocale SAPI locale.",
            Espace.Multimedia, "Generation",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("audio", TypePort.Audio, false) },
            new List<ParametreNoeud> { new("voix", "Voix", "texte", "fr-FR") },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? ctx.Ch("texte");
                if (string.IsNullOrEmpty(texte)) return ResultatExecution.Fail("texte vide");
                var outPath = Path.Combine(Path.GetTempPath(), "atelier_tts_" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    var ps = "Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.SetOutputToWaveFile('" + outPath + "'); $s.Speak('" + texte.Replace("'", "''") + "'); $s.Dispose();";
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell", "-NoProfile -Command \"" + ps.Replace("\"", "\\\"") + "\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit(60_000);
                    return ResultatExecution.Ok(new() { ["audio"] = outPath });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static void EnregistrerAudioVersTexte()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "audio_vers_texte", "Audio vers Texte", "Transcription audio via whisper.cpp.",
            Espace.Multimedia, "Generation",
            new List<Port> { new("audio", TypePort.Audio, true) },
            new List<Port> { new("transcription", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            async ctx => ResultatExecution.Fail("whisper non disponible - installe llama.cpp ou un autre ASR")
        ));
    }

    private static void EnregistrerImageVersTexte()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "image_vers_texte", "Image vers Texte", "Description d'une image par le LLM multimodal.",
            Espace.Multimedia, "Vision",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port> { new("description", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("question", "Question (vide=description)", "texte", "") },
            async ctx =>
            {
                try
                {
                    var img = ctx.Entree("image") ?? ctx.Ch("image");
                    var q = ctx.Ch("question");
                    if (string.IsNullOrEmpty(img) || !File.Exists(img)) return ResultatExecution.Fail("image introuvable");
                    var bytes = File.ReadAllBytes(img);
                    var b64 = Convert.ToBase64String(bytes);
                    var prompt = string.IsNullOrEmpty(q) ? "Decris cette image en francais, de maniere detaillee." : q;
                    var full = "[IMAGE:b64:" + b64 + "]\n\n" + prompt;
                    var res = await new ClientModele().CompleterAsync(full, 1024);
                    return ResultatExecution.Ok(new() { ["description"] = res });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static void EnregistrerExtraireFrames()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "extraire_frames", "Extraire frames", "Extrait les frames d'une video a N fps (ffmpeg).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("video", TypePort.Video, true) },
            new List<Port> { new("frames", TypePort.Liste, false) },
            new List<ParametreNoeud> { new("fps", "FPS", "nombre", 1.0) },
            async ctx =>
            {
                var v = ctx.Entree("video") ?? ctx.Ch("video");
                var fps = ctx.ChDouble("fps", 1.0);
                if (string.IsNullOrEmpty(v) || !File.Exists(v)) return ResultatExecution.Fail("video introuvable");
                var outDir = Path.Combine(Path.GetTempPath(), "atelier_frames_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(outDir);
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                        "-y -i \"" + v + "\" -vf fps=" + fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \"" + outDir + "/frame_%04d.png\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit();
                    var files = Directory.GetFiles(outDir, "frame_*.png");
                    return ResultatExecution.Ok(new() { ["frames"] = files });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static void EnregistrerFusionnerVideos()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "fusionner_videos", "Fusionner vidéos", "Concatene plusieurs videos (ffmpeg concat demuxer).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("videos", TypePort.Liste, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>(),
            async ctx =>
            {
                var raw = ctx.Entree("videos");
                if (raw is null) return ResultatExecution.Fail("liste videos vide");
                string[] videos;
                if (raw is System.Collections.IEnumerable e) videos = e.Cast<object?>().Select(o => o?.ToString() ?? "").ToArray();
                else if (raw is string s) videos = s.Split('\n');
                else videos = new[] { raw.ToString() ?? "" };
                if (videos.Length == 0) return ResultatExecution.Fail("aucune video");
                var listFile = Path.Combine(Path.GetTempPath(), "atelier_concat_" + Guid.NewGuid().ToString("N") + ".txt");
                var outFile = Path.Combine(Path.GetTempPath(), "atelier_fusion_" + Guid.NewGuid().ToString("N") + ".mp4");
                File.WriteAllLines(listFile, videos.Select(v => "file '" + v.Replace("'", "'\\''") + "'"));
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                        "-y -f concat -safe 0 -i \"" + listFile + "\" -c copy \"" + outFile + "\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit();
                    return ResultatExecution.Ok(new() { ["video"] = outFile });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
                finally { try { File.Delete(listFile); } catch { } }
            }
        ));
    }

    private static void EnregistrerDecouperVideo()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "decouper_video", "Découper vidéo", "Coupe un segment d'une video entre deux temps (secondes).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("video", TypePort.Video, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>
            {
                new("debut", "Debut (s)", "nombre", 0.0),
                new("fin", "Fin (s)", "nombre", 10.0),
            },
            async ctx =>
            {
                var v = ctx.Entree("video") ?? ctx.Ch("video");
                var d = ctx.ChDouble("debut", 0);
                var f = ctx.ChDouble("fin", 10);
                if (string.IsNullOrEmpty(v) || !File.Exists(v)) return ResultatExecution.Fail("video introuvable");
                var outFile = Path.Combine(Path.GetTempPath(), "atelier_cut_" + Guid.NewGuid().ToString("N") + ".mp4");
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                        "-y -ss " + d.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -i \"" + v + "\" -t " + (f - d).ToString(System.Globalization.CultureInfo.InvariantCulture) + " -c copy \"" + outFile + "\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit();
                    return ResultatExecution.Ok(new() { ["video"] = outFile });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    private static void EnregistrerRedimensionnerImage()
    {
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "redimensionner_image", "Redimensionner image", "Redimensionne une image (ffmpeg).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port> { new("image", TypePort.Image, false) },
            new List<ParametreNoeud>
            {
                new("largeur", "Largeur (px)", "nombre", 1024),
                new("hauteur", "Hauteur (px)", "nombre", 1024),
            },
            async ctx =>
            {
                var img = ctx.Entree("image") ?? ctx.Ch("image");
                var w = ctx.ChInt("largeur", 1024);
                var h = ctx.ChInt("hauteur", 1024);
                if (string.IsNullOrEmpty(img) || !File.Exists(img)) return ResultatExecution.Fail("image introuvable");
                var outFile = Path.Combine(Path.GetTempPath(), "atelier_resize_" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                        "-y -i \"" + img + "\" -vf scale=" + w + ":" + h + " \"" + outFile + "\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit();
                    return ResultatExecution.Ok(new() { ["image"] = outFile });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));
    }

    // === Helpers de resolution des manifestes modele.json ===

    private static ServeurDiffusion.ModeleSpec? TrouverModele(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var racine in new[] { Path.Combine(ModelesRacine, "image"), Path.Combine(ModelesRacine, "video") })
        {
            foreach (var m in ServeurDiffusion.ChargerModeles(racine))
            {
                if (m.Id == id) return m;
            }
        }
        return null;
    }

    private static ServeurDiffusion.ModeleSpec? TrouverPremierModele(string racine)
    {
        var dir = Path.Combine(ModelesRacine, racine);
        var liste = ServeurDiffusion.ChargerModeles(dir);
        return liste.Count > 0 ? liste[0] : null;
    }

    private static ServeurDiffusion.ModeleSpec? TrouverPremierModeleI2V()
    {
        var dir = Path.Combine(ModelesRacine, "video");
        foreach (var m in ServeurDiffusion.ChargerModeles(dir))
        {
            if (m.Id.Contains("i2v", StringComparison.OrdinalIgnoreCase)) return m;
        }
        return null;
    }
}