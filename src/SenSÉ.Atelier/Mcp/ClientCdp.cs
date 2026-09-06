using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Mcp;

/// <summary>Client HTTP loopback pour mcp-cdp (Chrome DevTools Protocol).</summary>
public sealed class ClientCdp
{
    private readonly HttpClient _http;
    public string UrlBase { get; }

    public ClientCdp(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_CDP_URL")
            ?? "http://127.0.0.1:9224").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private async Task<JsonElement> AppelerAsync(string path, object? body, CancellationToken ct = default)
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body);
        using var resp = await _http.PostAsync(
            $"{UrlBase}/{path}",
            new StringContent(json, Encoding.UTF8, "application/json"),
            ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception($"mcp-cdp HTTP {(int)resp.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
        {
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : text;
            throw new Exception("mcp-cdp: " + err);
        }
        return doc.RootElement.Clone();
    }

    public async Task NaviguerAsync(string url)
    {
        await AppelerAsync("navigate", new { url });
    }

    public async Task<string> EvalJsAsync(string code)
    {
        var r = await AppelerAsync("evaluate", new { expression = code });
        return r.TryGetProperty("data", out var d) ? d.ToString() : r.ToString();
    }

    public async Task<string> ScreenshotAsync()
    {
        var r = await AppelerAsync("screenshot", null);
        return r.TryGetProperty("data", out var d) ? d.ToString() : r.ToString();
    }
}