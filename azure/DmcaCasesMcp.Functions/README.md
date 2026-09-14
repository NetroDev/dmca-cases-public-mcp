# DmcaCasesMcp.Functions

.NET 8 Isolated Azure Functions host for the DMCA Cases MCP (list/get plus login, createCase, createDIYCase, createComplianceCase, updateCase, getSiteReport).

## Config

- `DMCA_API_TOKEN` — app setting / Key Vault reference. Sent as `Token` header to `https://api.dmca.com`.

## Endpoints

- MCP Streamable HTTP: `/runtime/webhooks/mcp` (Microsoft.Azure.Functions.Worker.Extensions.Mcp)
- MCP SSE: `/runtime/webhooks/mcp/sse`
- Info: `GET /api/mcp`
- Cover: `GET /cover`
- HTTP mirrors: `/api/listCases`, `/api/listDIYCases`, `/api/listComplianceCases`, `/api/getCaseById`, `/api/login`, `/api/createCase`, `/api/updateCase`, `/api/createDIYCase`, `/api/createComplianceCase`, `/api/getSiteReport?domain=` (calls upstream `/getSiteReport/{domain}`)

## Deploy (code, not container)

Target app hostname:

`https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net`

```bash
dotnet build -c Release
func azure functionapp publish <FUNCTION_APP_NAME> --dotnet-isolated
```

Prefer Flex Consumption.
