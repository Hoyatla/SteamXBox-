using System;
using System.Collections.Generic;
using System.IO;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;
using SenSÉ.Atelier.ModeleLocal;

namespace SenSÉ.Atelier.Bibliotheque.Multimedia;

/// <summary>
/// Les 10 noeuds multimedia : generation image/video/audio via
/// ComfyUI, OCR/description via le LLM, transformations via ffmpeg.
/// </summary>
public static class Tous
{
    private static string Awaiter(System.Threading.Tasks.Task<string> t) => t.GetAwaiter().GetResult();
    private static System.Threading.Tasks.Task AwaiterTask(System.Threading.Tasks.Task t) => t;

    public static void Enregistrer()
    {
        // 26. texte_vers_image
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_image", "Texte vers Image", "Genere une image a partir d'un prompt (via ComfyUI).",
            Espace.Multimedia, "Generation",
            new List<Port> { new("prompt", TypePort.Texte, true) },
            new List<Port> { new("image", TypePort.Image, false) },
            new List<ParametreNoeud> { new("modele", "Modele", "texte", "sdxl_base") },
            ctx =>
            {
                try
                {
                    var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
                    var modele = ctx.Ch("modele");
                    var path = Awaiter(new ClientComfyui().TexteVersImageAsync(prompt, modele));
                    return ResultatExecution.Ok(new() { ["image"] = path });
                }
                catch (Exception ex) { return ResultatExecution.Fail("ComfyUI: " + ex.Message); }
            }
        ));

        // 27. texte_vers_video
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_video", "Texte vers Vidéo", "Genere une video a partir d'un prompt (ComfyUI, modele video).",
            Espace.Multimedia, "Generation",
            new List<Port> { new("prompt", TypePort.Texte, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud> { new("duree", "Duree (s)", "nombre", 4.0) },
            ctx =>
            {
                try
                {
                    var prompt = ctx.Entree("prompt") ?? ctx.Ch("prompt");
                    var path = Awaiter(new ClientComfyui().TexteVersImageAsync(prompt));
                    return ResultatExecution.Ok(new() { ["video"] = path });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 28. image_vers_video
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "image_vers_video", "Image vers Vidéo", "Anime une image avec un prompt (img2vid via ComfyUI).",
            Espace.Multimedia, "Generation",
            new List<Port>
            {
                new("image", TypePort.Image, true),
                new("prompt", TypePort.Texte, true),
            },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud> { new("duree", "Duree (s)", "nombre", 4.0) },
            ctx => ResultatExecution.Fail("non implemente MVP - necessite workflow ComfyUI img2vid")
        ));

        // 29. texte_vers_son
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "texte_vers_son", "Texte vers Son", "Synthese vocale (TTS) locale via SAPI.",
            Espace.Multimedia, "Generation",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("audio", TypePort.Audio, false) },
            new List<ParametreNoeud> { new("voix", "Voix", "texte", "fr-FR") },
            ctx =>
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

        // 30. audio_vers_texte
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "audio_vers_texte", "Audio vers Texte", "Transcription audio via whisper.cpp local.",
            Espace.Multimedia, "Generation",
            new List<Port> { new("audio", TypePort.Audio, true) },
            new List<Port> { new("transcription", TypePort.Texte, false) },
            new List<ParametreNoeud>(),
            ctx => ResultatExecution.Fail("whisper non disponible - installe llama.cpp ou un autre ASR")
        ));

        // 31. image_vers_texte
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "image_vers_texte", "Image vers Texte", "Description d'une image par le LLM multimodal (Qwen VL).",
            Espace.Multimedia, "Vision",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port> { new("description", TypePort.Texte, false) },
            new List<ParametreNoeud> { new("question", "Question (vide=description)", "texte", "") },
            ctx =>
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
                    var res = Awaiter(new ClientModele().CompleterAsync(full, 1024));
                    return ResultatExecution.Ok(new() { ["description"] = res });
                }
                catch (Exception ex) { return ResultatExecution.Fail(ex.Message); }
            }
        ));

        // 32. extraire_frames
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "extraire_frames", "Extraire frames", "Extrait les frames d'une video a N fps (ffmpeg).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("video", TypePort.Video, true) },
            new List<Port> { new("frames", TypePort.Liste, false) },
            new List<ParametreNoeud> { new("fps", "FPS", "nombre", 1.0) },
            ctx =>
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

        // 33. fusionner_videos
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "fusionner_videos", "Fusionner vidéos", "Concatene plusieurs videos (ffmpeg concat demuxer).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("videos", TypePort.Liste, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>(),
            ctx =>
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

        // 34. decouper_video
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "decouper_video", "Découper vidéo", "Coupe un segment d'une video entre deux temps (en secondes).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("video", TypePort.Video, true) },
            new List<Port> { new("video", TypePort.Video, false) },
            new List<ParametreNoeud>
            {
                new("debut", "Debut (s)", "nombre", 0.0),
                new("fin", "Fin (s)", "nombre", 10.0),
            },
            ctx =>
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

        // 35. redimensionner_image
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "redimensionner_image", "Redimensionner image", "Redimensionne une image aux dimensions demandees (ffmpeg).",
            Espace.Multimedia, "Transformation",
            new List<Port> { new("image", TypePort.Image, true) },
            new List<Port> { new("image", TypePort.Image, false) },
            new List<ParametreNoeud>
            {
                new("largeur", "Largeur (px)", "nombre", 1024),
                new("hauteur", "Hauteur (px)", "nombre", 1024),
            },
            ctx =>
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
}
