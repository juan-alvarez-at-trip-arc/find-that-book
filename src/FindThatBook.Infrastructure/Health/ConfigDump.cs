using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Health;

/// <summary>The selected AI provider.</summary>
/// <param name="Provider">Either <c>Gemini</c> or <c>OpenAiCompatible</c>.</param>
public sealed record AiConfig(string Provider);

/// <summary>Gemini settings.</summary>
/// <param name="ApiKey">The masked key, or <c>not set</c>.</param>
/// <param name="Model">The model in use, after the blank-value fallback.</param>
public sealed record GeminiConfig(string ApiKey, string Model);

/// <summary>OpenAI-compatible settings.</summary>
/// <param name="ApiKey">The masked key, or <c>not set</c>.</param>
/// <param name="BaseUrl">The API root in use, after the blank-value fallback and without credentials.</param>
/// <param name="Model">The model in use, after the blank-value fallback.</param>
public sealed record OpenAiCompatibleConfig(string ApiKey, string BaseUrl, string Model);

/// <summary>Open Library settings.</summary>
/// <param name="BaseUrl">The API root, without credentials.</param>
/// <param name="MaxRetries">Retries after a 5xx, timeout or network error.</param>
/// <param name="RetryDelayMs">Base retry delay in milliseconds.</param>
/// <param name="MaxDetailLookups">Works per search that get a detail lookup.</param>
/// <param name="MaxAuthorsPerWork">Authors looked up per work.</param>
/// <param name="MaxConcurrentRequests">Simultaneous Open Library requests.</param>
/// <param name="DefaultCooldownSeconds">Pause after a 429 without Retry-After.</param>
/// <param name="Contact">Contact sent in the User-Agent, if configured.</param>
/// <param name="UserAgent">The User-Agent header value sent to Open Library.</param>
public sealed record OpenLibraryConfig(
    string BaseUrl, int MaxRetries, int RetryDelayMs, int MaxDetailLookups, int MaxAuthorsPerWork,
    int MaxConcurrentRequests, int DefaultCooldownSeconds, string? Contact, string UserAgent);

/// <summary>The effective application configuration, safe to return over HTTP: API keys are masked and URL credentials are removed.</summary>
/// <param name="Ai">The selected AI provider.</param>
/// <param name="Gemini">Gemini settings.</param>
/// <param name="OpenAiCompatible">OpenAI-compatible settings.</param>
/// <param name="OpenLibrary">Open Library settings.</param>
public sealed record ConfigDump(AiConfig Ai, GeminiConfig Gemini, OpenAiCompatibleConfig OpenAiCompatible, OpenLibraryConfig OpenLibrary)
{
    /// <summary>Builds the dump from the bound options.</summary>
    /// <param name="provider">The selected AI provider.</param>
    /// <param name="gemini">Gemini settings.</param>
    /// <param name="openAi">OpenAI-compatible settings.</param>
    /// <param name="openLibrary">Open Library settings.</param>
    /// <returns>A dump that never contains an API key.</returns>
    public static ConfigDump Build(AiProviderSelection provider, GeminiOptions gemini, OpenAiCompatibleOptions openAi, OpenLibraryOptions openLibrary)
        => new(
            new AiConfig(provider.Name),
            new GeminiConfig(Mask(gemini.ApiKey), Or(gemini.Model, GeminiOptions.DefaultModel)),
            new OpenAiCompatibleConfig(Mask(openAi.ApiKey), WithoutCredentials(Or(openAi.BaseUrl, OpenAiCompatibleOptions.DefaultBaseUrl)), Or(openAi.Model, OpenAiCompatibleOptions.DefaultModel)),
            new OpenLibraryConfig(
                WithoutCredentials(openLibrary.BaseUrl), openLibrary.MaxRetries, openLibrary.RetryDelayMs, openLibrary.MaxDetailLookups,
                openLibrary.MaxAuthorsPerWork, openLibrary.MaxConcurrentRequests, openLibrary.DefaultCooldownSeconds,
                openLibrary.Contact, openLibrary.UserAgent));

    private const int VisibleTail = 4;
    private const int MinLengthToShowTail = 16;

    /// <summary>Masks a key so it can be recognised but not used: only the last four characters stay visible, and keys shorter than sixteen characters are hidden completely.</summary>
    private static string Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "not set";
        key = key.Trim();
        return key.Length < MinLengthToShowTail
            ? new string('*', key.Length)
            : new string('*', key.Length - VisibleTail) + key[^VisibleTail..];
    }

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string WithoutCredentials(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length == 0) return url;
        return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.ToString();
    }
}
