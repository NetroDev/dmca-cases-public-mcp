using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>Outcome of a DMCA operation: an HTTP-style status plus the JSON text to hand back.</summary>
public sealed record OpResult(int Status, string Json)
{
    /// <summary>Plain JSON for tool text: keeps quotes and backticks in messages readable.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public bool IsError => Status >= 400;

    public static OpResult Ok(string json) => new(200, json);

    public static OpResult Error(int status, string message)
        => new(status, JsonSerializer.Serialize(new { error = message, status }, JsonOptions));
}

/// <summary>
/// The DMCA.com calls behind both the MCP tools and the /api HTTP mirrors, so the two stay in step.
/// Token values and passwords are never logged; only the token's source is.
/// </summary>
public sealed class DmcaOperations
{
    private readonly DmcaApiClient _client;
    private readonly ILogger<DmcaOperations> _logger;

    public DmcaOperations(DmcaApiClient client, ILogger<DmcaOperations> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<OpResult> GetAsync(string path, IDictionary<string, string?>? query, ResolvedToken? token, CancellationToken ct)
        => RunAsync(path, token, t => _client.GetRawAsync(path, query, t, ct));

    public Task<OpResult> PostAsync(string path, object payload, ResolvedToken? token, CancellationToken ct)
        => RunAsync(path, token, t => _client.PostRawAsync(path, payload, t, ct));

    /// <summary>
    /// List endpoints. The DMCA API signals "no matching cases" inconsistently: /listDIYCases answers
    /// HTTP 404 with an empty body and /listComplianceCases answers HTTP 200 with an empty body.
    /// Both are turned into an explicit empty list. A 404 that carries a body (e.g. the gateway's
    /// "Resource not found") is still reported as an error.
    /// </summary>
    public async Task<OpResult> ListAsync(string path, int? page, ResolvedToken? token, CancellationToken ct)
    {
        var query = page is null ? null : new Dictionary<string, string?> { ["page"] = page.Value.ToString() };
        if (token is null) return MissingToken(path);

        try
        {
            LogCall(path, token.Value);
            var body = await _client.GetRawAsync(path, query, token.Value.Value, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body)
                ? OpResult.Ok(EmptyList(path, page, "HTTP 200 with an empty body"))
                : OpResult.Ok(body);
        }
        catch (DmcaApiException ex) when (ex.StatusCode == 404 && string.IsNullOrWhiteSpace(ex.RawBody))
        {
            _logger.LogInformation("DMCA {Path} returned 404 with empty body; treating as no results", path);
            return OpResult.Ok(EmptyList(path, page, "HTTP 404 with an empty body"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(path, ex);
        }
    }

    /// <summary>
    /// POST /login (no Token header). Returns the upstream result and, on success, the token it carried.
    /// </summary>
    public async Task<(OpResult Result, string? Token)> LoginAsync(string email, string password, CancellationToken ct)
    {
        try
        {
            var body = await _client.PostRawAsync("/login", new { email, password }, token: null, ct).ConfigureAwait(false);
            var token = DmcaTokenResolver.ExtractLoginToken(body);
            if (token is null)
            {
                return (OpResult.Error(502, "DMCA API /login succeeded but the response did not contain a token."), null);
            }

            return (OpResult.Ok(body), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Failure("/login", ex), null);
        }
    }

    /// <summary>
    /// POST /updateCase. The DMCA API overwrites status and priority with null when they are left out,
    /// so when the caller omits either one the current value is read from /getCaseById and sent back
    /// unchanged. Status therefore only changes when the caller asks for it.
    /// </summary>
    public async Task<OpResult> UpdateCaseAsync(
        string caseId,
        string? status,
        string? priority,
        string subject,
        string description,
        string? copiedFromUrl,
        string? infringingUrl,
        string? infringingSiteIp,
        ResolvedToken? token,
        CancellationToken ct)
    {
        if (token is null) return MissingToken("/updateCase");

        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(priority))
        {
            var current = await GetAsync("/getCaseById", new Dictionary<string, string?> { ["id"] = caseId }, token, ct)
                .ConfigureAwait(false);
            if (current.IsError)
            {
                return OpResult.Error(current.Status,
                    "Could not read the case's current status/priority (needed because the DMCA API clears them when they are not sent). "
                    + "Pass status and priority explicitly, or check case_id. Upstream: " + current.Json);
            }

            var (curStatus, curPriority) = ReadStatusAndPriority(current.Json);
            if (string.IsNullOrWhiteSpace(status)) status = curStatus;
            if (string.IsNullOrWhiteSpace(priority)) priority = curPriority;
        }

        var payload = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["subject"] = subject,
            ["description"] = description,
        };
        if (!string.IsNullOrWhiteSpace(status)) payload["status"] = status;
        if (!string.IsNullOrWhiteSpace(priority)) payload["priority"] = priority;
        if (!string.IsNullOrWhiteSpace(copiedFromUrl)) payload["copiedFromUrl"] = copiedFromUrl;
        if (!string.IsNullOrWhiteSpace(infringingUrl)) payload["infringingUrl"] = infringingUrl;
        if (!string.IsNullOrWhiteSpace(infringingSiteIp)) payload["infringingSiteIp"] = infringingSiteIp;

        return await PostAsync("/updateCase", payload, token, ct).ConfigureAwait(false);
    }

    /// <summary>Text returned by the login tool / mirror. Contains the token on purpose.</summary>
    public static string LoginResultJson(string token, bool sessionCached, string usage)
        => JsonSerializer.Serialize(new
        {
            token,
            tokenUsage = usage,
            sessionCached,
        }, OpResult.JsonOptions);

    private async Task<OpResult> RunAsync(string path, ResolvedToken? token, Func<string, Task<string>> call)
    {
        if (token is null) return MissingToken(path);

        try
        {
            LogCall(path, token.Value);
            var body = await call(token.Value.Value).ConfigureAwait(false);
            return OpResult.Ok(string.IsNullOrWhiteSpace(body) ? "null" : body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(path, ex);
        }
    }

    private void LogCall(string path, ResolvedToken token)
        => _logger.LogInformation("DMCA {Path} using token from {TokenSource}", path, token.Source);

    private OpResult MissingToken(string path)
    {
        _logger.LogInformation("DMCA {Path}: caller is not logged in and passed no token", path);
        return OpResult.Error(401, NotLoggedIn.Message);
    }

    private OpResult Failure(string path, Exception ex)
    {
        if (ex is DmcaApiException api)
        {
            _logger.LogWarning("DMCA API error for {Path}: HTTP {Status}", path, api.StatusCode);
            return new OpResult(api.StatusCode, api.ResponseBody);
        }

        _logger.LogError(ex, "Unexpected error calling {Path}", path);
        return OpResult.Error(500, ex.Message);
    }

    private static string EmptyList(string path, int? page, string upstream)
        => JsonSerializer.Serialize(new
        {
            cases = Array.Empty<object>(),
            count = 0,
            page,
            note = $"No cases returned. DMCA API {path} answered {upstream}, which it uses when the account has no matching cases.",
        }, OpResult.JsonOptions);

    private static (string? Status, string? Priority) ReadStatusAndPriority(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (GetString(root, "STATUS"), GetString(root, "PRIORITY"));
        }
        catch (JsonException)
        {
            return (null, null);
        }

        static string? GetString(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
