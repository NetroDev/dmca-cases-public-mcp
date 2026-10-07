using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// Usage counting for MCP tools: emits one Application Insights custom event, <c>McpToolCall</c>, per
/// tool invocation (customEvents table). Recorded: tool name, outcome (ok / auth_error / error / exception),
/// the HTTP-style status the tool returned, the caller's User-Agent (truncated), a one-way hash of the MCP
/// session id (for distinct-session counts) and the duration.
/// Never recorded: tool arguments, tokens, passwords, emails, case content, client IPs.
/// Non-tool functions (landing page, static files, /api mirrors) pass straight through.
/// Note: the MCP initialize clientInfo (e.g. "glama 1.0.0") is not available to the worker; the host
/// logs it in traces ("Client (name version)") at initialize, and the extension does not persist it.
/// </summary>
public sealed class McpUsageMiddleware : IFunctionsWorkerMiddleware
{
    public const string EventName = "McpToolCall";

    private sealed class Outcome
    {
        public int? Status;
    }

    private static readonly AsyncLocal<Outcome?> Current = new();

    /// <summary>Called by tools (CaseTools.Finish) with the status of the result they return.</summary>
    internal static void Record(int status)
    {
        if (Current.Value is { } outcome) outcome.Status = status;
    }

    private readonly TelemetryClient _telemetry;

    public McpUsageMiddleware(TelemetryClient telemetry)
    {
        _telemetry = telemetry;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var trigger = context.FunctionDefinition.InputBindings.Values
            .FirstOrDefault(b => string.Equals(b.Type, "mcpToolTrigger", StringComparison.OrdinalIgnoreCase));
        if (trigger is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var outcome = new Outcome();
        Current.Value = outcome;
        var sw = Stopwatch.StartNew();
        var threw = false;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch
        {
            threw = true;
            throw;
        }
        finally
        {
            sw.Stop();
            Current.Value = null;
            try
            {
                await TrackAsync(context, trigger, outcome.Status, threw, sw.Elapsed.TotalMilliseconds).ConfigureAwait(false);
            }
            catch
            {
                // Telemetry must never break a tool call.
            }
        }
    }

    private async Task TrackAsync(FunctionContext context, BindingMetadata trigger, int? status, bool threw, double durationMs)
    {
        ToolInvocationContext? tool = null;
        try
        {
            tool = (await context.BindInputAsync<ToolInvocationContext>(trigger).ConfigureAwait(false)).Value;
        }
        catch
        {
            // Fall back to the function name below.
        }

        var evt = new EventTelemetry(EventName);
        evt.Properties["tool"] = tool?.Name ?? context.FunctionDefinition.Name;
        evt.Properties["outcome"] = threw ? "exception"
            : status is null ? "unknown"
            : status < 400 ? "ok"
            : status is 401 or 403 ? "auth_error"
            : "error";
        if (status is not null) evt.Properties["status"] = status.Value.ToString();

        if (tool?.Transport is HttpTransport http
            && http.Headers.TryGetValue("User-Agent", out var ua)
            && !string.IsNullOrWhiteSpace(ua))
        {
            evt.Properties["clientUserAgent"] = ua.Length > 120 ? ua[..120] : ua;
        }

        var sessionId = DmcaTokenResolver.GetSessionId(tool);
        if (sessionId is not null)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
            evt.Properties["sessionHash"] = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        evt.Metrics["durationMs"] = durationMs;
        _telemetry.TrackEvent(evt);
    }
}
