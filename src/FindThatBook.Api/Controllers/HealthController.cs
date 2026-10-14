using System.Net;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Controllers;

/// <summary>Liveness and readiness probes.</summary>
/// <param name="health">Service that builds and caches the readiness report.</param>
/// <param name="provider">The selected AI provider.</param>
/// <param name="gemini">Gemini settings.</param>
/// <param name="openAi">OpenAI-compatible settings.</param>
/// <param name="openLibrary">Open Library settings.</param>
/// <param name="configuration">Application configuration, read for the <c>Health:ExposeConfig</c> switch.</param>
/// <param name="generation">The generation settings (temperature, top-k, top-p, seed, reasoning effort) shown in the configuration dump.</param>
[ApiController]
[Route("api/health")]
public sealed class HealthController(
    HealthReportService health,
    AiProviderSelection provider,
    IOptions<GeminiOptions> gemini,
    IOptions<OpenAiCompatibleOptions> openAi,
    IOptions<OpenLibraryOptions> openLibrary,
    IConfiguration configuration,
    IOptions<AiGenerationOptions> generation) : ControllerBase
{
    /// <summary>Returns the effective configuration values. API keys are masked, keeping only their last four characters. The endpoint is off unless <c>Health:ExposeConfig</c> is true, because a same-machine reverse proxy that adds no forwarding header makes every caller look local. When it is on, only callers on the same machine are answered, and never callers relayed by a proxy.</summary>
    /// <returns>A 200 result with the configuration dump, or 404 when the endpoint is off, the caller is not on the local machine or its address is unknown.</returns>
    [HttpGet("config")]
    public IActionResult Config() => CanSeeDiagnostics()
        ? Ok(ConfigDump.Build(provider, gemini.Value, openAi.Value, openLibrary.Value, generation.Value))
        : NotFound();

    /// <summary>Tells whether the caller may see internal details: <c>Health:ExposeConfig</c> is true and the request comes directly from the machine the API runs on.</summary>
    /// <returns>True when diagnostics are switched on and the caller is local. A blank or invalid setting counts as off.</returns>
    private bool CanSeeDiagnostics()
        => bool.TryParse(configuration["Health:ExposeConfig"], out var exposed) && exposed && IsLocalRequest();

    /// <summary>Tells whether the current request comes directly from the machine the API runs on. A request that carries proxy headers was relayed, so its loopback address says nothing about the original caller.</summary>
    /// <returns>True when the remote address is known and is loopback or equal to the local address, and no proxy header is present. False when the address is unknown.</returns>
    private bool IsLocalRequest()
    {
        var headers = HttpContext.Request.Headers;
        if (ProxyHeaders.Any(headers.ContainsKey))
        {
            return false;
        }

        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return false;
        }

        return IPAddress.IsLoopback(remote) || remote.Equals(HttpContext.Connection.LocalIpAddress);
    }

    private static readonly string[] ProxyHeaders = ["X-Forwarded-For", "Forwarded", "X-Real-IP"];

    /// <summary>Reports that the API is running, for container and load balancer health checks. It makes no outside calls.</summary>
    /// <returns>A 200 result with <c>{ "status": "ok" }</c>.</returns>
    [HttpGet]
    public IActionResult Live() => Ok(new { status = "ok" });

    /// <summary>Reports whether searches can work. The result is cached for 30 seconds. The body holds only the overall status, because the per-dependency breakdown names the AI provider and says whether its key is missing or rejected. The full report goes only to callers that pass <see cref="CanSeeDiagnostics"/>.</summary>
    /// <param name="ct">Cancellation token tied to the HTTP request.</param>
    /// <returns>200 when ready or degraded (for example the AI is down and the fallback is used), 503 when the configuration is invalid or Open Library is down. The body is <c>{ "status": ... }</c>, or the full report for a caller that may see diagnostics.</returns>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var report = await health.GetAsync(ct);
        object body = CanSeeDiagnostics() ? report : new { status = report.Status };
        return report.Status == HealthReport.Unready
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, body)
            : Ok(body);
    }
}
