using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.EditeurTexte.Integration;

/// <summary>
/// Client HTTP vers l'Atelier (port 8770 par defaut) pour les fonctions multimodales
/// (LLM, ASR, vision). URL via ATELIER_URL ou defaut http://127.0.0.1:8770.
/// </summary>
public sealed class ClientAtelier : IDisposable
{
    public string UrlBase { get; }
    private readonly HttpClient _http;
    private readonly string? _token;

    public ClientAtelier(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_URL")
            ?? "http://127.0.0.1:8770").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _token = Environment.GetEnvironmentVariable("ATELIER_TOKEN");
    }

    public async Task<string> CompleterAsync(string prompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        var body = new { prompt, max_tokens = maxTokens };
        using var resp = await EnvoyerAsync("/atelier/llm/complete", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("ok").GetBoolean() == false)
        {
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "erreur inconnue";
            throw new InvalidOperationException("Atelier LLM : " + err);
        }
        return doc.RootElement.GetProperty("data").GetProperty("text").GetString() ?? "";
    }

    public async Task<string> TranscrireAsync(byte[] audioWav, string langue = "fr", CancellationToken ct = default)
    {
        // STUB Phase E.1 : sera branche quand Whisper sera dispo.
        throw new NotImplementedException(
            "ASR pas encore branche cote Atelier. Whisper en attente de backend stable (cf. phase E.2).");
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(string chemin, object body, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, UrlBase + chemin)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(_token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Atelier injoignable a {UrlBase} : {ex.Message}. Verifie que SenSÉ.Desktop tourne (il demarre l'Atelier sur 8770).", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
