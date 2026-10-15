using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Health;

/// <summary>The selected AI provider and the generation values sent to it.</summary>
/// <param name="Provider">Either <c>Gemini</c> or <c>OpenAiCompatible</c>.</param>
/// <param name="Temperature">The temperature sent, after the fallback to the default for an invalid value.</param>
/// <param name="TopK">The top-k sent, or null when none is sent.</param>
/// <param name="TopP">The top-p sent, or null when none is sent.</param>
/// <param name="Seed">The seed sent, or null when none is sent.</param>
/// <param name="ReasoningEffort">The reasoning effort sent (trimmed, lower case), or null when none is sent.</param>
/// <param name="Model">The model in use for the selected provider, after the blank-value fallback to that provider's default.</param>
public sealed record AiConfig(
    string Provider, double Temperature = AiGenerationOptions.DefaultTemperature, int? TopK = null, double? TopP = null, int? Seed = null, string? ReasoningEffort = null, string? Model = null);

/// <summary>Gemini settings.</summary>
/// <param name="ApiKey">The masked key, or <c>not set</c>.</param>
public sealed record GeminiConfig(string ApiKey);

/// <summary>OpenAI-compatible settings.</summary>
/// <param name="ApiKey">The masked key, or <c>not set</c>.</param>
/// <param name="BaseUrl">The API root in use, after the blank-value fallback and without credentials.</param>
public sealed record OpenAiCompatibleConfig(string ApiKey, string BaseUrl);

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
    /// <param name="generation">The generation settings (temperature, top-k, top-p, seed, reasoning effort). When null, the defaults are shown.</param>
    /// <returns>A dump that never contains an API key.</returns>
    public static ConfigDump Build(AiProviderSelection provider, GeminiOptions gemini, OpenAiCompatibleOptions openAi, OpenLibraryOptions openLibrary, AiGenerationOptions? generation = null)
        => new(
            new AiConfig(provider.Name, (generation ?? new AiGenerationOptions()).EffectiveTemperature, generation?.EffectiveTopK, generation?.EffectiveTopP, generation?.EffectiveSeed, generation?.EffectiveReasoningEffort, (generation ?? new AiGenerationOptions()).ModelOr(provider.Name == AiProviderSelection.Gemini ? GeminiOptions.DefaultModel : OpenAiCompatibleOptions.DefaultModel)),
            new GeminiConfig(Mask(gemini.ApiKey)),
            new OpenAiCompatibleConfig(Mask(openAi.ApiKey), WithoutCredentials(Or(openAi.BaseUrl, OpenAiCompatibleOptions.DefaultBaseUrl))),
            new OpenLibraryConfig(
                WithoutCredentials(openLibrary.BaseUrl), openLibrary.Resilience.MaxRetries, openLibrary.Resilience.RetryDelayMs, openLibrary.MaxDetailLookups,
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
