# DmcaCasesMcp.Functions

.NET 8 Isolated Azure Functions host for the DMCA Cases MCP (list/get plus login, createCase, createDIYCase, createComplianceCase, updateCase, getSiteReport).

## Config

- No DMCA token app setting. Every caller uses their own token.

## Tokens

Each tool call uses the caller's own token: the `token` argument, else the token from this MCP
session's `login` (in-memory cache keyed by the MCP session id, 12 h TTL, max 1000 entries, per
instance). With neither, the tool returns `{"error": "Not logged in. ...", "status": 401}`. The HTTP mirrors
take `token` (query/body) or the `X-DMCA-Token` / `Token` header. With more than one instance a
session can land on an instance that did not see its `login`; the tool then says "Not logged in" and
the caller should pass `token` (or log in again).

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
