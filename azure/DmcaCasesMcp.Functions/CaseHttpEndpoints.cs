using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Honest 1:1 HTTP mirrors of MCP tools under /api/...
/// Useful for curl/health checks; MCP clients should prefer /runtime/webhooks/mcp.
/// </summary>
public sealed class CaseHttpEndpoints
{
    private readonly DmcaApiClient _client;
    private readonly ILogger<CaseHttpEndpoints> _logger;

    public CaseHttpEndpoints(DmcaApiClient client, ILogger<CaseHttpEndpoints> logger)
    {
        _client = client;
        _logger = logger;
    }

    [Function("HttpListCases")]
    public Task<HttpResponseData> ListCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listCases", CopyQuery(req, "page"), ct);

    [Function("HttpListDiyCases")]
    public Task<HttpResponseData> ListDiyCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listDIYCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listDIYCases", CopyQuery(req, "page"), ct);

    [Function("HttpListComplianceCases")]
    public Task<HttpResponseData> ListComplianceCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/listComplianceCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listComplianceCases", CopyQuery(req, "page"), ct);

    [Function("HttpGetCaseById")]
    public async Task<HttpResponseData> GetCaseById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/getCaseById")] HttpRequestData req,
        CancellationToken ct)
    {
        var id = GetQuery(req, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("{\"error\":\"query parameter id is required\"}", ct).ConfigureAwait(false);
            bad.Headers.Add("Content-Type", "application/json");
            return bad;
        }

        return await ProxyGet(req, "/getCaseById", new Dictionary<string, string?> { ["id"] = id }, ct)
            .ConfigureAwait(false);
    }


    [Function("HttpLogin")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/login")] HttpRequestData req,
        CancellationToken ct)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":\"email and password are required\"}", ct).ConfigureAwait(false);
            return bad;
        }
        return await ProxyPost(req, "/login", new { email, password }, withToken: false, ct).ConfigureAwait(false);
    }

    [Function("HttpCreateCase")]
    public async Task<HttpResponseData> CreateCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createCase")] HttpRequestData req,
        CancellationToken ct)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        string? Get(string n) => root.TryGetProperty(n, out var el) ? el.GetString() : null;
        var subject = Get("subject");
        var description = Get("description");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":\"subject and description are required\"}", ct).ConfigureAwait(false);
            return bad;
        }
        var payload = new Dictionary<string, object?> { ["subject"] = subject, ["description"] = description };
        foreach (var k in new[] { "copiedFromUrl", "infringingUrl", "infringingSiteIp" })
        {
            var v = Get(k);
            if (!string.IsNullOrWhiteSpace(v)) payload[k] = v;
        }
        return await ProxyPost(req, "/createCase", payload, withToken: true, ct).ConfigureAwait(false);
    }

    [Function("HttpUpdateCase")]
    public async Task<HttpResponseData> UpdateCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/updateCase")] HttpRequestData req,
        CancellationToken ct)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        string? Get(string n) => root.TryGetProperty(n, out var el) ? el.GetString() : null;
        var caseId = Get("case_id");
        var status = Get("status");
        var subject = Get("subject");
        var description = Get("description");
        if (string.IsNullOrWhiteSpace(caseId) || string.IsNullOrWhiteSpace(status)
            || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":\"case_id, status, subject, and description are required\"}", ct).ConfigureAwait(false);
            return bad;
        }
        var payload = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["status"] = status,
            ["subject"] = subject,
            ["description"] = description,
        };
        foreach (var k in new[] { "copiedFromUrl", "infringingUrl", "infringingSiteIp", "priority" })
        {
            var v = Get(k);
            if (!string.IsNullOrWhiteSpace(v)) payload[k] = v;
        }
        return await ProxyPost(req, "/updateCase", payload, withToken: true, ct).ConfigureAwait(false);
    }


    [Function("HttpCreateDiyCase")]
    public async Task<HttpResponseData> CreateDiyCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createDIYCase")] HttpRequestData req,
        CancellationToken ct)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        string? Get(string n) => root.TryGetProperty(n, out var el) ? el.GetString() : null;
        var subject = Get("subject");
        var description = Get("description");
        var type = Get("type");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(type))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":\"subject, description, and type are required\"}", ct).ConfigureAwait(false);
            return bad;
        }
        var payload = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["description"] = description,
            ["type"] = type,
        };
        foreach (var k in new[] { "copiedFromUrl", "infringingUrl", "infringingSiteIp" })
        {
            var v = Get(k);
            if (!string.IsNullOrWhiteSpace(v)) payload[k] = v;
        }
        return await ProxyPost(req, "/createDIYCase", payload, withToken: true, ct).ConfigureAwait(false);
    }

    [Function("HttpCreateComplianceCase")]
    public async Task<HttpResponseData> CreateComplianceCase(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/createComplianceCase")] HttpRequestData req,
        CancellationToken ct)
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        string? Get(string n) => root.TryGetProperty(n, out var el) ? el.GetString() : null;
        var submitterEmail = Get("submitterEmail");
        var submitterFirstName = Get("submitterFirstName");
        var submitterLastName = Get("submitterLastName");
        var description = Get("description");
        var siteId = Get("siteId");
        if (string.IsNullOrWhiteSpace(submitterEmail) || string.IsNullOrWhiteSpace(submitterFirstName)
            || string.IsNullOrWhiteSpace(submitterLastName) || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(siteId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":\"submitterEmail, submitterFirstName, submitterLastName, description, and siteId are required\"}", ct).ConfigureAwait(false);
            return bad;
        }
        var payload = new Dictionary<string, object?>
        {
            ["submitterEmail"] = submitterEmail,
            ["submitterFirstName"] = submitterFirstName,
            ["submitterLastName"] = submitterLastName,
            ["description"] = description,
            ["siteId"] = siteId,
        };
        foreach (var k in new[] { "submitterCompanyName", "copiedFromUrl", "infringingUrl", "infringingSiteIp" })
        {
            var v = Get(k);
            if (!string.IsNullOrWhiteSpace(v)) payload[k] = v;
        }
        return await ProxyPost(req, "/createComplianceCase", payload, withToken: true, ct).ConfigureAwait(false);
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
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("{\"error\":\"query parameter domain is required\"}", ct).ConfigureAwait(false);
            bad.Headers.Add("Content-Type", "application/json");
            return bad;
        }

        var path = "/getSiteReport/" + Uri.EscapeDataString(domain.Trim());
        return await ProxyGet(req, path, null, ct).ConfigureAwait(false);
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
          "version": "1.2.0",
          "policy": "list-get-plus-login-create-update-diy-compliance-site-report",
          "mcpStreamableHttp": "/runtime/webhooks/mcp",
          "mcpSse": "/runtime/webhooks/mcp/sse",
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
          "note": "Use Microsoft.Azure.Functions.Worker.Extensions.Mcp Streamable HTTP at /runtime/webhooks/mcp. Pass x-functions-key with the mcp_extension system key unless webhookAuthorizationLevel is Anonymous. getSiteReport HTTP mirror uses query ?domain=; upstream calls GET /getSiteReport/{domain}."
        }
        """;
        await res.WriteStringAsync(body, ct).ConfigureAwait(false);
        return res;
    }

    private async Task<HttpResponseData> ProxyGet(
        HttpRequestData req,
        string path,
        IDictionary<string, string?>? query,
        CancellationToken ct)
    {
        try
        {
            var json = await _client.GetRawAsync(path, query, ct).ConfigureAwait(false);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json");
            await ok.WriteStringAsync(json, ct).ConfigureAwait(false);
            return ok;
        }
        catch (DmcaApiException ex)
        {
            _logger.LogWarning("Upstream error {Path} HTTP {Status}", path, ex.StatusCode);
            var res = req.CreateResponse((HttpStatusCode)ex.StatusCode);
            res.Headers.Add("Content-Type", "application/json");
            await res.WriteStringAsync(ex.ResponseBody, ct).ConfigureAwait(false);
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy failure for {Path}", path);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            res.Headers.Add("Content-Type", "application/json");
            await res.WriteStringAsync($"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}", ct)
                .ConfigureAwait(false);
            return res;
        }
    }


    private async Task<HttpResponseData> ProxyPost(
        HttpRequestData req,
        string path,
        object payload,
        bool withToken,
        CancellationToken ct)
    {
        try
        {
            var json = await _client.PostRawAsync(path, payload, withToken, ct).ConfigureAwait(false);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json");
            await ok.WriteStringAsync(json, ct).ConfigureAwait(false);
            return ok;
        }
        catch (DmcaApiException ex)
        {
            _logger.LogWarning("Upstream error {Path} HTTP {Status}", path, ex.StatusCode);
            var res = req.CreateResponse((HttpStatusCode)ex.StatusCode);
            res.Headers.Add("Content-Type", "application/json");
            await res.WriteStringAsync(ex.ResponseBody, ct).ConfigureAwait(false);
            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy failure for {Path}", path);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            res.Headers.Add("Content-Type", "application/json");
            await res.WriteStringAsync($"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}", ct)
                .ConfigureAwait(false);
            return res;
        }
    }

    private static Dictionary<string, string?>? CopyQuery(HttpRequestData req, params string[] keys)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var v = GetQuery(req, key);
            if (!string.IsNullOrEmpty(v)) dict[key] = v;
        }

        return dict.Count == 0 ? null : dict;
    }

    private static string? GetQuery(HttpRequestData req, string key)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return query.Get(key);
    }
}
