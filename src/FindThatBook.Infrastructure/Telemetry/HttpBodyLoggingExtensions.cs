using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Telemetry;

/// <summary>Dependency injection registration for outgoing HTTP traffic logging.</summary>
public static class HttpBodyLoggingExtensions
{
    /// <summary>Adds <see cref="HttpLoggingHandler"/> to every <see cref="HttpClient"/> created by the factory, with a masker that knows the configured API keys and the Open Library contact.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddHttpBodyLogging(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SensitiveDataMasker(
        [
            sp.GetRequiredService<IOptions<GeminiOptions>>().Value.ApiKey,
            sp.GetRequiredService<IOptions<OpenAiCompatibleOptions>>().Value.ApiKey,
            sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.Contact,
        ]));
        services.AddTransient<HttpLoggingHandler>();
        services.ConfigureHttpClientDefaults(builder => builder.AddHttpMessageHandler<HttpLoggingHandler>());
        return services;
    }
}
