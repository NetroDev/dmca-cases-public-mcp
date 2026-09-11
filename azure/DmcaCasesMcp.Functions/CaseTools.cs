using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// MCP tool triggers — exposed at /runtime/webhooks/mcp (Streamable HTTP)
/// via Microsoft.Azure.Functions.Worker.Extensions.Mcp.
/// Passwords and tokens are never logged.
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

    [Function(nameof(LoginTool))]
    public async Task<string> LoginTool(
        [McpToolTrigger("login", "POST https://api.dmca.com/login — email/password, no Token header. Password never logged.")]
            ToolInvocationContext context,
        [McpToolProperty("email", "DMCA.com account email.", isRequired: true)]
            string email,
        [McpToolProperty("password", "DMCA.com account password. Never logged.", isRequired: true)]
            string password,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return "{\"error\":\"email and password are required\"}";
        }

        return await SafePost("/login", new { email, password }, withToken: false, ct).ConfigureAwait(false);
    }

    [Function(nameof(CreateCaseTool))]
    public async Task<string> CreateCaseTool(
        [McpToolTrigger("createCase", "POST https://api.dmca.com/createCase — create managed takedown case. Requires Token.")]
            ToolInvocationContext context,
        [McpToolProperty("subject", "Case subject.", isRequired: true)]
            string subject,
        [McpToolProperty("description", "Case description.", isRequired: true)]
            string description,
        [McpToolProperty("copiedFromUrl", "Optional original / copied-from URL.", isRequired: false)]
            string? copiedFromUrl,
        [McpToolProperty("infringingUrl", "Optional infringing URL.", isRequired: false)]
            string? infringingUrl,
        [McpToolProperty("infringingSiteIp", "Optional infringing site IP.", isRequired: false)]
            string? infringingSiteIp,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return "{\"error\":\"subject and description are required\"}";
        }

        var payload = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["description"] = description,
        };
        if (!string.IsNullOrWhiteSpace(copiedFromUrl)) payload["copiedFromUrl"] = copiedFromUrl;
        if (!string.IsNullOrWhiteSpace(infringingUrl)) payload["infringingUrl"] = infringingUrl;
        if (!string.IsNullOrWhiteSpace(infringingSiteIp)) payload["infringingSiteIp"] = infringingSiteIp;

        return await SafePost("/createCase", payload, withToken: true, ct).ConfigureAwait(false);
    }

    [Function(nameof(UpdateCaseTool))]
    public async Task<string> UpdateCaseTool(
        [McpToolTrigger("updateCase", "POST https://api.dmca.com/updateCase — update managed takedown case. Requires Token.")]
            ToolInvocationContext context,
        [McpToolProperty("case_id", "Case ID to update.", isRequired: true)]
            string case_id,
        [McpToolProperty("status", "Case status.", isRequired: true)]
            string status,
        [McpToolProperty("subject", "Case subject.", isRequired: true)]
            string subject,
        [McpToolProperty("description", "Case description.", isRequired: true)]
            string description,
        [McpToolProperty("copiedFromUrl", "Optional original / copied-from URL.", isRequired: false)]
            string? copiedFromUrl,
        [McpToolProperty("infringingUrl", "Optional infringing URL.", isRequired: false)]
            string? infringingUrl,
        [McpToolProperty("infringingSiteIp", "Optional infringing site IP.", isRequired: false)]
            string? infringingSiteIp,
        [McpToolProperty("priority", "Optional priority.", isRequired: false)]
            string? priority,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(case_id) || string.IsNullOrWhiteSpace(status)
            || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return "{\"error\":\"case_id, status, subject, and description are required\"}";
        }

        var payload = new Dictionary<string, object?>
        {
            ["case_id"] = case_id,
            ["status"] = status,
            ["subject"] = subject,
            ["description"] = description,
        };
        if (!string.IsNullOrWhiteSpace(copiedFromUrl)) payload["copiedFromUrl"] = copiedFromUrl;
        if (!string.IsNullOrWhiteSpace(infringingUrl)) payload["infringingUrl"] = infringingUrl;
        if (!string.IsNullOrWhiteSpace(infringingSiteIp)) payload["infringingSiteIp"] = infringingSiteIp;
        if (!string.IsNullOrWhiteSpace(priority)) payload["priority"] = priority;

        return await SafePost("/updateCase", payload, withToken: true, ct).ConfigureAwait(false);
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

    private async Task<string> SafePost(string path, object payload, bool withToken, CancellationToken ct)
    {
        try
        {
            return await _client.PostRawAsync(path, payload, withToken, ct).ConfigureAwait(false);
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
