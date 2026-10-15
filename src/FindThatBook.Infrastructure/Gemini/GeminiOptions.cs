namespace FindThatBook.Infrastructure.Gemini;

/// <summary>Settings for the Gemini provider, bound from the <c>Ai:Gemini</c> configuration section. The model name is <c>Ai:Model</c>.</summary>
public sealed class GeminiOptions
{
    /// <summary>The Gemini API root.</summary>
    public const string ApiBaseUrl = "https://generativelanguage.googleapis.com/";
    /// <summary>The model name used when none is configured.</summary>
    public const string DefaultModel = "gemini-3.8-flash";

    /// <summary>The Gemini API key. When blank, the client throws <see cref="FindThatBook.Application.Abstractions.AiUnavailableException"/> and the app runs in fallback mode.</summary>
    public string? ApiKey { get; set; }
}
