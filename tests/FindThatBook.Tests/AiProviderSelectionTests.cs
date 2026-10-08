using FindThatBook.Infrastructure;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FindThatBook.Tests;

public class AiProviderSelectionTests
{
    private static IServiceCollection Services(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFindThatBook(config);
        return services;
    }

    private static IAiClient Resolve(params (string Key, string? Value)[] settings)
        => Services(settings).BuildServiceProvider().GetRequiredService<IAiClient>();

    [Fact]
    public void Gemini_is_the_default_provider()
        => Assert.IsType<GeminiClient>(Resolve());

    [Theory]
    [InlineData("Gemini")]
    [InlineData("gemini")]
    [InlineData("")]
    public void Gemini_can_be_selected_explicitly(string provider)
        => Assert.IsType<GeminiClient>(Resolve(("Ai:Provider", provider)));

    [Theory]
    [InlineData("OpenAiCompatible")]
    [InlineData("openaicompatible")]
    public void Openai_compatible_can_be_selected_by_configuration(string provider)
        => Assert.IsType<OpenAiCompatibleClient>(Resolve(("Ai:Provider", provider)));

    [Fact]
    public void Unknown_provider_fails_fast_with_a_helpful_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Services(("Ai:Provider", "Claude")));

        Assert.Contains("Ai:Provider", ex.Message);
        Assert.Contains("Gemini", ex.Message);
        Assert.Contains("OpenAiCompatible", ex.Message);
    }

    [Fact]
    public void Only_the_selected_client_is_registered()
    {
        var openAi = Services(("Ai:Provider", "OpenAiCompatible")).BuildServiceProvider();

        Assert.IsNotType<GeminiClient>(openAi.GetRequiredService<IAiClient>());
        Assert.NotNull(openAi.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiCompatibleOptions>>());
    }
}
