using FindThatBook.Infrastructure.Ai;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFindThatBook(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GeminiOptions>(config.GetSection("Gemini"));
        services.Configure<OpenLibraryOptions>(config.GetSection("OpenLibrary"));
        services.AddMemoryCache(options => options.SizeLimit = 2000);

        services.AddHttpClient<IBookCatalog, OpenLibraryCatalog>((sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FindThatBook/1.0 (coding challenge)");
        });

        services.Configure<OpenAiCompatibleOptions>(config.GetSection("OpenAiCompatible"));

        var provider = (config["Ai:Provider"] ?? "").Trim();
        if (provider.Length == 0 || provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiClient, GeminiClient>(client =>
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else if (provider.Equals("OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiClient, OpenAiCompatibleClient>((sp, client) =>
            {
                client.BaseAddress = sp.GetRequiredService<IOptions<OpenAiCompatibleOptions>>().Value.BaseUri;
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else
        {
            throw new InvalidOperationException($"Unknown Ai:Provider '{provider}'. Use 'Gemini' or 'OpenAiCompatible'.");
        }

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
