namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>Settings for the Open Library client: address, retry policy, request volume limits and the User-Agent contact.</summary>
public sealed class OpenLibraryOptions
{
    /// <summary>Base address of the Open Library API.</summary>
    public string BaseUrl { get; set; } = "https://openlibrary.org/";
    /// <summary>Retries after a 5xx response, timeout or network error. 429 and other 4xx responses are never retried.</summary>
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

    /// <summary>How many works per search get a detail lookup (works plus authors). Each one costs several requests.</summary>
    public int MaxDetailLookups { get; set; } = 6;

    /// <summary>Authors looked up per work; the rest are treated as contributors.</summary>
    public int MaxAuthorsPerWork { get; set; } = 3;

    /// <summary>Open Library requests allowed at the same time across the whole process.</summary>
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>Pause after an HTTP 429 that has no usable Retry-After header.</summary>
    public int DefaultCooldownSeconds { get; set; } = 30;

    /// <summary>Optional contact (email or URL) so Open Library can reach the operator; sent in the User-Agent.</summary>
    public string? Contact { get; set; }

    /// <summary>User-Agent header value sent to Open Library, which identifies the app and includes <see cref="Contact"/> when set.</summary>
    public string UserAgent => $"FindThatBook/1.0 ({(string.IsNullOrWhiteSpace(Contact) ? "coding challenge" : Contact.Trim())})";
}
