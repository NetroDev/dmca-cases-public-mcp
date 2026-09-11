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

/**
 * GET a DMCA.com public REST path and return parsed JSON as-is.
 * Does not invent response schemas. Does not call mutation endpoints.
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
  let body: unknown = text;
  if (text.length > 0) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  } else {
    body = null;
  }

  if (!res.ok) {
    const err = new Error(
      `DMCA API ${path} returned HTTP ${res.status}`
    ) as Error & { status?: number; body?: unknown };
    err.status = res.status;
    err.body = body;
    throw err;
  }

  return body;
}
