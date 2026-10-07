using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace DmcaCasesMcp.Functions;

/// <summary>Where the token used for an upstream call came from. Never carries the token itself.</summary>
public enum TokenSource
{
    Argument,
    Session,
    Header,
}

public readonly record struct ResolvedToken(string Value, TokenSource Source);

/// <summary>
/// In-memory, bounded cache of DMCA API tokens obtained by the login tool, keyed by MCP session.
/// Keys are SHA-256 hashes of the MCP session id, so raw session ids are not kept either.
/// Nothing is persisted; entries expire after <see cref="Ttl"/> and the cache never grows past
/// <see cref="MaxEntries"/>. Each Functions instance has its own cache.
/// </summary>
public sealed class SessionTokenCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(12);
    public const int MaxEntries = 1000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public SessionTokenCache() : this(TimeProvider.System) { }

    public SessionTokenCache(TimeProvider time) => _time = time;

    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);

    public int Count => _entries.Count;

    public void Set(string sessionId, string token)
    {
        var now = _time.GetUtcNow();
        if (_entries.Count >= MaxEntries)
        {
            Trim(now);
        }

        _entries[Key(sessionId)] = new Entry(token, now + Ttl);
    }

    public void Remove(string sessionId) => _entries.TryRemove(Key(sessionId), out _);

    public bool TryGet(string sessionId, out string token)
    {
        token = string.Empty;
        var key = Key(sessionId);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        token = entry.Token;
        return true;
    }

    private void Trim(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAt <= now) _entries.TryRemove(key, out _);
        }

        // Still full: drop the entries closest to expiry (i.e. the oldest logins).
        var excess = _entries.Count - MaxEntries + 1;
        if (excess <= 0) return;
        foreach (var (key, _) in _entries.OrderBy(e => e.Value.ExpiresAt).Take(excess).ToList())
        {
            _entries.TryRemove(key, out _);
        }
    }

    private static string Key(string sessionId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));
}

/// <summary>
/// Resolves the DMCA API token for a tool call. Only the caller's own token is used:
///   1. the tool's optional <c>token</c> argument,
///   2. a token on the MCP HTTP request: <c>Authorization: Bearer &lt;token&gt;</c> or <c>X-DMCA-Token</c>
///      (the Functions MCP extension passes request headers through to the tool context),
///   3. a token remembered from this MCP session's successful <c>login</c> call.
/// There is no server-wide fallback token. Token values are never logged.
/// </summary>
public sealed class DmcaTokenResolver
{
    public const string TokenHeaderName = "X-DMCA-Token";
    private const string SessionHeaderName = "Mcp-Session-Id";

    private readonly SessionTokenCache _cache;

    public DmcaTokenResolver(SessionTokenCache cache)
    {
        _cache = cache;
    }

    /// <summary>MCP session id for this invocation, if the extension provides one.</summary>
    public static string? GetSessionId(ToolInvocationContext? context)
    {
        if (context is null) return null;
        if (!string.IsNullOrWhiteSpace(context.SessionId)) return context.SessionId;
        if (context.Transport is HttpTransport http
            && http.Headers.TryGetValue(SessionHeaderName, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return null;
    }

    public ResolvedToken? Resolve(ToolInvocationContext? context, string? tokenArgument)
    {
        if (!string.IsNullOrWhiteSpace(tokenArgument))
        {
            return new ResolvedToken(tokenArgument.Trim(), TokenSource.Argument);
        }

        var header = GetHeaderToken(context);
        if (header is not null)
        {
            return new ResolvedToken(header, TokenSource.Header);
        }

        var sessionId = GetSessionId(context);
        if (sessionId is not null && _cache.TryGet(sessionId, out var cached))
        {
            return new ResolvedToken(cached, TokenSource.Session);
        }

        return null;
    }

    /// <summary><c>Authorization: Bearer</c> or <c>X-DMCA-Token</c> header on the MCP request, if any.</summary>
    public static string? GetHeaderToken(ToolInvocationContext? context)
    {
        if (context?.Transport is not HttpTransport http) return null;
        return BearerToken(http.Headers.TryGetValue("Authorization", out var auth) ? auth : null)
            ?? NonBlank(http.Headers.TryGetValue(TokenHeaderName, out var direct) ? direct : null);
    }

    /// <summary>The token from an <c>Authorization: Bearer ...</c> value; other schemes are ignored.</summary>
    public static string? BearerToken(string? authorization)
    {
        const string scheme = "Bearer ";
        var value = authorization?.TrimStart();
        if (value is null || !value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;
        return NonBlank(value[scheme.Length..]);
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Remember a login token for this MCP session. Returns false when there is no session id to key on.</summary>
    public bool RememberForSession(ToolInvocationContext? context, string token)
    {
        var sessionId = GetSessionId(context);
        if (sessionId is null) return false;
        _cache.Set(sessionId, token);
        return true;
    }

    /// <summary>Drop any token cached for this MCP session (used when a new login attempt fails).</summary>
    public void ForgetSession(ToolInvocationContext? context)
    {
        var sessionId = GetSessionId(context);
        if (sessionId is not null) _cache.Remove(sessionId);
    }

    /// <summary>
    /// Pull the token out of an upstream /login response. The API returns a bare JSON string;
    /// an object with a token-like property is accepted too in case that changes.
    /// </summary>
    public static string? ExtractLoginToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                return FirstNonBlank(root.GetString());
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String
                        && (prop.Name.Equals("token", StringComparison.OrdinalIgnoreCase)
                            || prop.Name.Equals("d", StringComparison.OrdinalIgnoreCase)))
                    {
                        return FirstNonBlank(prop.Value.GetString());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; treat a single-line plain-text body as the token.
            var text = body.Trim();
            if (!text.Contains('\n') && !text.Contains(' ')) return text;
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }

        return null;
    }
}

/// <summary>Message returned when the caller has neither logged in nor passed a token.</summary>
public static class NotLoggedIn
{
    public const string Message = "Not logged in. Call the login tool with your DMCA.com email and password first, or pass token.";
}
