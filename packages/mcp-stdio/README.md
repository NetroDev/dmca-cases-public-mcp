# `dmca-cases-mcp`

stdio MCP server for DMCA.com case **list**, **get**, **login**, **createCase**, **createDIYCase**, **createComplianceCase**, **updateCase**, and **getSiteReport**.

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

Every tool except `login` sends a DMCA API token as the `Token` header: the tool's optional `token` argument if given, otherwise the token from this session's `login`, otherwise `DMCA_API_TOKEN`. `updateCase` keeps status and priority unless you pass them.

## Env

- `DMCA_API_TOKEN` (preferred) or `DMCA_TOKEN` — optional fallback token, sent as HTTP header `Token` when the session has not logged in and no `token` argument is given. Never logged.

## Run

```bash
npm install
npm run build
DMCA_API_TOKEN=your_token node dist/index.js
```

Or via npx after publish:

```bash
npx -y dmca-cases-mcp
```

## Cursor / Claude Desktop

```json
{
  "mcpServers": {
    "dmca-cases": {
      "command": "npx",
      "args": ["-y", "dmca-cases-mcp"],
      "env": {
        "DMCA_API_TOKEN": "YOUR_TOKEN"
      }
    }
  }
}
```

Responses are returned as the upstream JSON body (stringified), without invented schemas.
