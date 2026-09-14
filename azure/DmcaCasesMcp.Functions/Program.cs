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

builder
    .ConfigureMcpTool("login")
    .WithProperty("email", "string", "DMCA.com account email.", required: true)
    .WithProperty("password", "string", "DMCA.com account password. Never logged.", required: true);

builder
    .ConfigureMcpTool("createCase")
    .WithProperty("subject", "string", "Case subject.", required: true)
    .WithProperty("description", "string", "Case description.", required: true)
    .WithProperty("copiedFromUrl", "string", "Optional original / copied-from URL.", required: false)
    .WithProperty("infringingUrl", "string", "Optional infringing URL.", required: false)
    .WithProperty("infringingSiteIp", "string", "Optional infringing site IP.", required: false);

builder
    .ConfigureMcpTool("updateCase")
    .WithProperty("case_id", "string", "Case ID to update.", required: true)
    .WithProperty("status", "string", "Case status.", required: true)
    .WithProperty("subject", "string", "Case subject.", required: true)
    .WithProperty("description", "string", "Case description.", required: true)
    .WithProperty("copiedFromUrl", "string", "Optional original / copied-from URL.", required: false)
    .WithProperty("infringingUrl", "string", "Optional infringing URL.", required: false)
    .WithProperty("infringingSiteIp", "string", "Optional infringing site IP.", required: false)
    .WithProperty("priority", "string", "Optional priority.", required: false);


builder
    .ConfigureMcpTool("createDIYCase")
    .WithProperty("subject", "string", "Case subject.", required: true)
    .WithProperty("description", "string", "Case description.", required: true)
    .WithProperty("type", "string", "DIY case type: Business - General | Personal - General | Toolkit Business Request CAN|EU|India | Toolkit Personal Request CAN|EU|India.", required: true)
    .WithProperty("copiedFromUrl", "string", "Optional original / copied-from URL.", required: false)
    .WithProperty("infringingUrl", "string", "Optional infringing URL.", required: false)
    .WithProperty("infringingSiteIp", "string", "Optional infringing site IP.", required: false);

builder
    .ConfigureMcpTool("createComplianceCase")
    .WithProperty("submitterEmail", "string", "Submitter email.", required: true)
    .WithProperty("submitterFirstName", "string", "Submitter first name.", required: true)
    .WithProperty("submitterLastName", "string", "Submitter last name.", required: true)
    .WithProperty("description", "string", "Case description.", required: true)
    .WithProperty("siteId", "string", "Id of the site the case is submitted to (site owner must have feature enabled).", required: true)
    .WithProperty("submitterCompanyName", "string", "Optional submitter company name.", required: false)
    .WithProperty("copiedFromUrl", "string", "Optional original / copied-from URL.", required: false)
    .WithProperty("infringingUrl", "string", "Optional infringing URL.", required: false)
    .WithProperty("infringingSiteIp", "string", "Optional infringing site IP.", required: false);

builder
    .ConfigureMcpTool("getSiteReport")
    .WithProperty("domain", "string", "Fully qualified domain name (upstream path /getSiteReport/{domain}).", required: true);

builder.Build().Run();
