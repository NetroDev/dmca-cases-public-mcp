using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Thin read-only client for https://api.dmca.com case GET endpoints.
/// Token is never logged.
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
            // Do not include token. Body may contain account data — return to caller as JSON error envelope.
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
