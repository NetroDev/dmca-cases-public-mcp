using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Honest 1:1 HTTP mirrors of the four read tools under /api/...
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
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "listCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listCases", CopyQuery(req, "page"), ct);

    [Function("HttpListDiyCases")]
    public Task<HttpResponseData> ListDiyCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "listDIYCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listDIYCases", CopyQuery(req, "page"), ct);

    [Function("HttpListComplianceCases")]
    public Task<HttpResponseData> ListComplianceCases(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "listComplianceCases")] HttpRequestData req,
        CancellationToken ct)
        => ProxyGet(req, "/listComplianceCases", CopyQuery(req, "page"), ct);

    [Function("HttpGetCaseById")]
    public async Task<HttpResponseData> GetCaseById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "getCaseById")] HttpRequestData req,
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

    /// <summary>
    /// Documented MCP entrypoint pointer (not a full streamable-http implementation).
    /// Clients should connect to /runtime/webhooks/mcp provided by the Functions MCP extension.
    /// </summary>
    [Function("HttpMcpInfo")]
    public async Task<HttpResponseData> McpInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mcp")] HttpRequestData req,
        CancellationToken ct)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        const string body = """
        {
          "name": "dmca-cases",
          "version": "1.0.0",
          "policy": "read-only-v1",
          "mcpStreamableHttp": "/runtime/webhooks/mcp",
          "mcpSse": "/runtime/webhooks/mcp/sse",
          "httpToolMirrors": [
            "/api/listCases",
            "/api/listDIYCases",
            "/api/listComplianceCases",
            "/api/getCaseById"
          ],
          "note": "Use Microsoft.Azure.Functions.Worker.Extensions.Mcp Streamable HTTP at /runtime/webhooks/mcp. Pass x-functions-key with the mcp_extension system key unless webhookAuthorizationLevel is Anonymous."
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
