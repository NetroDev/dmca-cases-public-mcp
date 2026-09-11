# dmca-cases-public-mcp

Public **read-only** MCP for [DMCA.com](https://www.dmca.com) cases (company of record: DMCA.com).

Maintained as **DMCA MCP Cases**.

## Policy (v1)

Exposes **list/get only**:

| Tool | Upstream |
|------|----------|
| `listCases` | `GET https://api.dmca.com/listCases` |
| `listDIYCases` | `GET https://api.dmca.com/listDIYCases` |
| `listComplianceCases` | `GET https://api.dmca.com/listComplianceCases` |
| `getCaseById` | `GET https://api.dmca.com/getCaseById?id=` |

**Not** exposed: `createCase`, `updateCase`, `createDIYCase`, `createComplianceCase`, `register`, `login`, badge/protected-item APIs.

Auth: env `DMCA_API_TOKEN` (or `DMCA_TOKEN`) sent as HTTP header `Token`. Never logged. Response bodies are returned as upstream JSON — no invented schemas or status enums.

Canonical docs: [www.dmca.com/api](https://www.dmca.com/api/) · OpenAPI [2.1.2](https://api.swaggerhub.com/apis/dmca/dmca-api/2.1.2) · host `https://api.dmca.com`.

## A) npm / stdio (local clients)

```bash
cd packages/mcp-stdio
npm install
npm run build
DMCA_API_TOKEN=your_token node dist/index.js
```

Cursor / Claude Desktop:

```json
{
  "mcpServers": {
    "dmca-cases": {
      "command": "npx",
      "args": ["-y", "@netrodev/dmca-cases-mcp"],
      "env": { "DMCA_API_TOKEN": "YOUR_TOKEN" }
    }
  }
}
```

Package name: `@netrodev/dmca-cases-mcp` · registry `mcpName`: `io.github.NetroDev/dmca-cases`.

## B) Azure Functions (.NET Isolated)

Live Function App (waiting for deploy):

`https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net`

Project: `azure/DmcaCasesMcp.Functions` (.NET 8 Isolated, code deploy — not container).

- MCP (Functions MCP extension): `/runtime/webhooks/mcp` (and SSE sibling `/runtime/webhooks/mcp/sse`)
- Info pointer: `GET /api/mcp`
- HTTP mirrors: `/api/listCases`, `/api/listDIYCases`, `/api/listComplianceCases`, `/api/getCaseById`

App setting: `DMCA_API_TOKEN`.

Deploy (after `dotnet` + Azure Functions Core Tools are available):

```bash
cd azure/DmcaCasesMcp.Functions
func azure functionapp publish dmca-cases-public-mcp --dotnet-isolated
```

(Use the exact Function App resource name in your subscription if it differs.)

Prefer **Flex Consumption**. Classic Consumption also works for short list/get calls.

## Registry metadata

See `server.json` for official MCP Registry (`packages` + Azure `remotes`).

## Cursor plugin scaffolding

See `mcp.json` and `.cursor-plugin/plugin.json` for a future Marketplace submit.

## Auto-deploy (GitHub Actions → Azure)

Workflow: `.github/workflows/azure-functions-deploy.yml`

On push to `main` (paths under `azure/DmcaCasesMcp.Functions/**`), GitHub Actions builds the .NET 8 Isolated project and deploys to Function App **dmca-cases-public-mcp**.

Required repo secret:

1. Azure Portal → Function App `dmca-cases-public-mcp` → **Get publish profile**
2. GitHub → Settings → Secrets and variables → Actions → `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`

Also set Function App setting `DMCA_API_TOKEN` in Azure (not in GitHub) for live API calls.
