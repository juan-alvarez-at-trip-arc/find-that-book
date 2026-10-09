using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Validates configuration values without any network call. Messages name settings but never print their values.</summary>
public static class HealthConfigCheck
{
    /// <summary>Checks the Open Library settings and the settings of the selected AI provider.</summary>
    /// <param name="provider">The selected AI provider. Only its settings are checked.</param>
    /// <param name="gemini">Gemini settings.</param>
    /// <param name="openAi">OpenAI-compatible settings.</param>
    /// <param name="openLibrary">Open Library settings.</param>
    /// <returns><see cref="HealthStatus.Down"/> for an invalid value, <see cref="HealthStatus.Degraded"/> for a missing optional value, otherwise <see cref="HealthStatus.Ok"/>.</returns>
    public static ConfigHealth Validate(AiProviderSelection provider, GeminiOptions gemini, OpenAiCompatibleOptions openAi, OpenLibraryOptions openLibrary)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!IsHttpUrl(openLibrary.BaseUrl))
            errors.Add("OpenLibrary:BaseUrl must be an absolute http or https URL.");
        RequirePositive(errors, "OpenLibrary:MaxDetailLookups", openLibrary.MaxDetailLookups);
        RequirePositive(errors, "OpenLibrary:MaxAuthorsPerWork", openLibrary.MaxAuthorsPerWork);
        RequirePositive(errors, "OpenLibrary:MaxConcurrentRequests", openLibrary.MaxConcurrentRequests);
        RequirePositive(errors, "OpenLibrary:DefaultCooldownSeconds", openLibrary.DefaultCooldownSeconds);

        if (provider.Name == AiProviderSelection.Gemini)
        {
            if (string.IsNullOrWhiteSpace(gemini.ApiKey))
                warnings.Add("Gemini:ApiKey is not set; the app uses the non-AI fallback.");
        }
        else if (!string.IsNullOrWhiteSpace(openAi.BaseUrl) && !IsHttpUrl(openAi.BaseUrl))
        {
            errors.Add("OpenAiCompatible:BaseUrl must be an absolute http or https URL.");
        }

        var status = errors.Count > 0 ? HealthStatus.Down : warnings.Count > 0 ? HealthStatus.Degraded : HealthStatus.Ok;
        return new ConfigHealth(status, [.. errors, .. warnings]);
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void RequirePositive(List<string> errors, string setting, int value)
    {
        if (value <= 0) errors.Add($"{setting} must be greater than 0.");
    }
}
