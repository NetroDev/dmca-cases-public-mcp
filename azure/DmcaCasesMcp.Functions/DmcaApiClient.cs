using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _config;
    private readonly ILogger<DmcaApiClient> _logger;

    public DmcaApiClient(HttpClient http, IConfiguration config, ILogger<DmcaApiClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.BaseAddress = new Uri(ApiBase);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string ResolveToken()
    {
        var token = _config["DMCA_API_TOKEN"]
            ?? _config["DMCA_TOKEN"]
            ?? Environment.GetEnvironmentVariable("DMCA_API_TOKEN")
            ?? Environment.GetEnvironmentVariable("DMCA_TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Missing DMCA API token. Set DMCA_API_TOKEN (or DMCA_TOKEN) app setting / environment variable.");
        }

        return token.Trim();
    }

    /// <summary>
    /// GET a DMCA.com path. <paramref name="path"/> may include path segments
    /// (e.g. <c>/getSiteReport/{domain}</c>); optional <paramref name="query"/> is appended as query string.
    /// </summary>
    public async Task<string> GetRawAsync(string path, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var token = ResolveToken();
        var uri = BuildUri(path, query);

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("Token", token);

        _logger.LogInformation("DMCA GET {Path}", path);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var envelope = JsonSerializer.Serialize(new
            {
                error = $"DMCA API {path} returned HTTP {(int)res.StatusCode}",
                status = (int)res.StatusCode,
                body = TryParseJson(body)
            });
            throw new DmcaApiException((int)res.StatusCode, envelope);
        }

        return string.IsNullOrWhiteSpace(body) ? "null" : body;
    }

    /// <summary>
    /// POST JSON. When withToken is true, sends Token header from app settings.
    /// Never logs request body (may contain password) or token values.
    /// </summary>
    public async Task<string> PostRawAsync(string path, object payload, bool withToken = true, CancellationToken ct = default)
    {
        var uri = BuildUri(path, null);
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        if (withToken)
        {
            req.Headers.TryAddWithoutValidation("Token", ResolveToken());
        }

        var json = JsonSerializer.Serialize(payload);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("DMCA POST {Path}", path);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var envelope = JsonSerializer.Serialize(new
            {
                error = $"DMCA API {path} returned HTTP {(int)res.StatusCode}",
                status = (int)res.StatusCode,
                body = TryParseJson(body)
            });
            throw new DmcaApiException((int)res.StatusCode, envelope);
        }

        return string.IsNullOrWhiteSpace(body) ? "null" : body;
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
    public string ResponseBody { get; }

    public DmcaApiException(int statusCode, string responseBody)
        : base($"DMCA API HTTP {statusCode}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
