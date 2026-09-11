using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// MCP tool triggers — exposed at /runtime/webhooks/mcp (Streamable HTTP)
/// via Microsoft.Azure.Functions.Worker.Extensions.Mcp.
/// </summary>
public sealed class CaseTools
{
    private readonly DmcaApiClient _client;
    private readonly ILogger<CaseTools> _logger;

    public CaseTools(DmcaApiClient client, ILogger<CaseTools> logger)
    {
        _client = client;
        _logger = logger;
    }

    [Function(nameof(ListCasesTool))]
    public async Task<string> ListCasesTool(
        [McpToolTrigger("listCases", "GET https://api.dmca.com/listCases — list managed takedown cases. Optional page.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        CancellationToken ct)
    {
        return await SafeGet("/listCases", PageQuery(page), ct).ConfigureAwait(false);
    }

    [Function(nameof(ListDiyCasesTool))]
    public async Task<string> ListDiyCasesTool(
        [McpToolTrigger("listDIYCases", "GET https://api.dmca.com/listDIYCases — list DIY cases. Optional page.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        CancellationToken ct)
    {
        return await SafeGet("/listDIYCases", PageQuery(page), ct).ConfigureAwait(false);
    }

    [Function(nameof(ListComplianceCasesTool))]
    public async Task<string> ListComplianceCasesTool(
        [McpToolTrigger("listComplianceCases", "GET https://api.dmca.com/listComplianceCases — list compliance cases. Optional page.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        CancellationToken ct)
    {
        return await SafeGet("/listComplianceCases", PageQuery(page), ct).ConfigureAwait(false);
    }

    [Function(nameof(GetCaseByIdTool))]
    public async Task<string> GetCaseByIdTool(
        [McpToolTrigger("getCaseById", "GET https://api.dmca.com/getCaseById?id= — fetch one case by id.")]
            ToolInvocationContext context,
        [McpToolProperty("id", "Case ID.", isRequired: true)]
            string id,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "{\"error\":\"id is required\"}";
        }

        return await SafeGet("/getCaseById", new Dictionary<string, string?> { ["id"] = id }, ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string?>? PageQuery(double? page)
    {
        if (page is null) return null;
        return new Dictionary<string, string?> { ["page"] = ((int)page.Value).ToString() };
    }

    private async Task<string> SafeGet(string path, IDictionary<string, string?>? query, CancellationToken ct)
    {
        try
        {
            return await _client.GetRawAsync(path, query, ct).ConfigureAwait(false);
        }
        catch (DmcaApiException ex)
        {
            _logger.LogWarning("DMCA API error for {Path}: HTTP {Status}", path, ex.StatusCode);
            return ex.ResponseBody;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling {Path}", path);
            return $"{{\"error\":{System.Text.Json.JsonSerializer.Serialize(ex.Message)}}}";
        }
    }
}
