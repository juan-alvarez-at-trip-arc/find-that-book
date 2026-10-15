using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Builds the readiness report from the configuration check and every registered probe. The report is cached so frequent polling does not spend AI quota or trip Open Library throttling.</summary>
/// <param name="probes">The registered probes.</param>
/// <param name="gemini">Gemini settings.</param>
/// <param name="openAi">OpenAI-compatible settings.</param>
/// <param name="openLibrary">Open Library settings.</param>
/// <param name="provider">The selected AI provider.</param>
/// <param name="clock">Time source, replaceable in tests. Defaults to the system clock.</param>
/// <param name="generation">The generation settings (temperature, top-k, top-p, seed, reasoning effort) to validate. When null, they are not checked.</param>
/// <param name="aiResilience">The retry, timeout and circuit settings of the AI calls to validate. When null, they are not checked.</param>
public sealed class HealthReportService(
    IEnumerable<IHealthProbe> probes,
    IOptions<GeminiOptions> gemini,
    IOptions<OpenAiCompatibleOptions> openAi,
    IOptions<OpenLibraryOptions> openLibrary,
    AiProviderSelection provider,
    TimeProvider? clock = null,
    IOptions<AiGenerationOptions>? generation = null,
    IOptions<AiResilienceOptions>? aiResilience = null)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HealthReport? _cached;
    private DateTimeOffset _expires;

    /// <summary>Gets the current report, running the probes only when the cached one is older than 30 seconds.</summary>
    /// <param name="ct">Cancellation token tied to the HTTP request.</param>
    /// <returns>The report.</returns>
    public async Task<HealthReport> GetAsync(CancellationToken ct)
    {
        if (Fresh() is { } hit) return hit;

        await _lock.WaitAsync(ct);
        try
        {
            if (Fresh() is { } again) return again;

            var results = await Task.WhenAll(probes.Select(p => p.CheckAsync(ct)));
            var config = HealthConfigCheck.Validate(provider, gemini.Value, openAi.Value, openLibrary.Value, generation?.Value, aiResilience?.Value);
            var criticalDown = probes.Zip(results).Any(x => x.First.Critical && x.Second.Status == HealthStatus.Down);

            var status = config.Status == HealthStatus.Down || criticalDown
                ? HealthReport.Unready
                : config.Status != HealthStatus.Ok || results.Any(r => r.Status != HealthStatus.Ok)
                    ? HealthReport.Degraded
                    : HealthReport.Ready;

            _cached = new HealthReport(status, config, results);
            _expires = _clock.GetUtcNow() + CacheLifetime;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private HealthReport? Fresh() => _cached is not null && _clock.GetUtcNow() < _expires ? _cached : null;
}
