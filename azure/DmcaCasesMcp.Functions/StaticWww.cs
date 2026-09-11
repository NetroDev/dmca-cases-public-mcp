using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DmcaCasesMcp.Functions;

/// <summary>Serves brand logo assets from wwwroot.</summary>
public sealed class StaticWww
{
    [Function("DmcaLogoSvg")]
    public Task<HttpResponseData> Svg(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dmca-logo.svg")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "dmca-logo.svg", "image/svg+xml; charset=utf-8", ct);

    [Function("DmcaLogoPng")]
    public Task<HttpResponseData> Png(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dmca-logo.png")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "dmca-logo.png", "image/png", ct);

    [Function("FaviconSvg")]
    public Task<HttpResponseData> FaviconSvg(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "favicon.svg")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "dmca-logo.svg", "image/svg+xml; charset=utf-8", ct);

    [Function("FaviconPng")]
    public Task<HttpResponseData> FaviconPng(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "favicon.png")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "dmca-logo.png", "image/png", ct);


    [Function("FaviconIco")]
    public Task<HttpResponseData> FaviconIco(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "favicon.ico")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "favicon.ico", "image/x-icon", ct);

    [Function("AppleTouchIcon")]
    public Task<HttpResponseData> AppleTouchIcon(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apple-touch-icon.png")] HttpRequestData req,
        CancellationToken ct)
        => Serve(req, "apple-touch-icon.png", "image/png", ct);

    private static async Task<HttpResponseData> Serve(HttpRequestData req, string file, string contentType, CancellationToken ct)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", file),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", file),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            var miss = req.CreateResponse(HttpStatusCode.NotFound);
            await miss.WriteStringAsync("Not found", ct).ConfigureAwait(false);
            return miss;
        }

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", contentType);
        res.Headers.Add("Cache-Control", "public, max-age=86400");
        await res.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        return res;
    }
}
