#!/usr/bin/env node
/**
 * DMCA Cases public MCP (stdio) — read-only v1.
 * Tools map 1:1 to documented GET endpoints on https://api.dmca.com.
 * No create/update/login/register tools.
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { dmcaGet } from "./dmcaClient.js";

const SERVER_NAME = "dmca-cases";
const SERVER_VERSION = "1.0.0";

function jsonResult(data: unknown) {
  return {
    content: [
      {
        type: "text" as const,
        text: JSON.stringify(data, null, 2),
      },
    ],
  };
}

function errorResult(err: unknown) {
  const e = err as Error & { status?: number; body?: unknown };
  const payload = {
    error: e.message || String(err),
    status: e.status,
    body: e.body,
  };
  return {
    content: [
      {
        type: "text" as const,
        text: JSON.stringify(payload, null, 2),
      },
    ],
    isError: true as const,
  };
}

function createServer(): McpServer {
  const server = new McpServer({
    name: SERVER_NAME,
    version: SERVER_VERSION,
  });

  server.tool(
    "listCases",
    "GET https://api.dmca.com/listCases — list managed takedown cases for the account. Optional page (max 50 per page).",
    {
      page: z
        .number()
        .int()
        .positive()
        .optional()
        .describe("Optional page number for paging (max 50 results per page)."),
    },
    async ({ page }) => {
      try {
        const data = await dmcaGet("/listCases", { page });
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "listDIYCases",
    "GET https://api.dmca.com/listDIYCases — list DIY cases for the account. Optional page (max 50 per page).",
    {
      page: z
        .number()
        .int()
        .positive()
        .optional()
        .describe("Optional page number for paging (max 50 results per page)."),
    },
    async ({ page }) => {
      try {
        const data = await dmcaGet("/listDIYCases", { page });
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "listComplianceCases",
    "GET https://api.dmca.com/listComplianceCases — list compliance cases for the account. Optional page (max 50 per page).",
    {
      page: z
        .number()
        .int()
        .positive()
        .optional()
        .describe("Optional page number for paging (max 50 results per page)."),
    },
    async ({ page }) => {
      try {
        const data = await dmcaGet("/listComplianceCases", { page });
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "getCaseById",
    "GET https://api.dmca.com/getCaseById?id= — fetch a single case owned by the API account.",
    {
      id: z
        .string()
        .min(1)
        .describe("Case ID returned by listCases / createCase (or equivalent)."),
    },
    async ({ id }) => {
      try {
        const data = await dmcaGet("/getCaseById", { id });
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  return server;
}

async function main(): Promise<void> {
  // Never write protocol traffic to stdout; use stderr for diagnostics only.
  // Do not log tokens or env secret values.
  const server = createServer();
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  console.error("Fatal:", err instanceof Error ? err.message : String(err));
  process.exit(1);
});
