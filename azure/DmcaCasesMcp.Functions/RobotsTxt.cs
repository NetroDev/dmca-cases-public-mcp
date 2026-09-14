using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

public sealed class RobotsTxt
{
    [Function("RobotsTxt")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "robots.txt")] HttpRequestData req,
        CancellationToken ct)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        const string body =
            """
            User-agent: *
            Allow: /
            Allow: /api/mcp
            Disallow: /api/listCases
            Disallow: /api/listDIYCases
            Disallow: /api/listComplianceCases
            Disallow: /api/getCaseById
            Disallow: /api/login
            Disallow: /api/createCase
            Disallow: /api/updateCase
            Disallow: /api/createDIYCase
            Disallow: /api/createComplianceCase
            Disallow: /api/getSiteReport
            Disallow: /runtime/

            Sitemap: https://dmca-cases-public-mcp-afdcard8bbdtd4e3.westus3-01.azurewebsites.net/sitemap.xml
            """;
        await res.WriteStringAsync(body, ct).ConfigureAwait(false);
        return res;
    }
}
