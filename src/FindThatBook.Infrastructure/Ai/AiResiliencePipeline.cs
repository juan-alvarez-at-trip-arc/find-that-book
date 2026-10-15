using System.Net;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Telemetry;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>
/// The Polly pipeline around every AI HTTP call: a total timeout, one retry for fast failures, a circuit breaker and a timeout per attempt. One instance is shared by the whole process so the circuit state is shared too. While the circuit is open a call fails at once with <see cref="AiUnavailableException"/>, and the fallback decorators answer without the AI.
/// </summary>
public sealed class AiResiliencePipeline
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private AiResiliencePipeline(ResiliencePipeline<HttpResponseMessage> pipeline) => _pipeline = pipeline;

    /// <summary>A pipeline that sends the call once with no retry, no circuit and no timeout of its own.</summary>
    public static AiResiliencePipeline None { get; } = new(ResiliencePipeline<HttpResponseMessage>.Empty);

    /// <summary>Builds the pipeline from the settings: total timeout, then retry (only when <see cref="AiResilienceOptions.RetryCount"/> is above 0), then the circuit breaker, then the timeout per attempt.</summary>
    /// <param name="settings">Retry, timeout and circuit settings.</param>
    /// <param name="clock">Time source, replaceable in tests. Defaults to the system clock.</param>
    /// <returns>A pipeline whose circuit state belongs to this instance.</returns>
    public static AiResiliencePipeline Create(AiResilienceOptions settings, TimeProvider? clock = null)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = clock ?? TimeProvider.System };

        builder.AddTimeout(TimeSpan.FromMilliseconds(Math.Max(1, settings.TotalTimeoutMs)));

        if (settings.RetryCount > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(IsServerError),
                MaxRetryAttempts = settings.RetryCount,
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, settings.RetryDelayMs)),
                BackoffType = DelayBackoffType.Constant,
                UseJitter = true,
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return default;
                },
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
                .HandleResult(response => IsServerError(response) || response.StatusCode == HttpStatusCode.TooManyRequests),
            FailureRatio = settings.BreakerFailureRatio,
            MinimumThroughput = Math.Max(2, settings.BreakerMinimumThroughput),
            SamplingDuration = TimeSpan.FromSeconds(Math.Max(1, settings.BreakerSamplingSeconds)),
            BreakDuration = TimeSpan.FromSeconds(Math.Max(1, settings.BreakerBreakSeconds)),
            OnOpened = _ =>
            {
                FindThatBookTelemetry.RecordAiCircuit("opened");
                return default;
            },
            OnClosed = _ =>
            {
                FindThatBookTelemetry.RecordAiCircuit("closed");
                return default;
            },
        });

        builder.AddTimeout(TimeSpan.FromMilliseconds(Math.Max(1, settings.AttemptTimeoutMs)));
        return new AiResiliencePipeline(builder.Build());
    }

    /// <summary>Sends the call through the pipeline. <paramref name="send"/> runs once per attempt, so it must build a new request each time.</summary>
    /// <param name="send">Sends one attempt with the token the pipeline provides.</param>
    /// <param name="ct">Cancellation token of the caller.</param>
    /// <returns>The last response, which may carry a failure status that the caller turns into <see cref="AiUnavailableException"/>.</returns>
    /// <exception cref="AiUnavailableException">The circuit is open or the call timed out.</exception>
    /// <exception cref="HttpRequestException">The request failed after the retries.</exception>
    public async Task<HttpResponseMessage> SendAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(token => new ValueTask<HttpResponseMessage>(send(token)), ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new AiUnavailableException("AI provider is temporarily unavailable after repeated failures.", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new AiUnavailableException("AI provider timed out.", ex);
        }
    }

    /// <summary>Tells whether a response carries a 5xx status.</summary>
    /// <param name="response">The response of one attempt.</param>
    /// <returns>True for a server error.</returns>
    private static bool IsServerError(HttpResponseMessage response) => (int)response.StatusCode >= 500;
}
