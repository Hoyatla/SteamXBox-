using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Mcp;

/// <summary>Client HTTP loopback pour mcp-saisie (127.0.0.1:8766).</summary>
public sealed class ClientSaisie
{
    private readonly HttpClient _http;
    public string UrlBase { get; }

    public ClientSaisie(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_SAISIE_URL")
            ?? "http://127.0.0.1:8766").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private async Task<string> AppelerAsync(string tool, object? body, CancellationToken ct = default)
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body);
        using var resp = await _http.PostAsync(
            $"{UrlBase}/saisie/{tool}",
            new StringContent(json, Encoding.UTF8, "application/json"),
            ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception($"mcp-saisie HTTP {(int)resp.StatusCode}: {text}");
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
            {
                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : text;
                throw new Exception("mcp-saisie: " + err);
            }
            if (doc.RootElement.TryGetProperty("data", out var d)) return d.ToString();
        }
        catch (JsonException) { }
        return text;
    }

    public Task<string> ScreenshotEcranAsync() => AppelerAsync("screenshot_ecran", null);
    public Task<string> ScreenshotFenetreAsync(string titre) => AppelerAsync("screenshot_fenetre", new { titre });
    public Task<string> SourisDeplacerAsync(int x, int y) => AppelerAsync("souris_deplacer", new { x, y });
    public Task<string> SourisCliquerAsync(string bouton, int? x, int? y) => AppelerAsync("souris_cliquer", new { bouton, x, y });
    public Task<string> ClavierTaperAsync(string texte) => AppelerAsync("clavier_taper", new { texte });
    public Task<string> ClavierToucheAsync(string touche) => AppelerAsync("clavier_touche", new { touche });
    public Task<string> ListerFenetresAsync() => AppelerAsync("lister_fenetres", null);
}