const API_BASE = "https://api.dmca.com";

/**
 * Resolve DMCA API token from env. Never log the value.
 */
export function getDmcaToken(): string {
  const token = process.env.DMCA_API_TOKEN || process.env.DMCA_TOKEN;
  if (!token || !token.trim()) {
    throw new Error(
      "Missing DMCA API token. Set DMCA_API_TOKEN (or DMCA_TOKEN) in the environment."
    );
  }
  return token.trim();
}

export type DmcaQuery = Record<string, string | number | undefined | null>;

function parseBody(text: string): unknown {
  if (text.length === 0) return null;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function throwIfNotOk(path: string, res: Response, body: unknown): void {
  if (res.ok) return;
  const err = new Error(`DMCA API ${path} returned HTTP ${res.status}`) as Error & {
    status?: number;
    body?: unknown;
  };
  err.status = res.status;
  err.body = body;
  throw err;
}

/**
 * GET a DMCA.com public REST path and return parsed JSON as-is.
 */
export async function dmcaGet(
  path: string,
  query?: DmcaQuery
): Promise<unknown> {
  const token = getDmcaToken();
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

  const text = await res.text();
  const body = parseBody(text);
  throwIfNotOk(path, res, body);
  return body;
}

/**
 * POST JSON to a DMCA.com path. Optionally attach Token header.
 * Never log request bodies (may contain password) or Token values.
 */
export async function dmcaPost(
  path: string,
  payload: Record<string, unknown>,
  options?: { withToken?: boolean }
): Promise<unknown> {
  const withToken = options?.withToken !== false;
  const url = new URL(path.startsWith("http") ? path : `${API_BASE}${path}`);
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  if (withToken) {
    headers.Token = getDmcaToken();
  }

  const res = await fetch(url, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });

  const text = await res.text();
  const body = parseBody(text);
  throwIfNotOk(path, res, body);
  return body;
}

/** POST /login — no Token header. Do not log email/password. */
export async function dmcaLogin(email: string, password: string): Promise<unknown> {
  return dmcaPost(
    "/login",
    { email, password },
    { withToken: false }
  );
}
