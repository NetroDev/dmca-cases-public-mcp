using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Configuration;

namespace DmcaCasesMcp.Functions;

/// <summary>Where the token used for an upstream call came from. Never carries the token itself.</summary>
public enum TokenSource
{
    Argument,
    Session,
    Header,
    AppSetting,
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
/// Resolves the DMCA API token for a tool call, in this order:
///   1. the tool's optional <c>token</c> argument,
///   2. a token remembered from this MCP session's successful <c>login</c> call,
///   3. an <c>X-DMCA-Token</c> header on the MCP HTTP request (when the extension passes headers through),
///   4. the DMCA_API_TOKEN / DMCA_TOKEN app setting (server-wide default, kept for backward compatibility).
/// Token values are never logged.
/// </summary>
public sealed class DmcaTokenResolver
{
    public const string TokenHeaderName = "X-DMCA-Token";
    private const string SessionHeaderName = "Mcp-Session-Id";

    private readonly SessionTokenCache _cache;
    private readonly IConfiguration _config;

    public DmcaTokenResolver(SessionTokenCache cache, IConfiguration config)
    {
        _cache = cache;
        _config = config;
    }

    /// <summary>MCP session id for this invocation, if the extension provides one.</summary>
    public static string? GetSessionId(ToolInvocationContext? context)
    {
        if (context is null) return null;
        if (!string.IsNullOrWhiteSpace(context.SessionId)) return context.SessionId;
        return GetHeader(context, SessionHeaderName);
    }

    public static string? GetHeader(ToolInvocationContext? context, string name)
    {
        if (context?.Transport is HttpTransport http
            && http.Headers.TryGetValue(name, out var value)
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

        var sessionId = GetSessionId(context);
        if (sessionId is not null && _cache.TryGet(sessionId, out var cached))
        {
            return new ResolvedToken(cached, TokenSource.Session);
        }

        var header = GetHeader(context, TokenHeaderName);
        if (header is not null)
        {
            return new ResolvedToken(header, TokenSource.Header);
        }

        return ResolveFallback();
    }

    /// <summary>Server-wide DMCA_API_TOKEN / DMCA_TOKEN setting, or null when unset.</summary>
    public ResolvedToken? ResolveFallback()
    {
        var token = FirstNonBlank(
            _config["DMCA_API_TOKEN"],
            _config["DMCA_TOKEN"],
            Environment.GetEnvironmentVariable("DMCA_API_TOKEN"),
            Environment.GetEnvironmentVariable("DMCA_TOKEN"));
        return token is null ? null : new ResolvedToken(token, TokenSource.AppSetting);
    }

    /// <summary>Remember a login token for this MCP session. Returns false when there is no session id to key on.</summary>
    public bool RememberForSession(ToolInvocationContext? context, string token)
    {
        var sessionId = GetSessionId(context);
        if (sessionId is null) return false;
        _cache.Set(sessionId, token);
        return true;
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

/// <summary>Thrown when no token is available from any source.</summary>
public sealed class MissingTokenException : Exception
{
    public MissingTokenException()
        : base("No DMCA API token available. Call the login tool first (the token is then reused for this session), "
               + "pass the token from login as the `token` argument, or configure DMCA_API_TOKEN on the server.")
    {
    }
}
