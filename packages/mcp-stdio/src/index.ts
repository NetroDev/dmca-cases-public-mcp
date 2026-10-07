#!/usr/bin/env node
/**
 * DMCA Cases public MCP (stdio) — list/get + login/createCase/createDIYCase/createComplianceCase/updateCase/getSiteReport.
 * Tools map 1:1 to documented endpoints on https://api.dmca.com.
 * Passwords and tokens are never logged.
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import {
  dmcaGet,
  dmcaList,
  dmcaLogin,
  dmcaPost,
  forgetSessionToken,
  rememberSessionToken,
  resolveToken,
} from "./dmcaClient.js";

const SERVER_NAME = "dmca-cases";
const SERVER_VERSION = "1.2.0";

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

const tokenArg = z
  .string()
  .optional()
  .describe("DMCA API token from the login tool; optional if the server/session already has one.");

const pageArg = z
  .number()
  .int()
  .positive()
  .optional()
  .describe("Optional page number for paging (max 50 results per page).");

/** Current STATUS / PRIORITY of a case, read with the same token. */
async function currentStatusAndPriority(
  caseId: string,
  token: string
): Promise<{ status?: string; priority?: string }> {
  let data: unknown;
  try {
    data = await dmcaGet("/getCaseById", token, { id: caseId });
  } catch (err) {
    const e = err as Error;
    throw Object.assign(
      new Error(
        "Could not read the case's current status/priority (needed because the DMCA API clears them when they are not sent). " +
          `Pass status and priority explicitly, or check case_id. (${e.message})`
      ),
      { status: (err as { status?: number }).status, body: (err as { body?: unknown }).body }
    );
  }
  const row = (Array.isArray(data) ? data[0] : data) as Record<string, unknown> | undefined;
  const str = (v: unknown) => (typeof v === "string" && v ? v : undefined);
  return { status: str(row?.STATUS), priority: str(row?.PRIORITY) };
}

function createServer(): McpServer {
  const server = new McpServer({
    name: SERVER_NAME,
    version: SERVER_VERSION,
  });

  server.tool(
    "listCases",
    "GET https://api.dmca.com/listCases — list managed takedown cases for the account. Optional page (max 50 per page).",
    { page: pageArg, token: tokenArg },
    async ({ page, token }) => {
      try {
        const data = await dmcaList("/listCases", resolveToken(token).value, page);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "listDIYCases",
    "GET https://api.dmca.com/listDIYCases — list DIY cases for the account. Optional page (max 50 per page). An empty account comes back as an empty list.",
    { page: pageArg, token: tokenArg },
    async ({ page, token }) => {
      try {
        const data = await dmcaList("/listDIYCases", resolveToken(token).value, page);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "listComplianceCases",
    "GET https://api.dmca.com/listComplianceCases — list compliance cases for the account. Optional page (max 50 per page).",
    { page: pageArg, token: tokenArg },
    async ({ page, token }) => {
      try {
        const data = await dmcaList("/listComplianceCases", resolveToken(token).value, page);
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
      token: tokenArg,
    },
    async ({ id, token }) => {
      try {
        const data = await dmcaGet("/getCaseById", resolveToken(token).value, { id });
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "login",
    "POST https://api.dmca.com/login — authenticate with email/password (no Token header). Returns the DMCA API token; it is reused automatically for the rest of this session, or pass it as `token`. Password is never logged.",
    {
      email: z.string().min(1).describe("DMCA.com account email."),
      password: z.string().min(1).describe("DMCA.com account password. Never logged."),
    },
    async ({ email, password }) => {
      try {
        const token = await dmcaLogin(email, password);
        rememberSessionToken(token);
        return jsonResult({
          token,
          tokenUsage:
            "This token will be used automatically for the rest of this MCP session (kept in memory for up to 12 hours). " +
            "You can also pass it as the `token` argument on any tool call; an explicit `token` always wins.",
          sessionCached: true,
        });
      } catch (err) {
        // A failed login must not leave this session acting as whoever logged in before.
        forgetSessionToken();
        return errorResult(err);
      }
    }
  );

  server.tool(
    "createCase",
    "POST https://api.dmca.com/createCase — create a managed takedown case. Uses the session/`token` DMCA API token.",
    {
      subject: z.string().min(1).describe("Case subject."),
      description: z.string().min(1).describe("Case description."),
      copiedFromUrl: z
        .string()
        .optional()
        .describe("Optional original / copied-from URL."),
      infringingUrl: z
        .string()
        .optional()
        .describe("Optional infringing URL."),
      infringingSiteIp: z
        .string()
        .optional()
        .describe("Optional infringing site IP."),
      token: tokenArg,
    },
    async ({ subject, description, copiedFromUrl, infringingUrl, infringingSiteIp, token }) => {
      try {
        const payload: Record<string, unknown> = { subject, description };
        if (copiedFromUrl) payload.copiedFromUrl = copiedFromUrl;
        if (infringingUrl) payload.infringingUrl = infringingUrl;
        if (infringingSiteIp) payload.infringingSiteIp = infringingSiteIp;
        const data = await dmcaPost("/createCase", payload, resolveToken(token).value);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "updateCase",
    "POST https://api.dmca.com/updateCase — update an existing managed takedown case. Status and priority are kept as they are unless you pass them. Uses the session/`token` DMCA API token.",
    {
      case_id: z.string().min(1).describe("Case ID to update."),
      subject: z.string().min(1).describe("Case subject."),
      description: z.string().min(1).describe("Case description."),
      status: z
        .string()
        .optional()
        .describe("Optional new case status. Left unchanged when omitted."),
      copiedFromUrl: z
        .string()
        .optional()
        .describe("Optional original / copied-from URL."),
      infringingUrl: z
        .string()
        .optional()
        .describe("Optional infringing URL."),
      infringingSiteIp: z
        .string()
        .optional()
        .describe("Optional infringing site IP."),
      priority: z
        .string()
        .optional()
        .describe("Optional new priority. Left unchanged when omitted."),
      token: tokenArg,
    },
    async ({
      case_id,
      status,
      subject,
      description,
      copiedFromUrl,
      infringingUrl,
      infringingSiteIp,
      priority,
      token,
    }) => {
      try {
        const tokenValue = resolveToken(token).value;
        // The DMCA API overwrites status and priority with null when they are not sent,
        // so carry the current values over unless the caller asked to change them.
        if (!status || !priority) {
          const current = await currentStatusAndPriority(case_id, tokenValue);
          status = status || current.status;
          priority = priority || current.priority;
        }
        const payload: Record<string, unknown> = {
          case_id,
          subject,
          description,
        };
        if (status) payload.status = status;
        if (priority) payload.priority = priority;
        if (copiedFromUrl) payload.copiedFromUrl = copiedFromUrl;
        if (infringingUrl) payload.infringingUrl = infringingUrl;
        if (infringingSiteIp) payload.infringingSiteIp = infringingSiteIp;
        const data = await dmcaPost("/updateCase", payload, tokenValue);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "createDIYCase",
    "POST https://api.dmca.com/createDIYCase — create a DIY case. Uses the session/`token` DMCA API token.",
    {
      subject: z.string().min(1).describe("Case subject."),
      description: z.string().min(1).describe("Case description."),
      type: z
        .string()
        .min(1)
        .describe(
          "DIY case type: Business - General | Personal - General | Toolkit Business Request CAN|EU|India | Toolkit Personal Request CAN|EU|India."
        ),
      copiedFromUrl: z
        .string()
        .optional()
        .describe("Optional original / copied-from URL."),
      infringingUrl: z
        .string()
        .optional()
        .describe("Optional infringing URL."),
      infringingSiteIp: z
        .string()
        .optional()
        .describe("Optional infringing site IP."),
      token: tokenArg,
    },
    async ({ subject, description, type, copiedFromUrl, infringingUrl, infringingSiteIp, token }) => {
      try {
        const payload: Record<string, unknown> = { subject, description, type };
        if (copiedFromUrl) payload.copiedFromUrl = copiedFromUrl;
        if (infringingUrl) payload.infringingUrl = infringingUrl;
        if (infringingSiteIp) payload.infringingSiteIp = infringingSiteIp;
        const data = await dmcaPost("/createDIYCase", payload, resolveToken(token).value);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "createComplianceCase",
    "POST https://api.dmca.com/createComplianceCase — create a compliance case. siteId site owner must have feature enabled. Uses the session/`token` DMCA API token.",
    {
      submitterEmail: z.string().min(1).describe("Submitter email."),
      submitterFirstName: z.string().min(1).describe("Submitter first name."),
      submitterLastName: z.string().min(1).describe("Submitter last name."),
      description: z.string().min(1).describe("Case description."),
      siteId: z
        .string()
        .min(1)
        .describe("Id of the site the case is submitted to (site owner must have feature enabled)."),
      submitterCompanyName: z
        .string()
        .optional()
        .describe("Optional submitter company name."),
      copiedFromUrl: z
        .string()
        .optional()
        .describe("Optional original / copied-from URL."),
      infringingUrl: z
        .string()
        .optional()
        .describe("Optional infringing URL."),
      infringingSiteIp: z
        .string()
        .optional()
        .describe("Optional infringing site IP."),
      token: tokenArg,
    },
    async ({
      submitterEmail,
      submitterFirstName,
      submitterLastName,
      description,
      siteId,
      submitterCompanyName,
      copiedFromUrl,
      infringingUrl,
      infringingSiteIp,
      token,
    }) => {
      try {
        const payload: Record<string, unknown> = {
          submitterEmail,
          submitterFirstName,
          submitterLastName,
          description,
          siteId,
        };
        if (submitterCompanyName) payload.submitterCompanyName = submitterCompanyName;
        if (copiedFromUrl) payload.copiedFromUrl = copiedFromUrl;
        if (infringingUrl) payload.infringingUrl = infringingUrl;
        if (infringingSiteIp) payload.infringingSiteIp = infringingSiteIp;
        const data = await dmcaPost("/createComplianceCase", payload, resolveToken(token).value);
        return jsonResult(data);
      } catch (err) {
        return errorResult(err);
      }
    }
  );

  server.tool(
    "getSiteReport",
    "GET https://api.dmca.com/getSiteReport/{domain} — site report for a fully qualified domain name. Uses the session/`token` DMCA API token.",
    {
      domain: z
        .string()
        .min(1)
        .describe("Fully qualified domain name (upstream path segment)."),
      token: tokenArg,
    },
    async ({ domain, token }) => {
      try {
        const path = `/getSiteReport/${encodeURIComponent(domain.trim())}`;
        const data = await dmcaGet(path, resolveToken(token).value);
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
  // Do not log tokens, passwords, or env secret values.
  const server = createServer();
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  console.error("Fatal:", err instanceof Error ? err.message : String(err));
  process.exit(1);
});
