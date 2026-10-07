using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Honest 1:1 HTTP mirrors of MCP tools under /api/...
/// Useful for curl/health checks; MCP clients should prefer /runtime/webhooks/mcp.
/// The caller supplies their own token per request: `token` (query string or JSON body) or the
/// Authorization: Bearer / X-DMCA-Token / Token header. There is no server-wide token. Tokens and passwords are never logged.
/// </summary>
public sealed class CaseHttpEndpoints
{
    private readonly DmcaOperations _ops;

    public CaseHttpEndpoints(DmcaOperations ops)
    {
        _ops = ops;
    }

    [Function("HttpListCases")]
    public Task<HttpResponseData> ListCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listCases")] HttpRequestData req,
        CancellationToken ct)
        => List(req, "/listCases", ct);

    [Function("HttpListDiyCases")]
    public Task<HttpResponseData> ListDiyCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listDIYCases")] HttpRequestData req,
        CancellationToken ct)
        => List(req, "/listDIYCases", ct);

    [Function("HttpListComplianceCases")]
    public Task<HttpResponseData> ListComplianceCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listComplianceCases")] HttpRequestData req,
        CancellationToken ct)
        => List(req, "/listComplianceCases", ct);

    [Function("HttpGetCaseById")]
    public async Task<HttpResponseData> GetCaseById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/getCaseById")] HttpRequestData req,
        CancellationToken ct)
    {
        var id = GetQuery(req, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return await Respond(req, OpResult.Error(400, "query parameter id is required"), ct).ConfigureAwait(false);
        }

        var result = await _ops.GetAsync("/getCaseById", new Dictionary<string, string?> { ["id"] = id }, Token(req, null), ct)
            .ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpLogin")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/login")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await ReadBody(req, ct).ConfigureAwait(false);
        var email = Get(body, "email");
        var password = Get(body, "password");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return await Respond(req, OpResult.Error(400, "email and password are required"), ct).ConfigureAwait(false);
        }

        var (result, token) = await _ops.LoginAsync(email, password, ct).ConfigureAwait(false);
        if (token is not null)
        {
            result = OpResult.Ok(DmcaOperations.LoginResultJson(token, sessionCached: false,
                "Send this token on later requests as the X-DMCA-Token header (or a `token` field). The HTTP mirrors do not keep sessions."));
        }

        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpCreateCase")]
    public async Task<HttpResponseData> CreateCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createCase")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await ReadBody(req, ct).ConfigureAwait(false);
        var subject = Get(body, "subject");
        var description = Get(body, "description");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return await Respond(req, OpResult.Error(400, "subject and description are required"), ct).ConfigureAwait(false);
        }

        var payload = new Dictionary<string, object?> { ["subject"] = subject, ["description"] = description };
        CopyOptional(body, payload, "copiedFromUrl", "infringingUrl", "infringingSiteIp");
        var result = await _ops.PostAsync("/createCase", payload, Token(req, body), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpUpdateCase")]
    public async Task<HttpResponseData> UpdateCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/updateCase")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await ReadBody(req, ct).ConfigureAwait(false);
        var caseId = Get(body, "case_id");
        var subject = Get(body, "subject");
        var description = Get(body, "description");
        if (string.IsNullOrWhiteSpace(caseId) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return await Respond(req, OpResult.Error(400, "case_id, subject, and description are required"), ct).ConfigureAwait(false);
        }

        var result = await _ops.UpdateCaseAsync(
            caseId, Get(body, "status"), Get(body, "priority"), subject, description,
            Get(body, "copiedFromUrl"), Get(body, "infringingUrl"), Get(body, "infringingSiteIp"),
            Token(req, body), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpCreateDiyCase")]
    public async Task<HttpResponseData> CreateDiyCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createDIYCase")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await ReadBody(req, ct).ConfigureAwait(false);
        var subject = Get(body, "subject");
        var description = Get(body, "description");
        var type = Get(body, "type");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(type))
        {
            return await Respond(req, OpResult.Error(400, "subject, description, and type are required"), ct).ConfigureAwait(false);
        }

        var payload = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["description"] = description,
            ["type"] = type,
        };
        CopyOptional(body, payload, "copiedFromUrl", "infringingUrl", "infringingSiteIp");
        var result = await _ops.PostAsync("/createDIYCase", payload, Token(req, body), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpCreateComplianceCase")]
    public async Task<HttpResponseData> CreateComplianceCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createComplianceCase")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await ReadBody(req, ct).ConfigureAwait(false);
        var required = new[] { "submitterEmail", "submitterFirstName", "submitterLastName", "description", "siteId" };
        if (required.Any(k => string.IsNullOrWhiteSpace(Get(body, k))))
        {
            return await Respond(req, OpResult.Error(400,
                "submitterEmail, submitterFirstName, submitterLastName, description, and siteId are required"), ct).ConfigureAwait(false);
        }

        var payload = required.ToDictionary(k => k, k => (object?)Get(body, k));
        CopyOptional(body, payload, "submitterCompanyName", "copiedFromUrl", "infringingUrl", "infringingSiteIp");
        var result = await _ops.PostAsync("/createComplianceCase", payload, Token(req, body), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    [Function("HttpGetSiteReport")]
    public async Task<HttpResponseData> GetSiteReport(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/getSiteReport")] HttpRequestData req,
        CancellationToken ct)
    {
        // Prefer query ?domain= for Function routing; upstream call uses /getSiteReport/{domain}.
        var domain = GetQuery(req, "domain");
        if (string.IsNullOrWhiteSpace(domain))
        {
            return await Respond(req, OpResult.Error(400, "query parameter domain is required"), ct).ConfigureAwait(false);
        }

        var path = "/getSiteReport/" + Uri.EscapeDataString(domain.Trim());
        var result = await _ops.GetAsync(path, null, Token(req, null), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Documented MCP entrypoint pointer (not a full streamable-http implementation).
    /// Clients should connect to /runtime/webhooks/mcp provided by the Functions MCP extension.
    /// </summary>
    [Function("HttpMcpInfo")]
    public async Task<HttpResponseData> McpInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/mcp")] HttpRequestData req,
        CancellationToken ct)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        const string body = """
        {
          "name": "dmca-cases",
          "version": "1.2.1",
          "policy": "list-get-plus-login-create-update-diy-compliance-site-report",
          "homepage": "https://mcp.dmca.com/",
          "canonicalBaseUrl": "https://mcp.dmca.com",
          "fallbackBaseUrl": "https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net",
          "mcpStreamableHttp": "/runtime/webhooks/mcp",
          "mcpSse": "/runtime/webhooks/mcp/sse",
          "mcpStreamableHttpUrl": "https://mcp.dmca.com/runtime/webhooks/mcp",
          "mcpStreamableHttpFallbackUrl": "https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net/runtime/webhooks/mcp",
          "cover": ["/", "/cover"],
          "httpToolMirrors": [
            "/api/listCases",
            "/api/listDIYCases",
            "/api/listComplianceCases",
            "/api/getCaseById",
            "/api/login",
            "/api/createCase",
            "/api/updateCase",
            "/api/createDIYCase",
            "/api/createComplianceCase",
            "/api/getSiteReport"
          ],
          "note": "GET / (and /cover) serves the HTML cover page. The MCP protocol endpoint is fixed by the Azure Functions MCP extension at /runtime/webhooks/mcp (Streamable HTTP) and /runtime/webhooks/mcp/sse (legacy SSE); it is not served at /. No x-functions-key is needed for the MCP endpoint (webhookAuthorizationLevel is Anonymous); callers send their own DMCA.com API token as Authorization: Bearer <token> (or pass token, or call the login tool). https://mcp.dmca.com is the canonical host; the azurewebsites.net hostname works as a fallback. getSiteReport HTTP mirror uses query ?domain=; upstream calls GET /getSiteReport/{domain}."
        }
        """;
        await res.WriteStringAsync(body, ct).ConfigureAwait(false);
        return res;
    }

    private async Task<HttpResponseData> List(HttpRequestData req, string path, CancellationToken ct)
    {
        int? page = int.TryParse(GetQuery(req, "page"), out var p) ? p : null;
        var result = await _ops.ListAsync(path, page, Token(req, null), ct).ConfigureAwait(false);
        return await Respond(req, result, ct).ConfigureAwait(false);
    }

    /// <summary>`token` field/query, then Authorization: Bearer, X-DMCA-Token or Token header. Null when the caller sent none.</summary>
    private static ResolvedToken? Token(HttpRequestData req, JsonElement? body)
    {
        var explicitToken = (body is { } b ? Get(b, "token") : null) ?? GetQuery(req, "token");
        if (DmcaTokenResolver.NormalizeToken(explicitToken) is { } argument)
        {
            return new ResolvedToken(argument, TokenSource.Argument);
        }

        if (req.Headers.TryGetValues("Authorization", out var auth)
            && DmcaTokenResolver.AuthorizationToken(auth.FirstOrDefault()) is { } bearer)
        {
            return new ResolvedToken(bearer, TokenSource.Header);
        }

        foreach (var name in new[] { DmcaTokenResolver.TokenHeaderName, DmcaTokenResolver.DmcaTokenHeaderName })
        {
            if (req.Headers.TryGetValues(name, out var values)
                && DmcaTokenResolver.NormalizeToken(values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))) is { } value)
            {
                return new ResolvedToken(value, TokenSource.Header);
            }
        }

        return null;
    }

    private static async Task<HttpResponseData> Respond(HttpRequestData req, OpResult result, CancellationToken ct)
    {
        var res = req.CreateResponse((HttpStatusCode)result.Status);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(result.Json, ct).ConfigureAwait(false);
        return res;
    }

    private static async Task<JsonElement> ReadBody(HttpRequestData req, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? Get(JsonElement? body, string name)
        => body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static void CopyOptional(JsonElement body, Dictionary<string, object?> payload, params string[] keys)
    {
        foreach (var k in keys)
        {
            var v = Get(body, k);
            if (!string.IsNullOrWhiteSpace(v)) payload[k] = v;
        }
    }

    private static string? GetQuery(HttpRequestData req, string key)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return query.Get(key);
    }
}
