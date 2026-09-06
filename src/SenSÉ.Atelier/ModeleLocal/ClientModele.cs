using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.ModeleLocal;

/// <summary>
/// Client HTTP pour invoquer le modele local Qwen. L'Atelier ne lance
/// PAS le modele (il vit dans SenSÉ.Desktop ou pc-agent) ; il parle a
/// un endpoint HTTP configurable.
/// </summary>
/// <remarks>
/// <para>Endpoint par defaut : <c>ATELIER_LLM_URL</c> ou
/// <c>http://127.0.0.1:8765/v1/llm/complete</c> (pc-agent).</para>
///
/// <para>Si l'endpoint n'est pas joignable, le client leve une
/// <see cref="ModeleInjoignableException"/> avec un message clair que
/// le moteur remonte tel quel.</para>
/// </remarks>
public sealed class ClientModele
{
    private readonly HttpClient _http;
    public string UrlBase { get; }
    public string Modele { get; set; } = "qwen-3.5-4b";

    public ClientModele(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("ATELIER_LLM_URL")
            ?? "http://127.0.0.1:8765/v1/llm/complete").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<string> CompleterAsync(string prompt, int maxTokens = 2048, CancellationToken ct = default)
    {
        var body = new
        {
            model = Modele,
            prompt,
            max_tokens = maxTokens,
            stream = false,
        };
        try
        {
            using var resp = await _http.PostAsync(
                UrlBase,
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
                ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new ModeleInjoignableException($"HTTP {(int)resp.StatusCode}: {text}");
            // Reponse OpenAI-style: {choices:[{message:{content:"..."}}]}
            // ou simplifiee: {text:"..."} ou {completion:"..."}
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var chs) && chs.GetArrayLength() > 0)
                {
                    var msg = chs[0].GetProperty("message");
                    if (msg.TryGetProperty("content", out var c)) return c.GetString() ?? "";
                }
                if (root.TryGetProperty("text", out var t)) return t.GetString() ?? "";
                if (root.TryGetProperty("completion", out var co)) return co.GetString() ?? "";
            }
            catch { /* pas du JSON */ }
            return text;
        }
        catch (HttpRequestException ex)
        {
            throw new ModeleInjoignableException(
                $"LLM injoignable a {UrlBase} : {ex.Message}. "
                + "Verifie que SenSÉ.Desktop ou pc-agent tourne, ou fixe ATELIER_LLM_URL.",
                ex);
        }
    }
}

public sealed class ModeleInjoignableException : Exception
{
    public ModeleInjoignableException(string message) : base(message) { }
    public ModeleInjoignableException(string message, Exception inner) : base(message, inner) { }
}