using System.Text.Json;
using FindThatBook.Application.Telemetry;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// The Polly pipeline around every Open Library attempt: retry with a growing delay, a circuit breaker, and a timeout per attempt. One instance is shared by the whole process so the circuit state is shared too. A 429 is not handled here: it throws <see cref="FindThatBook.Application.Abstractions.CatalogUnavailableException"/>, which no strategy treats as a failure.
/// </summary>
public sealed class OpenLibraryResiliencePipeline
{
    private OpenLibraryResiliencePipeline(ResiliencePipeline pipeline) => Pipeline = pipeline;

    /// <summary>The configured pipeline. Run one attempt, with its gate and cooldown checks, inside it.</summary>
    public ResiliencePipeline Pipeline { get; }

    /// <summary>Builds the pipeline from the settings: retry (only when <see cref="OpenLibraryOptions.MaxRetries"/> is above 0), then the circuit breaker, then the timeout per attempt.</summary>
    /// <param name="settings">Retry, timeout and circuit settings.</param>
    /// <param name="clock">Time source, replaceable in tests. Defaults to the system clock.</param>
    /// <returns>A pipeline whose circuit state belongs to this instance.</returns>
    public static OpenLibraryResiliencePipeline Create(OpenLibraryOptions settings, TimeProvider? clock = null)
    {
        var builder = new ResiliencePipelineBuilder { TimeProvider = clock ?? TimeProvider.System };
        var transient = new PredicateBuilder().Handle<Exception>(IsTransient);

        if (settings.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = settings.MaxRetries,
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, settings.RetryDelayMs)),
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true,
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            ShouldHandle = transient,
            FailureRatio = settings.CircuitBreakerFailureRatio,
            MinimumThroughput = Math.Max(2, settings.CircuitBreakerMinimumThroughput),
            SamplingDuration = TimeSpan.FromSeconds(Math.Max(1, settings.CircuitBreakerSamplingSeconds)),
            BreakDuration = TimeSpan.FromSeconds(Math.Max(1, settings.CircuitBreakerBreakSeconds)),
            OnOpened = _ =>
            {
                FindThatBookTelemetry.RecordOpenLibraryCircuit("opened");
                return default;
            },
            OnClosed = _ =>
            {
                FindThatBookTelemetry.RecordOpenLibraryCircuit("closed");
                return default;
            },
        });

        builder.AddTimeout(TimeSpan.FromMilliseconds(Math.Max(1, settings.AttemptTimeoutMs)));
        return new OpenLibraryResiliencePipeline(builder.Build());
    }

    /// <summary>Tells whether a failure is worth a retry and counts towards the circuit: network errors, timeouts, invalid JSON and 5xx responses.</summary>
    /// <param name="error">The failure of one attempt.</param>
    /// <returns>True for a transient failure.</returns>
    private static bool IsTransient(Exception error)
        => error is HttpRequestException or JsonException or TimeoutRejectedException or OpenLibraryTransientException;
}

/// <summary>A failure of one Open Library attempt that may pass on its own, such as a 5xx response or a timeout of the HTTP client.</summary>
/// <param name="message">What went wrong.</param>
internal sealed class OpenLibraryTransientException(string message) : Exception(message);
