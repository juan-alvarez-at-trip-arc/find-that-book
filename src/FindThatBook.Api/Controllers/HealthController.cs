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
    /// <summary>Returns the effective configuration values. API keys are masked, keeping only their first and last four characters.</summary>
    /// <returns>A 200 result with the configuration dump.</returns>
    [HttpGet("config")]
    public ConfigDump Config() => ConfigDump.Build(provider, gemini.Value, openAi.Value, openLibrary.Value);

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
