using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// SEO cover page. host.json sets HTTP routePrefix to "".
/// Never use Route="/" — under a non-empty prefix that becomes "api//" and crashes the host.
/// </summary>
public sealed class HomePage
{
    private static readonly Lazy<string> Html = new(LoadHtml);

    // Exact root when routePrefix is empty.
    [Function("HomePage")]
    public Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "")] HttpRequestData req,
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
        <link rel="canonical" href="https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net/"/>
        </head><body>
        <h1>DMCA Cases MCP</h1>
        <p><a href="https://www.dmca.com/">DMCA.com</a></p>
        </body></html>
        """;
    }
}
