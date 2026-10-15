namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>Settings for the retry, timeout and circuit breaker around Open Library requests, bound from the <c>OpenLibrary:Resilience</c> section.</summary>
public sealed class OpenLibraryResilienceOptions
{
    /// <summary>Retries after a 5xx response, timeout, network error or invalid JSON. 429 and other 4xx responses are never retried.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base delay in milliseconds before a retry; it is multiplied by the attempt number.</summary>
    public int RetryDelayMs { get; set; } = 300;

    /// <summary>Time in milliseconds one attempt may take before it is cancelled and counted as a failure.</summary>
    public int AttemptTimeoutMs { get; set; } = 10_000;

    /// <summary>Share of failed requests, from 0 to 1, within <see cref="CircuitBreakerSamplingSeconds"/> that opens the circuit. 429 responses do not count, the cooldown handles them.</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Fewest requests in the sampling window before the circuit may open. Must be at least 2.</summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 6;

    /// <summary>Length in seconds of the window in which failures are counted.</summary>
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>Seconds an open circuit rejects requests before it lets one through to test Open Library again.</summary>
    public int CircuitBreakerBreakSeconds { get; set; } = 20;

    /// <summary>Lists the settings that are out of range. Messages name the setting and never print its value.</summary>
    /// <returns>One message per invalid setting, empty when all are valid.</returns>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (AttemptTimeoutMs <= 0) problems.Add("OpenLibrary:Resilience:AttemptTimeoutMs must be greater than 0.");
        if (CircuitBreakerFailureRatio is <= 0 or > 1) problems.Add("OpenLibrary:Resilience:CircuitBreakerFailureRatio must be above 0 and at most 1.");
        if (CircuitBreakerMinimumThroughput < 2) problems.Add("OpenLibrary:Resilience:CircuitBreakerMinimumThroughput must be at least 2.");
        if (CircuitBreakerSamplingSeconds <= 0) problems.Add("OpenLibrary:Resilience:CircuitBreakerSamplingSeconds must be greater than 0.");
        if (CircuitBreakerBreakSeconds <= 0) problems.Add("OpenLibrary:Resilience:CircuitBreakerBreakSeconds must be greater than 0.");
        return problems;
    }
}
