using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Mcp;

/// <summary>Client HTTP pour ComfyUI (generation image/video/audio).</summary>
public sealed class ClientComfyui
{
    private readonly HttpClient _http;
    public string UrlBase { get; }

    public ClientComfyui(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_COMFYUI_URL")
            ?? "http://127.0.0.1:8188").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    private async Task<JsonElement> AppelerAsync(string path, object? body, CancellationToken ct = default)
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body);
        using var resp = await _http.PostAsync(
            $"{UrlBase}{path}",
            new StringContent(json, Encoding.UTF8, "application/json"),
            ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception($"ComfyUI HTTP {(int)resp.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Soumet un prompt texte-vers-image, attend le resultat, renvoie le chemin du PNG.</summary>
    public async Task<string> TexteVersImageAsync(string prompt, string? modele = null, CancellationToken ct = default)
    {
        var r = await AppelerAsync("/prompt", new
        {
            prompt = new Dictionary<string, object?>
            {
                ["3"] = new { class_type = "KSampler", inputs = new { steps = 20, cfg = 7, sampler_name = "euler", scheduler = "normal", denoise = 1.0 } },
                ["6"] = new { class_type = "CLIPTextEncode", inputs = new { text = prompt, clip = new object[] { "4", 1 } } },
                ["7"] = new { class_type = "CLIPTextEncode", inputs = new { text = "low quality, blurry", clip = new object[] { "4", 1 } } },
                ["8"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "3", 0 }, vae = new object[] { "9", 0 } } },
                ["9"] = new { class_type = "VAELoader", inputs = new { vae_name = "vae-ft-mse-840000.safetensors" } },
            },
        }, ct);
        // Simplifie : ComfyUI repond avec {prompt_id}, on attend ensuite /history/<id>
        var promptId = r.TryGetProperty("prompt_id", out var pid) ? pid.GetString() : null;
        if (promptId is null) throw new Exception("ComfyUI n'a pas renvoye de prompt_id");
        return await AttendreImageAsync(promptId, ct);
    }

    private async Task<string> AttendreImageAsync(string promptId, CancellationToken ct)
    {
        for (int i = 0; i < 120; i++) // 10 min max
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5_000, ct);
            using var resp = await _http.GetAsync($"{UrlBase}/history/{promptId}", ct);
            if (!resp.IsSuccessStatusCode) continue;
            var text = await resp.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty(promptId, out var entry) &&
                    entry.TryGetProperty("outputs", out var outs))
                {
                    foreach (var img in outs.EnumerateObject())
                    {
                        if (img.Value.TryGetProperty("images", out var images) &&
                            images.GetArrayLength() > 0)
                        {
                            var file = images[0].TryGetProperty("filename", out var fn) ? fn.GetString() : null;
                            if (file is not null) return Path.Combine(
                                Environment.GetEnvironmentVariable("COMFYUI_OUTPUT") ?? "", file);
                        }
                    }
                }
            }
            catch { }
        }
        throw new Exception("ComfyUI: timeout en attendant l'image");
    }
}