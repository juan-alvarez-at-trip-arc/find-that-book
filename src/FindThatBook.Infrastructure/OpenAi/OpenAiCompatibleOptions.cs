namespace FindThatBook.Infrastructure.OpenAi;

public sealed class OpenAiCompatibleOptions
{
    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1/";
    public const string DefaultModel = "openai/gpt-oss-20b";

    /// <summary>Optional: local servers such as Ollama need no key.</summary>
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string Model { get; set; } = DefaultModel;

    /// <summary>Base address with a trailing slash so relative paths append instead of replace.</summary>
    public Uri BaseUri
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
            return new Uri(url.EndsWith('/') ? url : url + "/");
        }
    }
}
