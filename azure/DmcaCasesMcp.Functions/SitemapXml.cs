using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

public sealed class SitemapXml
{
    [Function("SitemapXml")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sitemap.xml")] HttpRequestData req,
        CancellationToken ct)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/xml; charset=utf-8");
        const string body =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url>
                <loc>https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net/</loc>
                <changefreq>weekly</changefreq>
                <priority>1.0</priority>
              </url>
              <url>
                <loc>https://www.dmca.com/</loc>
                <changefreq>daily</changefreq>
                <priority>0.8</priority>
              </url>
              <url>
                <loc>https://www.dmca.com/api/</loc>
                <changefreq>weekly</changefreq>
                <priority>0.7</priority>
              </url>
            </urlset>
            """;
        await res.WriteStringAsync(body, ct).ConfigureAwait(false);
        return res;
    }
}
