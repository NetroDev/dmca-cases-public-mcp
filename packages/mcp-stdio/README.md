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

Every tool except `login` sends your own DMCA API token as the `Token` header: the tool's optional `token` argument if given, otherwise the token from this session's `login`. There is no environment-variable token; without login or `token`, tools fail with "Not logged in. Call the login tool with your DMCA.com email and password first, or pass token." `updateCase` keeps status and priority unless you pass them. Tokens and passwords are never logged.

## Run

```bash
npm install
npm run build
node dist/index.js
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
      "args": ["-y", "dmca-cases-mcp"]
    }
  }
}
```

Responses are returned as the upstream JSON body (stringified), without invented schemas.
