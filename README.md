# dmca-cases-public-mcp

Public MCP for [DMCA.com](https://www.dmca.com) cases (company of record: DMCA.com).

Maintained as **DMCA MCP Cases**.

## Tools

| Tool | Upstream |
|------|----------|
| `login` | `POST https://api.dmca.com/login` |
| `listCases` | `GET https://api.dmca.com/listCases` |
| `listDIYCases` | `GET https://api.dmca.com/listDIYCases` |
| `listComplianceCases` | `GET https://api.dmca.com/listComplianceCases` |
| `getCaseById` | `GET https://api.dmca.com/getCaseById?id=` |
| `createCase` | `POST https://api.dmca.com/createCase` |
| `createDIYCase` | `POST https://api.dmca.com/createDIYCase` |
| `createComplianceCase` | `POST https://api.dmca.com/createComplianceCase` |
| `updateCase` | `POST https://api.dmca.com/updateCase` |
| `getSiteReport` | `GET https://api.dmca.com/getSiteReport/{domain}` |

**Not** exposed: `register`, badge/protected-item APIs, XARF.

Auth: env `DMCA_API_TOKEN` (or `DMCA_TOKEN`) sent as HTTP header `Token`. `login` can mint a token. Never logged. Response bodies are returned as upstream JSON — no invented schemas or status enums.

`createCase`, `createDIYCase`, `createComplianceCase`, and `updateCase` require a valid Token (`DMCA_API_TOKEN`). `getSiteReport` also sends Token.

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

Live Function App:

`https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net`

Cover: `/cover` · Project: `azure/DmcaCasesMcp.Functions` (.NET 8 Isolated, code deploy — not container).

- MCP (Functions MCP extension): `/runtime/webhooks/mcp` (and SSE sibling `/runtime/webhooks/mcp/sse`)
- Info pointer: `GET /api/mcp`
- HTTP mirrors: `/api/listCases`, `/api/listDIYCases`, `/api/listComplianceCases`, `/api/getCaseById`, `/api/login`, `/api/createCase`, `/api/updateCase`, `/api/createDIYCase`, `/api/createComplianceCase`, `/api/getSiteReport?domain=` (upstream path `/getSiteReport/{domain}`)

App setting: `DMCA_API_TOKEN`.

Deploy (after `dotnet` + Azure Functions Core Tools are available):

```bash
cd azure/DmcaCasesMcp.Functions
func azure functionapp publish dmca-cases-public-mcp --dotnet-isolated
```

(Use the exact Function App resource name in your subscription if it differs.)

Prefer **Flex Consumption**.

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
