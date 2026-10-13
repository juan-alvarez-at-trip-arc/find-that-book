using System.Text.RegularExpressions;

namespace FindThatBook.Infrastructure.Telemetry;

/// <summary>Replaces credentials and other sensitive values with <see cref="Mask"/> so that HTTP traffic can be logged safely.</summary>
public sealed class SensitiveDataMasker
{
    /// <summary>The text that replaces every masked value.</summary>
    public const string Mask = "***";

    private const int MinSecretLength = 4;

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "x-goog-api-key", "x-api-key", "Cookie", "Set-Cookie",
    };

    private static readonly Regex QueryParameter = new(
        "([?&](?:key|api[_-]?key|token|access_token)=)[^&\\s\"]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonField = new(
        "(\"[^\"\\\\]*(?:api[_-]?key|token|secret|password|authorization)[^\"\\\\]*\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerToken = new("\\bBearer\\s+[^\\s\"]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string[] _secrets;

    /// <summary>Creates a masker that also hides the configured secret values wherever they appear.</summary>
    /// <param name="secrets">Configured secret values such as API keys and the contact email. Blank values and values shorter than four characters are ignored.</param>
    public SensitiveDataMasker(IEnumerable<string?> secrets)
    {
        _secrets = secrets
            .Select(s => s?.Trim())
            .Where(s => s is { Length: >= MinSecretLength })
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    /// <summary>Masks a header value. Credential headers are replaced entirely and other headers only lose configured secrets.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The value that is safe to log.</returns>
    public string MaskHeader(string name, string value)
        => SensitiveHeaders.Contains(name) ? Mask : MaskText(value);

    /// <summary>Masks configured secrets, bearer tokens, credential query parameters and credential JSON fields in a URL or body.</summary>
    /// <param name="text">The URL or body text.</param>
    /// <returns>The text that is safe to log.</returns>
    public string MaskText(string text)
    {
        foreach (var secret in _secrets)
            text = text.Replace(secret, Mask, StringComparison.Ordinal);

        text = BearerToken.Replace(text, "Bearer " + Mask);
        text = QueryParameter.Replace(text, "$1" + Mask);
        return JsonField.Replace(text, "$1\"" + Mask + "\"");
    }
}
