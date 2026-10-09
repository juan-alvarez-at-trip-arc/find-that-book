using System.Net;
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
[ApiController]
[Route("api/health")]
public sealed class HealthController(
    HealthReportService health,
    AiProviderSelection provider,
    IOptions<GeminiOptions> gemini,
    IOptions<OpenAiCompatibleOptions> openAi,
    IOptions<OpenLibraryOptions> openLibrary) : ControllerBase
{
    /// <summary>Returns the effective configuration values. API keys are masked, keeping only their first and last four characters. Only callers on the same machine are answered.</summary>
    /// <returns>A 200 result with the configuration dump, or 404 when the caller is not on the local machine.</returns>
    [HttpGet("config")]
    public IActionResult Config() => IsLocalRequest()
        ? Ok(ConfigDump.Build(provider, gemini.Value, openAi.Value, openLibrary.Value))
        : NotFound();

    /// <summary>Tells whether the current request comes from the machine the API runs on.</summary>
    /// <returns>True when the remote address is unknown, loopback, or equal to the local address.</returns>
    private bool IsLocalRequest()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is null || IPAddress.IsLoopback(remote))
        {
            return true;
        }

        return remote.Equals(HttpContext.Connection.LocalIpAddress);
    }

    /// <summary>Reports that the API is running, for container and load balancer health checks. It makes no outside calls.</summary>
    /// <returns>A 200 result with <c>{ "status": "ok" }</c>.</returns>
    [HttpGet]
    public IActionResult Live() => Ok(new { status = "ok" });

    /// <summary>Reports whether searches can work. The result is cached for 30 seconds.</summary>
    /// <param name="ct">Cancellation token tied to the HTTP request.</param>
    /// <returns>200 with the report when ready or degraded (for example the AI is down and the fallback is used), 503 with the report when the configuration is invalid or Open Library is down.</returns>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var report = await health.GetAsync(ct);
        return report.Status == HealthReport.Unready
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, report)
            : Ok(report);
    }
}
