using DmcaCasesMcp.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddHttpClient<DmcaApiClient>();

// Optional: declare MCP tool property metadata for tools that use ConfigureMcpTool.
builder
    .ConfigureMcpTool("listCases")
    .WithProperty("page", "number", "Optional page number (max 50 results per page).", required: false);

builder
    .ConfigureMcpTool("listDIYCases")
    .WithProperty("page", "number", "Optional page number (max 50 results per page).", required: false);

builder
    .ConfigureMcpTool("listComplianceCases")
    .WithProperty("page", "number", "Optional page number (max 50 results per page).", required: false);

builder
    .ConfigureMcpTool("getCaseById")
    .WithProperty("id", "string", "Case ID returned by listCases (or equivalent).", required: true);

builder.Build().Run();
