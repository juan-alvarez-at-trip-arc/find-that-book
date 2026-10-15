using FindThatBook.Infrastructure.Ai;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure;

/// <summary>Dependency injection registration for the Infrastructure layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The most bytes any outgoing HTTP response may hold (2 MB). Open Library records and AI replies are a few kilobytes, so a larger body is a fault or an attack, and a request that meets one fails instead of filling memory.</summary>
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>The safety timeout of the AI HTTP client: a little longer than the total timeout of the resilience pipeline, so the pipeline always ends a call first.</summary>
    /// <param name="sp">Service provider used to read <see cref="AiResilienceOptions"/>.</param>
    /// <returns>The total timeout plus 5 seconds.</returns>
    private static TimeSpan AiClientTimeout(IServiceProvider sp)
        => TimeSpan.FromMilliseconds(Math.Max(1, sp.GetRequiredService<IOptions<AiResilienceOptions>>().Value.TotalTimeoutMs) + 5000);

    /// <summary>Registers the Open Library catalog, the selected AI client, the AI-first parsers and writers with their non-AI fallbacks, and the search service.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="config">Application configuration. <c>Ai:Provider</c> selects <c>Gemini</c> (default) or <c>OpenAiCompatible</c>. <c>Ai:Temperature</c>, <c>Ai:TopK</c> and <c>Ai:TopP</c> set the sampling parameters sent to either provider.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>Ai:Provider</c> names an unknown provider.</exception>
    public static IServiceCollection AddFindThatBook(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureHttpClientDefaults(builder => builder.ConfigureHttpClient(client => client.MaxResponseContentBufferSize = MaxResponseBytes));
        services.Configure<AiGenerationOptions>(config.GetSection("Ai"));
        services.Configure<GeminiOptions>(config.GetSection("Gemini"));
        services.Configure<OpenLibraryOptions>(config.GetSection("OpenLibrary"));
        services.Configure<AiResilienceOptions>(config.GetSection("Ai:Resilience"));
        services.AddSingleton(sp => AiResiliencePipeline.Create(sp.GetRequiredService<IOptions<AiResilienceOptions>>().Value));
        services.AddMemoryCache(options => options.SizeLimit = 2000);

        services.AddHttpClient<IBookCatalog, OpenLibraryCatalog>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.UserAgent);
        });
        services.AddSingleton(sp => new OpenLibraryGate(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.MaxConcurrentRequests));
        services.AddSingleton(sp => OpenLibraryResiliencePipeline.Create(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value));

        services.Configure<OpenAiCompatibleOptions>(config.GetSection("OpenAiCompatible"));

        var provider = (config["Ai:Provider"] ?? "").Trim();
        if (provider.Length == 0 || provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiClient, GeminiClient>((sp, client) =>
            {
                client.BaseAddress = new Uri(GeminiOptions.ApiBaseUrl);
                client.Timeout = AiClientTimeout(sp);
            });
            services.AddSingleton(new AiProviderSelection(AiProviderSelection.Gemini));
            services.AddSingleton<IHealthProbe, GeminiProbe>();
        }
        else if (provider.Equals("OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiClient, OpenAiCompatibleClient>((sp, client) =>
            {
                client.BaseAddress = sp.GetRequiredService<IOptions<OpenAiCompatibleOptions>>().Value.BaseUri;
                client.Timeout = AiClientTimeout(sp);
            });
            services.AddSingleton(new AiProviderSelection(AiProviderSelection.OpenAiCompatible));
            services.AddSingleton<IHealthProbe, OpenAiCompatibleProbe>();
        }
        else
        {
            throw new InvalidOperationException($"Unknown Ai:Provider '{provider}'. Use 'Gemini' or 'OpenAiCompatible'.");
        }

        services.AddHttpClient(ProbeRunner.ClientName);
        services.AddSingleton<IHealthProbe, OpenLibraryProbe>();
        services.AddSingleton<HealthReportService>();

        services.AddTransient<IQueryParser>(sp => new FallbackQueryParser(
            new AiQueryParser(sp.GetRequiredService<IAiClient>()),
            new HeuristicQueryParser(),
            sp.GetRequiredService<ILogger<FallbackQueryParser>>()));

        services.AddTransient<IExplanationWriter>(sp => new FallbackExplanationWriter(
            new AiExplanationWriter(sp.GetRequiredService<IAiClient>()),
            new TemplateExplanationWriter(),
            sp.GetRequiredService<ILogger<FallbackExplanationWriter>>()));

        services.AddTransient<ISearchService, SearchService>();
        return services;
    }
}
