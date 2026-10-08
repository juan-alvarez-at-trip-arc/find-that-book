using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Infrastructure.Gemini;
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

        services.AddHttpClient<IGeminiClient, GeminiClient>(client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddTransient<IQueryParser>(sp => new FallbackQueryParser(
            new GeminiQueryParser(sp.GetRequiredService<IGeminiClient>()),
            new HeuristicQueryParser(),
            sp.GetRequiredService<ILogger<FallbackQueryParser>>()));

        services.AddTransient<IExplanationWriter>(sp => new FallbackExplanationWriter(
            new GeminiExplanationWriter(sp.GetRequiredService<IGeminiClient>()),
            new TemplateExplanationWriter(),
            sp.GetRequiredService<ILogger<FallbackExplanationWriter>>()));

        services.AddTransient<ISearchService, SearchService>();
        return services;
    }
}
