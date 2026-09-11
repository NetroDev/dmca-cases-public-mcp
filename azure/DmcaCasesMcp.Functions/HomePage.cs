using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

/// <summary>
/// SEO-friendly branded cover page.
/// Note: Azure Functions HTTP routes are under the "api" prefix by default.
/// Do NOT use Route="/" — that becomes "api//" and crashes the host.
/// </summary>
public sealed class HomePage
{
    private static readonly Lazy<string> Html = new(LoadHtml);

    [Function("HomePage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req,
        CancellationToken ct)
    {
        return await WriteHtmlAsync(req, ct).ConfigureAwait(false);
    }

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
        <meta name="viewport" content="width=device-width, initial-scale=1"/>
        <title>DMCA Cases MCP — Official DMCA.com Cases API for AI Agents</title>
        <meta name="description" content="Official DMCA.com Cases MCP for AI agents. Company of record: DMCA.com."/>
        <meta name="robots" content="index,follow"/>
        <link rel="canonical" href="https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net/api"/>
        </head><body>
        <h1>DMCA Cases MCP</h1>
        <p>Company of record: <a href="https://www.dmca.com/">DMCA.com</a></p>
        <p><a href="https://www.dmca.com/api/">API docs</a> · <a href="https://www.dmca.com/">Main site</a></p>
        </body></html>
        """;
    }
}
