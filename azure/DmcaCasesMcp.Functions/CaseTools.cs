using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// MCP tool triggers — exposed at /runtime/webhooks/mcp (Streamable HTTP)
/// via Microsoft.Azure.Functions.Worker.Extensions.Mcp.
/// Every tool that calls the DMCA API resolves its token per call (see <see cref="DmcaTokenResolver"/>):
/// `token` argument, then this MCP session's login, then an X-DMCA-Token header, then DMCA_API_TOKEN.
/// Passwords and tokens are never logged.
/// </summary>
public sealed class CaseTools
{
    internal const string TokenDescription = "DMCA API token from the login tool; optional if the server/session already has one.";

    private readonly DmcaOperations _ops;
    private readonly DmcaTokenResolver _tokens;
    private readonly ILogger<CaseTools> _logger;

    public CaseTools(DmcaOperations ops, DmcaTokenResolver tokens, ILogger<CaseTools> logger)
    {
        _ops = ops;
        _tokens = tokens;
        _logger = logger;
    }

    [Function(nameof(ListCasesTool))]
    public async Task<string> ListCasesTool(
        [McpToolTrigger("listCases", "GET https://api.dmca.com/listCases — list managed takedown cases. Optional page.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
        => Finish(await _ops.ListAsync("/listCases", Page(page), _tokens.Resolve(context, token), ct).ConfigureAwait(false));

    [Function(nameof(ListDiyCasesTool))]
    public async Task<string> ListDiyCasesTool(
        [McpToolTrigger("listDIYCases", "GET https://api.dmca.com/listDIYCases — list DIY cases. Optional page. An empty account comes back as an empty list.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
        => Finish(await _ops.ListAsync("/listDIYCases", Page(page), _tokens.Resolve(context, token), ct).ConfigureAwait(false));

    [Function(nameof(ListComplianceCasesTool))]
    public async Task<string> ListComplianceCasesTool(
        [McpToolTrigger("listComplianceCases", "GET https://api.dmca.com/listComplianceCases — list compliance cases. Optional page.")]
            ToolInvocationContext context,
        [McpToolProperty("page", "Optional page number (max 50 per page).", isRequired: false)]
            double? page,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
        => Finish(await _ops.ListAsync("/listComplianceCases", Page(page), _tokens.Resolve(context, token), ct).ConfigureAwait(false));

    [Function(nameof(GetCaseByIdTool))]
    public async Task<string> GetCaseByIdTool(
        [McpToolTrigger("getCaseById", "GET https://api.dmca.com/getCaseById?id= — fetch one case by id.")]
            ToolInvocationContext context,
        [McpToolProperty("id", "Case ID.", isRequired: true)]
            string id,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        id = FromArguments(context, "id", id);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Finish(OpResult.Error(400, "id is required"));
        }

        var query = new Dictionary<string, string?> { ["id"] = id };
        return Finish(await _ops.GetAsync("/getCaseById", query, _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    [Function(nameof(LoginTool))]
    public async Task<string> LoginTool(
        [McpToolTrigger("login", "POST https://api.dmca.com/login — email/password, no Token header. Returns the DMCA API token; it is reused automatically for the rest of this MCP session, or pass it as `token`. Password never logged.")]
            ToolInvocationContext context,
        [McpToolProperty("email", "DMCA.com account email.", isRequired: true)]
            string email,
        [McpToolProperty("password", "DMCA.com account password. Never logged.", isRequired: true)]
            string password,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Finish(OpResult.Error(400, "email and password are required"));
        }

        var (result, token) = await _ops.LoginAsync(email, password, ct).ConfigureAwait(false);
        if (token is null)
        {
            return Finish(result);
        }

        var cached = _tokens.RememberForSession(context, token);
        _logger.LogInformation("Login succeeded; token cached for MCP session: {Cached}", cached);

        var usage = cached
            ? "This token will be used automatically for the rest of this MCP session (kept in server memory for up to 12 hours). "
              + "You can also pass it as the `token` argument on any tool call; an explicit `token` always wins. "
              + "If a later call fails with HTTP 401, pass the token explicitly."
            : "This server could not identify an MCP session for this connection, so the token was not cached. "
              + "Pass it as the `token` argument on each tool call.";
        return DmcaOperations.LoginResultJson(token, cached, usage);
    }

    [Function(nameof(CreateCaseTool))]
    public async Task<string> CreateCaseTool(
        [McpToolTrigger("createCase", "POST https://api.dmca.com/createCase — create managed takedown case. Uses the session/`token` DMCA API token.")]
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
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return Finish(OpResult.Error(400, "subject and description are required"));
        }

        var payload = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["description"] = description,
        };
        AddIfSet(payload, "copiedFromUrl", copiedFromUrl);
        AddIfSet(payload, "infringingUrl", infringingUrl);
        AddIfSet(payload, "infringingSiteIp", infringingSiteIp);

        return Finish(await _ops.PostAsync("/createCase", payload, _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    [Function(nameof(UpdateCaseTool))]
    public async Task<string> UpdateCaseTool(
        [McpToolTrigger("updateCase", "POST https://api.dmca.com/updateCase — update managed takedown case. Status and priority are kept as they are unless you pass them. Uses the session/`token` DMCA API token.")]
            ToolInvocationContext context,
        [McpToolProperty("case_id", "Case ID to update.", isRequired: true)]
            string case_id,
        [McpToolProperty("subject", "Case subject.", isRequired: true)]
            string subject,
        [McpToolProperty("description", "Case description.", isRequired: true)]
            string description,
        [McpToolProperty("status", "Optional new case status. Left unchanged when omitted.", isRequired: false)]
            string? status,
        [McpToolProperty("copiedFromUrl", "Optional original / copied-from URL.", isRequired: false)]
            string? copiedFromUrl,
        [McpToolProperty("infringingUrl", "Optional infringing URL.", isRequired: false)]
            string? infringingUrl,
        [McpToolProperty("infringingSiteIp", "Optional infringing site IP.", isRequired: false)]
            string? infringingSiteIp,
        [McpToolProperty("priority", "Optional new priority. Left unchanged when omitted.", isRequired: false)]
            string? priority,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        case_id = FromArguments(context, "case_id", case_id);
        if (string.IsNullOrWhiteSpace(case_id) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description))
        {
            return Finish(OpResult.Error(400, "case_id, subject, and description are required"));
        }

        return Finish(await _ops.UpdateCaseAsync(
            case_id, status, priority, subject, description, copiedFromUrl, infringingUrl, infringingSiteIp,
            _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    [Function(nameof(CreateDiyCaseTool))]
    public async Task<string> CreateDiyCaseTool(
        [McpToolTrigger("createDIYCase", "POST https://api.dmca.com/createDIYCase — create DIY case. Uses the session/`token` DMCA API token.")]
            ToolInvocationContext context,
        [McpToolProperty("subject", "Case subject.", isRequired: true)]
            string subject,
        [McpToolProperty("description", "Case description.", isRequired: true)]
            string description,
        [McpToolProperty("type", "DIY case type enum (e.g. Business - General, Personal - General, Toolkit Business/Personal Request CAN|EU|India).", isRequired: true)]
            string type,
        [McpToolProperty("copiedFromUrl", "Optional original / copied-from URL.", isRequired: false)]
            string? copiedFromUrl,
        [McpToolProperty("infringingUrl", "Optional infringing URL.", isRequired: false)]
            string? infringingUrl,
        [McpToolProperty("infringingSiteIp", "Optional infringing site IP.", isRequired: false)]
            string? infringingSiteIp,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(type))
        {
            return Finish(OpResult.Error(400, "subject, description, and type are required"));
        }

        var payload = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["description"] = description,
            ["type"] = type,
        };
        AddIfSet(payload, "copiedFromUrl", copiedFromUrl);
        AddIfSet(payload, "infringingUrl", infringingUrl);
        AddIfSet(payload, "infringingSiteIp", infringingSiteIp);

        return Finish(await _ops.PostAsync("/createDIYCase", payload, _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    [Function(nameof(CreateComplianceCaseTool))]
    public async Task<string> CreateComplianceCaseTool(
        [McpToolTrigger("createComplianceCase", "POST https://api.dmca.com/createComplianceCase — create compliance case. siteId site owner must have feature enabled. Uses the session/`token` DMCA API token.")]
            ToolInvocationContext context,
        [McpToolProperty("submitterEmail", "Submitter email.", isRequired: true)]
            string submitterEmail,
        [McpToolProperty("submitterFirstName", "Submitter first name.", isRequired: true)]
            string submitterFirstName,
        [McpToolProperty("submitterLastName", "Submitter last name.", isRequired: true)]
            string submitterLastName,
        [McpToolProperty("description", "Case description.", isRequired: true)]
            string description,
        [McpToolProperty("siteId", "Id of the site the case is submitted to (site owner must have feature enabled).", isRequired: true)]
            string siteId,
        [McpToolProperty("submitterCompanyName", "Optional submitter company name.", isRequired: false)]
            string? submitterCompanyName,
        [McpToolProperty("copiedFromUrl", "Optional original / copied-from URL.", isRequired: false)]
            string? copiedFromUrl,
        [McpToolProperty("infringingUrl", "Optional infringing URL.", isRequired: false)]
            string? infringingUrl,
        [McpToolProperty("infringingSiteIp", "Optional infringing site IP.", isRequired: false)]
            string? infringingSiteIp,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        siteId = FromArguments(context, "siteId", siteId);
        if (string.IsNullOrWhiteSpace(submitterEmail) || string.IsNullOrWhiteSpace(submitterFirstName)
            || string.IsNullOrWhiteSpace(submitterLastName) || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(siteId))
        {
            return Finish(OpResult.Error(400, "submitterEmail, submitterFirstName, submitterLastName, description, and siteId are required"));
        }

        var payload = new Dictionary<string, object?>
        {
            ["submitterEmail"] = submitterEmail,
            ["submitterFirstName"] = submitterFirstName,
            ["submitterLastName"] = submitterLastName,
            ["description"] = description,
            ["siteId"] = siteId,
        };
        AddIfSet(payload, "submitterCompanyName", submitterCompanyName);
        AddIfSet(payload, "copiedFromUrl", copiedFromUrl);
        AddIfSet(payload, "infringingUrl", infringingUrl);
        AddIfSet(payload, "infringingSiteIp", infringingSiteIp);

        return Finish(await _ops.PostAsync("/createComplianceCase", payload, _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    [Function(nameof(GetSiteReportTool))]
    public async Task<string> GetSiteReportTool(
        [McpToolTrigger("getSiteReport", "GET https://api.dmca.com/getSiteReport/{domain} — site report for a fully qualified domain name. Uses the session/`token` DMCA API token.")]
            ToolInvocationContext context,
        [McpToolProperty("domain", "Fully qualified domain name (path segment upstream).", isRequired: true)]
            string domain,
        [McpToolProperty("token", TokenDescription, isRequired: false)]
            string? token,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return Finish(OpResult.Error(400, "domain is required"));
        }

        var path = "/getSiteReport/" + Uri.EscapeDataString(domain.Trim());
        return Finish(await _ops.GetAsync(path, null, _tokens.Resolve(context, token), ct).ConfigureAwait(false));
    }

    private static int? Page(double? page) => page is null ? null : (int)page.Value;

    /// <summary>
    /// The MCP worker extension (1.0.0) fails to bind a string tool property whose value looks like a
    /// GUID — the parameter arrives null — so case ids are read from the raw tool arguments instead.
    /// </summary>
    private static string? FromArguments(ToolInvocationContext context, string name, string? bound)
    {
        if (!string.IsNullOrWhiteSpace(bound)) return bound;
        if (context.Arguments is null || !context.Arguments.TryGetValue(name, out var raw) || raw is null) return bound;
        return raw switch
        {
            string text => text,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } el => el.GetString(),
            _ => raw.ToString(),
        };
    }

    private static void AddIfSet(Dictionary<string, object?> payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) payload[key] = value;
    }

    private static string Finish(OpResult result) => result.Json;
}
