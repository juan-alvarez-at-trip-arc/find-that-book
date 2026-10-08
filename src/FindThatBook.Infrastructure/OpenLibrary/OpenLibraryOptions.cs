namespace FindThatBook.Infrastructure.OpenLibrary;

public sealed class OpenLibraryOptions
{
    public string BaseUrl { get; set; } = "https://openlibrary.org/";
    public int MaxRetries { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 300;

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

    public string UserAgent => $"FindThatBook/1.0 ({(string.IsNullOrWhiteSpace(Contact) ? "coding challenge" : Contact.Trim())})";
}
