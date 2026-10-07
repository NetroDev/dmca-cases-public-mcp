using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Thin client for https://api.dmca.com case GET/POST endpoints.
/// Tokens and passwords are never logged.
/// </summary>
public sealed class DmcaApiClient
{
    public const string ApiBase = "https://api.dmca.com";

    private readonly HttpClient _http;
    private readonly ILogger<DmcaApiClient> _logger;

    public DmcaApiClient(HttpClient http, ILogger<DmcaApiClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(ApiBase);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// GET a DMCA.com path with the given token sent as the <c>Token</c> header.
    /// <paramref name="path"/> may include path segments (e.g. <c>/getSiteReport/{domain}</c>);
    /// optional <paramref name="query"/> is appended as query string.
    /// </summary>
    public async Task<string> GetRawAsync(string path, IDictionary<string, string?>? query, string token, CancellationToken ct = default)
    {
        var uri = BuildUri(path, query);

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("Token", token);

        _logger.LogInformation("DMCA GET {Path}", path);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = Unwrap(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        ThrowIfNotOk(path, res, body);
        return body;
    }

    /// <summary>
    /// POST JSON. When <paramref name="token"/> is non-null it is sent as the <c>Token</c> header
    /// (login passes null). Never logs request body (may contain password) or token values.
    /// </summary>
    public async Task<string> PostRawAsync(string path, object payload, string? token, CancellationToken ct = default)
    {
        var uri = BuildUri(path, null);
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        if (token is not null)
        {
            req.Headers.TryAddWithoutValidation("Token", token);
        }

        var json = JsonSerializer.Serialize(payload);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("DMCA POST {Path}", path);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = Unwrap(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        ThrowIfNotOk(path, res, body);
        return body;
    }

    /// <summary>
    /// When asked for JSON the DMCA API wraps its payload in a JSON string (e.g. <c>"[{\"ID\":...}]"</c>,
    /// or <c>""</c> for an empty 404). Unwrap one level so callers get the real JSON; an empty
    /// wrapped string becomes an empty body.
    /// </summary>
    internal static string Unwrap(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith('"')) return body;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.String) return body;
            var inner = (doc.RootElement.GetString() ?? string.Empty).Trim();
            if (inner.Length == 0) return string.Empty;
            if (inner.StartsWith('[') || inner.StartsWith('{'))
            {
                using var _ = JsonDocument.Parse(inner);
                return inner;
            }
        }
        catch (JsonException)
        {
        }

        return body;
    }

    private static void ThrowIfNotOk(string path, HttpResponseMessage res, string body)
    {
        if (res.IsSuccessStatusCode) return;

        var status = (int)res.StatusCode;
        var envelope = JsonSerializer.Serialize(new
        {
            error = $"DMCA API {path} returned HTTP {status}",
            status,
            body = TryParseJson(body)
        });
        throw new DmcaApiException(status, envelope, body);
    }

    private static Uri BuildUri(string path, IDictionary<string, string?>? query)
    {
        var builder = new UriBuilder(new Uri(new Uri(ApiBase), path.TrimStart('/')));
        if (query is null || query.Count == 0)
        {
            return builder.Uri;
        }

        var qs = new List<string>();
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrEmpty(value)) continue;
            qs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        builder.Query = string.Join("&", qs);
        return builder.Uri;
    }

    private static object? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch
        {
            return text;
        }
    }
}

public sealed class DmcaApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>JSON error envelope (error, status, body) suitable for returning to callers.</summary>
    public string ResponseBody { get; }

    /// <summary>Upstream response body exactly as received (may be empty).</summary>
    public string RawBody { get; }

    public DmcaApiException(int statusCode, string responseBody, string rawBody)
        : base($"DMCA API HTTP {statusCode}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RawBody = rawBody;
    }
}
