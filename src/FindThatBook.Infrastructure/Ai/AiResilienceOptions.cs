namespace FindThatBook.Infrastructure.Ai;

/// <summary>Settings for the retry, timeout and circuit breaker around AI calls, bound from the <c>Ai:Resilience</c> section.</summary>
public sealed class AiResilienceOptions
{
    /// <summary>Retries after a 5xx response or a network error. A timeout, a 429 and other 4xx responses are never retried, because a retry would only add waiting time or load.</summary>
    public int RetryCount { get; set; } = 1;

    /// <summary>Delay in milliseconds before a retry.</summary>
    public int RetryDelayMs { get; set; } = 300;

    /// <summary>Time in milliseconds one attempt may take before it is cancelled and counted as a failure.</summary>
    public int AttemptTimeoutMs { get; set; } = 30_000;

    /// <summary>Time in milliseconds the whole call may take, retries included.</summary>
    public int TotalTimeoutMs { get; set; } = 45_000;

    /// <summary>Share of failed calls, from 0 to 1, within <see cref="BreakerSamplingSeconds"/> that opens the circuit. A 429 counts as a failure here.</summary>
    public double BreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Fewest calls in the sampling window before the circuit may open. Must be at least 2.</summary>
    public int BreakerMinimumThroughput { get; set; } = 4;

    /// <summary>Length in seconds of the window in which failures are counted.</summary>
    public int BreakerSamplingSeconds { get; set; } = 30;

    /// <summary>Seconds an open circuit rejects calls, so searches use the non-AI fallback at once, before it lets one through to test the provider again.</summary>
    public int BreakerBreakSeconds { get; set; } = 30;

    /// <summary>Lists the settings that are out of range. Messages name the setting and never print its value.</summary>
    /// <returns>One message per invalid setting, empty when all are valid.</returns>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (RetryCount < 0) problems.Add("Ai:Resilience:RetryCount must not be negative.");
        if (RetryDelayMs < 0) problems.Add("Ai:Resilience:RetryDelayMs must not be negative.");
        if (AttemptTimeoutMs <= 0) problems.Add("Ai:Resilience:AttemptTimeoutMs must be greater than 0.");
        if (TotalTimeoutMs < AttemptTimeoutMs) problems.Add("Ai:Resilience:TotalTimeoutMs must not be less than AttemptTimeoutMs.");
        if (BreakerFailureRatio is <= 0 or > 1) problems.Add("Ai:Resilience:BreakerFailureRatio must be above 0 and at most 1.");
        if (BreakerMinimumThroughput < 2) problems.Add("Ai:Resilience:BreakerMinimumThroughput must be at least 2.");
        if (BreakerSamplingSeconds <= 0) problems.Add("Ai:Resilience:BreakerSamplingSeconds must be greater than 0.");
        if (BreakerBreakSeconds <= 0) problems.Add("Ai:Resilience:BreakerBreakSeconds must be greater than 0.");
        return problems;
    }
}
