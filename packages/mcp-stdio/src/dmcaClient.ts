const API_BASE = "https://api.dmca.com";

/** Where the token for a call came from. Never carries the token itself. */
export type TokenSource = "argument" | "session" | "env";

export interface ResolvedToken {
  value: string;
  source: TokenSource;
}

/**
 * Token remembered from this process's last successful login. A stdio server runs one
 * process per client, so this is already per caller. In memory only; expires after 12 hours.
 */
const SESSION_TTL_MS = 12 * 60 * 60 * 1000;
let sessionToken: { value: string; expiresAt: number } | undefined;

export function rememberSessionToken(token: string): void {
  sessionToken = { value: token, expiresAt: Date.now() + SESSION_TTL_MS };
}

function getSessionToken(): string | undefined {
  if (!sessionToken) return undefined;
  if (sessionToken.expiresAt <= Date.now()) {
    sessionToken = undefined;
    return undefined;
  }
  return sessionToken.value;
}

/** DMCA_API_TOKEN / DMCA_TOKEN from the environment, if set. */
export function getEnvToken(): string | undefined {
  const token = process.env.DMCA_API_TOKEN || process.env.DMCA_TOKEN;
  return token && token.trim() ? token.trim() : undefined;
}

/**
 * Resolve the token for a tool call: explicit `token` argument, then the token from this
 * session's login, then DMCA_API_TOKEN / DMCA_TOKEN. Never log the value.
 */
export function resolveToken(explicit?: string): ResolvedToken {
  if (explicit && explicit.trim()) return { value: explicit.trim(), source: "argument" };
  const session = getSessionToken();
  if (session) return { value: session, source: "session" };
  const env = getEnvToken();
  if (env) return { value: env, source: "env" };
  throw new Error(
    "No DMCA API token available. Call the login tool first (the token is then reused for this session), " +
      "pass the token from login as the `token` argument, or set DMCA_API_TOKEN (or DMCA_TOKEN) in the environment."
  );
}

export type DmcaQuery = Record<string, string | number | undefined | null>;

export class DmcaApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly body: unknown,
    public readonly rawBody: string
  ) {
    super(message);
  }
}

/**
 * Parse an upstream body. When asked for JSON the DMCA API wraps its payload in a JSON string
 * (e.g. "[{\"ID\":...}]", or "" for an empty 404), so unwrap one level of that too.
 */
function parseBody(text: string): unknown {
  if (text.trim().length === 0) return null;
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return text;
  }
  if (typeof value === "string") {
    const inner = value.trim();
    if (inner.length === 0) return null;
    if (inner.startsWith("[") || inner.startsWith("{")) {
      try {
        return JSON.parse(inner);
      } catch {
        return value;
      }
    }
  }
  return value;
}

async function readOrThrow(path: string, res: Response): Promise<unknown> {
  const text = await res.text();
  const body = parseBody(text);
  if (!res.ok) {
    throw new DmcaApiError(`DMCA API ${path} returned HTTP ${res.status}`, res.status, body, text);
  }
  return body;
}

/**
 * GET a DMCA.com public REST path with the given token and return parsed JSON as-is.
 * Path may include segments (e.g. /getSiteReport/{domain}); optional query is appended.
 */
export async function dmcaGet(path: string, token: string, query?: DmcaQuery): Promise<unknown> {
  const url = new URL(path.startsWith("http") ? path : `${API_BASE}${path}`);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === "") continue;
      url.searchParams.set(key, String(value));
    }
  }

  const res = await fetch(url, {
    method: "GET",
    headers: {
      Token: token,
      Accept: "application/json",
    },
  });
  return readOrThrow(path, res);
}

/**
 * POST JSON to a DMCA.com path. Sends the Token header when a token is given (login passes none).
 * Never log request bodies (may contain password) or Token values.
 */
export async function dmcaPost(
  path: string,
  payload: Record<string, unknown>,
  token: string | undefined
): Promise<unknown> {
  const url = new URL(path.startsWith("http") ? path : `${API_BASE}${path}`);
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  if (token !== undefined) {
    headers.Token = token;
  }

  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });
  return readOrThrow(path, res);
}

/**
 * List endpoints. The DMCA API signals "no matching cases" inconsistently: /listDIYCases answers
 * HTTP 404 with an empty body (or "") and /listComplianceCases answers HTTP 200 with an empty body.
 * Both become an explicit empty list. A 404 with a body (e.g. the gateway's "Resource not found")
 * is still an error.
 */
export async function dmcaList(path: string, token: string, page?: number): Promise<unknown> {
  const empty = (upstream: string) => ({
    cases: [],
    count: 0,
    page,
    note: `No cases returned. DMCA API ${path} answered ${upstream}, which it uses when the account has no matching cases.`,
  });
  try {
    const data = await dmcaGet(path, token, { page });
    return data === null ? empty("HTTP 200 with an empty body") : data;
  } catch (err) {
    if (err instanceof DmcaApiError && err.status === 404 && err.body === null) {
      return empty("HTTP 404 with an empty body");
    }
    throw err;
  }
}

/** POST /login — no Token header. Do not log email/password. Returns the token from the response. */
export async function dmcaLogin(email: string, password: string): Promise<string> {
  const data = await dmcaPost("/login", { email, password }, undefined);
  const token = extractLoginToken(data);
  if (!token) {
    throw new Error("DMCA API /login succeeded but the response did not contain a token.");
  }
  return token;
}

/** The API returns a bare JSON string; accept an object with a token-like field too. */
function extractLoginToken(data: unknown): string | undefined {
  if (typeof data === "string") return data.trim() || undefined;
  if (data && typeof data === "object") {
    for (const [key, value] of Object.entries(data as Record<string, unknown>)) {
      if ((key.toLowerCase() === "token" || key === "d") && typeof value === "string" && value.trim()) {
        return value.trim();
      }
    }
  }
  return undefined;
}
