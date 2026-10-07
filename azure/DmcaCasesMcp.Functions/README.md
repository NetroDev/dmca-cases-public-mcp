# DmcaCasesMcp.Functions

.NET 8 Isolated Azure Functions host for the DMCA Cases MCP (list/get plus login, createCase, createDIYCase, createComplianceCase, updateCase, getSiteReport).

## Config

- `DMCA_API_TOKEN` — app setting / Key Vault reference. Fallback `Token` header for `https://api.dmca.com`
  when the caller has not logged in or passed a token.

## Tokens

Each tool call picks its token in this order: the `token` argument, the token from this MCP session's
`login` (in-memory cache keyed by the MCP session id, 12 h TTL, max 1000 entries, per instance), an
`X-DMCA-Token` request header, then `DMCA_API_TOKEN`. The HTTP mirrors take `token` (query/body),
`X-DMCA-Token` or `Token` headers, then `DMCA_API_TOKEN`. With more than one instance a session can land
on an instance that did not see its `login`; callers that need a guaranteed account should pass `token`.

## Endpoints

- MCP Streamable HTTP: `/runtime/webhooks/mcp` (Microsoft.Azure.Functions.Worker.Extensions.Mcp)
- MCP SSE: `/runtime/webhooks/mcp/sse`
- Info: `GET /api/mcp`
- Cover (HTML): `GET /` (root) and `GET /cover` (alias)
- HTTP mirrors: `/api/listCases`, `/api/listDIYCases`, `/api/listComplianceCases`, `/api/getCaseById`, `/api/login`, `/api/createCase`, `/api/updateCase`, `/api/createDIYCase`, `/api/createComplianceCase`, `/api/getSiteReport?domain=` (calls upstream `/getSiteReport/{domain}`)

## Root (`/`) routing

`host.json` sets `extensions.http.routePrefix` to `""` and `HomePage` uses
`Route = "{rootpage:regex(^$)?}"` (optional segment that must be empty => exactly `/`).

- Do not use `Route = ""`: the host maps it to `/HomePage` (function name) while the worker maps `/`,
  so the platform default "Your Azure Function App is up and running" page answers `/`.
- Do not use `Route = "/"`: the worker builds the pattern `//`, fails to parse it, and every HTTP
  function returns 500.
- `AzureWebJobsDisableHomepage=true` only changes the unmatched-root fallback to 204.

The MCP protocol itself cannot move to `/`: the Microsoft MCP extension serves it only at the
host-reserved `/runtime/webhooks/mcp` path.

## Deploy (code, not container)

Canonical public host:

`https://mcp.dmca.com`

Default Azure hostname:

`https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net`

```bash
dotnet build -c Release
func azure functionapp publish <FUNCTION_APP_NAME> --dotnet-isolated
```

Prefer Flex Consumption.
