namespace FindThatBook.Infrastructure.OpenAi;

/// <summary>Settings for the OpenAI-compatible provider, bound from the <c>OpenAiCompatible</c> configuration section.</summary>
public sealed class OpenAiCompatibleOptions
{
    /// <summary>The base URL used when none is configured (Groq).</summary>
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1/";
    /// <summary>The model name used when none is configured.</summary>
    public const string DefaultModel = "openai/gpt-oss-20b";

    /// <summary>The API key sent as a bearer token. Optional because local servers such as Ollama need no key.</summary>
    public string? ApiKey { get; set; }
    /// <summary>The provider's API root. A blank value falls back to <see cref="DefaultBaseUrl"/>.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    /// <summary>The model name. A blank value falls back to <see cref="DefaultModel"/>.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>The base address as a <see cref="System.Uri"/> with a trailing slash so relative paths append instead of replace. A blank <see cref="BaseUrl"/> uses <see cref="DefaultBaseUrl"/>.</summary>
    public Uri BaseUri
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
            return new Uri(url.EndsWith('/') ? url : url + "/");
        }
    }
}
