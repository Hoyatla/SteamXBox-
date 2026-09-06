using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Mcp;

/// <summary>Client HTTP loopback pour pc-agent (UIA automation, port 8765).</summary>
public sealed class ClientDebugApi
{
    private readonly HttpClient _http;
    public string UrlBase { get; }

    public ClientDebugApi(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_DEBUG_URL")
            ?? "http://127.0.0.1:8765").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private async Task<JsonElement> AppelerAsync(string method, string path, object? body, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(
            method == "GET" ? HttpMethod.Get : HttpMethod.Post,
            $"{UrlBase}{path}");
        if (body is not null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new Exception($"pc-agent HTTP {(int)resp.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> DumpAsync()
        => await AppelerAsync("POST", "/v1/uia/dump", null);
    public async Task<JsonElement> DumpWindowAsync(string title)
        => await AppelerAsync("POST", "/v1/uia/dump-window", new { title });
    public async Task<JsonElement> InvokeAsync(string automationId)
        => await AppelerAsync("POST", "/v1/uia/invoke", new { automationId });
    public async Task<JsonElement> SetTextAsync(string automationId, string value)
        => await AppelerAsync("POST", "/v1/uia/set-text", new { automationId, value });
    public async Task<JsonElement> FindMainEditAsync(string title)
        => await AppelerAsync("POST", "/v1/uia/find-main-edit", new { title });
    public async Task<JsonElement> ListWindowsAsync()
        => await AppelerAsync("GET", "/v1/uia/list-windows", null);
}