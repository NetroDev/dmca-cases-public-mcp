# `@netrodev/dmca-cases-mcp`

stdio MCP server for DMCA.com case **list**, **get**, **login**, **createCase**, and **updateCase**.

## Tools

| Tool | Upstream |
|------|----------|
| `login` | `POST https://api.dmca.com/login` |
| `listCases` | `GET https://api.dmca.com/listCases` |
| `listDIYCases` | `GET https://api.dmca.com/listDIYCases` |
| `listComplianceCases` | `GET https://api.dmca.com/listComplianceCases` |
| `getCaseById` | `GET https://api.dmca.com/getCaseById?id=` |
| `createCase` | `POST https://api.dmca.com/createCase` |
| `updateCase` | `POST https://api.dmca.com/updateCase` |

`createCase` and `updateCase` require `DMCA_API_TOKEN` (Token header).

## Env

- `DMCA_API_TOKEN` (preferred) or `DMCA_TOKEN` — sent as HTTP header `Token`. Never logged.

## Run

```bash
npm install
npm run build
DMCA_API_TOKEN=your_token node dist/index.js
```

Or via npx after publish:

```bash
npx -y @netrodev/dmca-cases-mcp
```

## Cursor / Claude Desktop

```json
{
  "mcpServers": {
    "dmca-cases": {
      "command": "npx",
      "args": ["-y", "@netrodev/dmca-cases-mcp"],
      "env": {
        "DMCA_API_TOKEN": "YOUR_TOKEN"
      }
    }
  }
}
```

Responses are returned as the upstream JSON body (stringified), without invented schemas.
