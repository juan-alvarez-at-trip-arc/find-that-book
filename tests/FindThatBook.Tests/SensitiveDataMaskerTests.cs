using FindThatBook.Infrastructure.Telemetry;

namespace FindThatBook.Tests;

public class SensitiveDataMaskerTests
{
    private static readonly SensitiveDataMasker Masker = new(["sk-live-123456", "noname@acme.org"]);

    [Theory]
    [InlineData("Authorization")]
    [InlineData("x-goog-api-key")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    public void Masks_sensitive_header_values(string name)
    {
        Assert.Equal("***", Masker.MaskHeader(name, "anything"));
    }

    [Fact]
    public void Keeps_other_header_values_but_masks_configured_secrets_inside_them()
    {
        Assert.Equal("application/json", Masker.MaskHeader("Accept", "application/json"));
        Assert.Equal("FindThatBook/1.0 (***)", Masker.MaskHeader("User-Agent", "FindThatBook/1.0 (noname@acme.org)"));
    }

    [Fact]
    public void Masks_credential_query_parameters()
    {
        var masked = Masker.MaskText("https://x.test/v1/models?key=abc123&q=hobbit&access_token=zzz");

        Assert.Equal("https://x.test/v1/models?key=***&q=hobbit&access_token=***", masked);
    }

    [Fact]
    public void Masks_credential_fields_in_json_bodies_and_keeps_other_fields()
    {
        var masked = Masker.MaskText("""{"apiKey":"abc","title":"The Hobbit","client_secret": "s3","maxTokens":50}""");

        Assert.Equal("""{"apiKey":"***","title":"The Hobbit","client_secret": "***","maxTokens":50}""", masked);
    }

    [Fact]
    public void Masks_bearer_tokens_and_configured_secret_values_anywhere()
    {
        var masked = Masker.MaskText("Bearer abc.def and the key sk-live-123456 was echoed by noname@acme.org");

        Assert.DoesNotContain("abc.def", masked);
        Assert.DoesNotContain("sk-live-123456", masked);
        Assert.DoesNotContain("noname@acme.org", masked);
    }

    [Fact]
    public void Ignores_blank_and_very_short_secret_values()
    {
        var masker = new SensitiveDataMasker([null, "", "  ", "ab"]);

        Assert.Equal("a b about", masker.MaskText("a b about"));
    }
}
