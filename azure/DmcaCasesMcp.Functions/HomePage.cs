using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// SEO cover page served at the site root and at /cover. Requires host.json
/// extensions.http.routePrefix = "".
///
/// Why the root route is "{rootpage:regex(^$)?}" (verified against host + worker source):
/// - Route = "": the Functions HOST treats an empty/whitespace route as "use the function name",
///   so it maps to /HomePage while the WORKER (ASP.NET Core integration) maps "/" + "" = "/".
///   The two disagree, so GET / falls through to the host's built-in
///   "Your Azure Function App is up and running" page and /HomePage 500s.
/// - Route = "/": host accepts it, but the worker builds "{prefix}/{route}" = "//", which
///   RoutePatternFactory rejects; that breaks the worker endpoint data source and EVERY HTTP
///   function returns 500 (this took the app down on 2026-10-06; do not use).
/// - "{rootpage:regex(^$)?}": an optional parameter that may only be empty. Host template matches
///   exactly "/" (any non-empty segment fails the regex, so /cover, /robots.txt etc. are unaffected
///   regardless of route order); worker template "/{rootpage:regex(^$)?}" parses fine.
/// - AzureWebJobsDisableHomepage=true (app setting) only turns the host default page into 204 when
///   no function matches "/"; it does not by itself route "/" to this function.
/// </summary>
public sealed class HomePage
{
    private static readonly Lazy<string> Html = new(LoadHtml);

    // Exact root (requires routePrefix ""). See class remarks for why this is not "" or "/".
    [Function("HomePage")]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "head", Route = "{rootpage:regex(^$)?}")] HttpRequestData req,
        CancellationToken ct)
        => WriteHtmlAsync(req, ct);

    // Friendly alias
    [Function("HomePageCover")]
    public Task<HttpResponseData> Cover(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cover")] HttpRequestData req,
        CancellationToken ct)
        => WriteHtmlAsync(req, ct);

    private static async Task<HttpResponseData> WriteHtmlAsync(HttpRequestData req, CancellationToken ct)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "text/html; charset=utf-8");
        res.Headers.Add("Cache-Control", "public, max-age=300");
        res.Headers.Add("X-Content-Type-Options", "nosniff");
        await res.WriteStringAsync(Html.Value, ct).ConfigureAwait(false);
        return res;
    }

    private static string LoadHtml()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "index.html"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return File.ReadAllText(c, Encoding.UTF8);
        }

        return """
        <!DOCTYPE html><html lang="en"><head>
        <meta charset="utf-8"/>
        <title>DMCA Cases MCP</title>
        <link rel="canonical" href="https://mcp.dmca.com/"/>
        </head><body>
        <h1>DMCA Cases MCP</h1>
        <p><a href="https://www.dmca.com/">DMCA.com</a></p>
        </body></html>
        """;
    }
}
