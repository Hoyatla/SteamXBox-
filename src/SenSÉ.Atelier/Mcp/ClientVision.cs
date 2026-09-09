using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SenSÉ.Atelier.Mcp;

/// <summary>
/// Client HTTP pour parler à pc-agent (vision : YOLOX-Nano + YuNet + ByteTrack).
/// L'Atelier ne lance PAS pc-agent — il parle à un endpoint HTTP configurable.
/// </summary>
/// <remarks>
/// <para>Endpoint par défaut : <c>PC_AGENT_URL</c> ou <c>http://127.0.0.1:8765</c>.</para>
///
/// <para>Si pc-agent n'est pas joignable, le client lève une
/// <see cref="VisionInjoignableException"/> avec un message clair que
/// le moteur remonte tel quel.</para>
///
/// <para>Auth : bearer token si <c>PC_AGENT_TOKEN</c> est défini.</para>
/// </remarks>
public sealed class ClientVision : IDisposable
{
    public string UrlBase { get; }
    private readonly HttpClient _http;
    private readonly string? _token;

    /// <summary>Modèle de détection demandé (par défaut <c>yolox-nano</c> côté pc-agent).</summary>
    public string Modele { get; set; } = "yolox-nano";

    public ClientVision(string? urlBase = null)
    {
        UrlBase = (urlBase ?? Environment.GetEnvironmentVariable("PC_AGENT_URL")
            ?? "http://127.0.0.1:8765").TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _token = Environment.GetEnvironmentVariable("PC_AGENT_TOKEN");
    }

    /// <summary>
    /// Appelle <c>POST /v1/detect/frame</c> sur pc-agent. Le fichier
    /// pointé par <paramref name="cheminImage"/> est lu, encodé en base64,
    /// envoyé ; la réponse JSON est désérialisée (snake_case → PascalCase).
    /// </summary>
    public async Task<DetectionFrame> DetecterFrameAsync(
        string cheminImage,
        bool detecterVisages = false,
        IEnumerable<string>? filtreClasses = null,
        double? confianceMin = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(cheminImage))
            throw new FileNotFoundException("image introuvable", cheminImage);
        var bytes = await File.ReadAllBytesAsync(cheminImage, ct);
        var b64 = Convert.ToBase64String(bytes);

        var body = new Dictionary<string, object?>
        {
            ["image_b64"] = b64,
            ["detect_faces"] = detecterVisages,
        };
        if (filtreClasses is not null) body["classes_filter"] = filtreClasses;
        if (confianceMin is not null) body["conf"] = confianceMin.Value;

        using var resp = await EnvoyerAsync("/v1/detect/frame", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        var frame = JsonSerializer.Deserialize<DetectionFrame>(json, OptionsJson);
        if (frame is null)
            throw new InvalidOperationException("réponse vide de pc-agent");
        return frame;
    }

    /// <summary>
    /// Appelle <c>POST /v1/detect/video</c> sur pc-agent. Chaque chemin
    /// de frame est lu, encodé en base64, envoyé dans l'ordre.
    /// </summary>
    public async Task<DetectionVideo> TrackerVideoAsync(
        IReadOnlyList<string> cheminsFrames,
        bool detecterVisages = false,
        IEnumerable<string>? filtreClasses = null,
        double? confianceMin = null,
        int? maxFrames = null,
        CancellationToken ct = default)
    {
        var b64Frames = new List<string>(cheminsFrames.Count);
        foreach (var chemin in cheminsFrames)
        {
            if (!File.Exists(chemin))
                throw new FileNotFoundException("frame introuvable", chemin);
            var bytes = await File.ReadAllBytesAsync(chemin, ct);
            b64Frames.Add(Convert.ToBase64String(bytes));
        }

        var body = new Dictionary<string, object?>
        {
            ["frames_b64"] = b64Frames,
            ["detect_faces"] = detecterVisages,
        };
        if (filtreClasses is not null) body["classes_filter"] = filtreClasses;
        if (confianceMin is not null) body["conf"] = confianceMin.Value;
        if (maxFrames is not null) body["max_frames"] = maxFrames.Value;

        using var resp = await EnvoyerAsync("/v1/detect/video", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        var video = JsonSerializer.Deserialize<DetectionVideo>(json, OptionsJson);
        if (video is null)
            throw new InvalidOperationException("réponse vide de pc-agent");
        return video;
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
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            return await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new VisionInjoignableException(
                $"pc-agent injoignable à {UrlBase} : {ex.Message}. "
                + "Vérifie que pc-agent tourne (sinon lance-le à la main) ou fixe PC_AGENT_URL.",
                ex);
        }
    }

    /// <summary>
    /// Policy snake_case pour matcher la sortie de pc-agent (Rust serde
    /// utilise par défaut snake_case dans les sérialisations JSON).
    /// </summary>
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public void Dispose() => _http.Dispose();
}

public sealed class VisionInjoignableException : Exception
{
    public VisionInjoignableException(string message) : base(message) { }
    public VisionInjoignableException(string message, Exception inner) : base(message, inner) { }
}

// ----------------------------------------------------------------------------
// DTOs de la réponse pc-agent
// ----------------------------------------------------------------------------

/// <summary>Réponse de <c>POST /v1/detect/frame</c>.</summary>
public sealed class DetectionFrame
{
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<Detection> Detections { get; set; } = new();
    public List<FaceOut?> Faces { get; set; } = new();
    public long ElapsedMs { get; set; }
    public long ElapsedDecodeMs { get; set; }
    public long ElapsedModelMs { get; set; }
}

/// <summary>Une détection. Boîte en pixels d'image originale.</summary>
public sealed class Detection
{
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    public float Score { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>Visage détecté dans une box "person" (YuNet).</summary>
public sealed class FaceOut
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Score { get; set; }
    public float[][] Landmarks { get; set; } = Array.Empty<float[]>();
}

/// <summary>Réponse de <c>POST /v1/detect/video</c>.</summary>
public sealed class DetectionVideo
{
    public List<DetectionFrame> Frames { get; set; } = new();
    public List<TrackEvent> Events { get; set; } = new();
    public int TotalTracks { get; set; }
    public long ElapsedTotalMs { get; set; }
}

/// <summary>Un événement de tracking ByteTrack.</summary>
public sealed class TrackEvent
{
    public int FrameIdx { get; set; }
    public long TrackId { get; set; }
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    /// <summary>"appeared" | "continued" | "disappeared"</summary>
    public string Kind { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
