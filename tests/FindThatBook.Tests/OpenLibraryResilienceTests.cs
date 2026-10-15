using System.Net;
using System.Net.Http.Headers;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FindThatBook.Tests;

public class OpenLibraryResilienceTests
{
    private const string HobbitSearch = """
        {"docs":[{"key":"/works/OL1W","title":"The Hobbit","author_name":["J.R.R. Tolkien"],
        "author_key":["OL1A"],"first_publish_year":1937,"edition_count":120}]}
        """;

    private static OpenLibraryOptions Settings(int retries = 0) => new()
    {
        RetryDelayMs = 0,
        MaxRetries = retries,
        CircuitBreakerMinimumThroughput = 2,
        CircuitBreakerBreakSeconds = 20,
    };

    private static OpenLibraryCatalog Catalog(HttpMessageHandler handler, OpenLibraryOptions settings, TimeProvider clock)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(settings),
            new OpenLibraryGate(settings.MaxConcurrentRequests, clock),
            OpenLibraryResiliencePipeline.Create(settings, clock));

    private static HttpResponseMessage Ok(HttpRequestMessage req)
        => req.RequestUri!.AbsolutePath == "/search.json" ? StubHandler.Json(HobbitSearch) : StubHandler.Json("""{"authors":[]}""");

    private static Task<IReadOnlyList<CatalogCandidate>> Search(OpenLibraryCatalog catalog)
        => catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default);

    [Fact]
    public async Task The_circuit_opens_after_repeated_failures_and_later_searches_send_no_request()
    {
        var clock = new FakeTimeProvider();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var catalog = Catalog(handler, Settings(), clock);
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));
        var sent = handler.Requests.Count;

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));

        Assert.Equal(2, sent);
        Assert.Equal(sent, handler.Requests.Count);
    }

    [Fact]
    public async Task The_circuit_closes_again_once_the_break_has_passed_and_a_request_succeeds()
    {
        var clock = new FakeTimeProvider();
        var healthy = false;
        var handler = new StubHandler(req => healthy ? Ok(req) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var catalog = Catalog(handler, Settings(), clock);
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));
        healthy = true;
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));

        clock.Advance(TimeSpan.FromSeconds(21));
        var found = await Search(catalog);

        Assert.Single(found);
    }

    [Fact]
    public async Task A_429_does_not_count_towards_the_circuit_because_the_cooldown_owns_it()
    {
        var clock = new FakeTimeProvider();
        var limited = true;
        var handler = new StubHandler(req =>
        {
            if (!limited) return Ok(req);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });
        var catalog = Catalog(handler, Settings(), clock);
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));
            clock.Advance(TimeSpan.FromSeconds(2));
        }
        limited = false;

        var found = await Search(catalog);

        Assert.Single(found);
    }

    [Fact]
    public async Task A_hung_attempt_times_out_and_is_retried()
    {
        var clock = TimeProvider.System;
        var settings = Settings(retries: 1);
        settings.AttemptTimeoutMs = 50;
        settings.CircuitBreakerMinimumThroughput = 100;
        var handler = new HangingHandler();
        var catalog = Catalog(handler, settings, clock);

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(catalog));

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task The_registered_catalogs_share_one_circuit()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenLibrary:MaxRetries"] = "0", ["OpenLibrary:RetryDelayMs"] = "0", ["OpenLibrary:CircuitBreakerMinimumThroughput"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFindThatBook(config);
        services.Configure<HttpClientFactoryOptions>(nameof(IBookCatalog), o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(provider.GetRequiredService<IBookCatalog>()));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(provider.GetRequiredService<IBookCatalog>()));
        var sent = handler.Requests.Count;

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => Search(provider.GetRequiredService<IBookCatalog>()));

        Assert.Equal(sent, handler.Requests.Count);
    }

    private static Task<IReadOnlyList<CatalogCandidate>> Search(IBookCatalog catalog)
        => catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default);

    [Theory]
    [InlineData("AttemptTimeoutMs", "OPENLIBRARY_ATTEMPT_TIMEOUT_MS", "10000")]
    [InlineData("CircuitBreakerFailureRatio", "OPENLIBRARY_CIRCUIT_FAILURE_RATIO", "0.5")]
    [InlineData("CircuitBreakerMinimumThroughput", "OPENLIBRARY_CIRCUIT_MINIMUM_THROUGHPUT", "6")]
    [InlineData("CircuitBreakerSamplingSeconds", "OPENLIBRARY_CIRCUIT_SAMPLING_SECONDS", "30")]
    [InlineData("CircuitBreakerBreakSeconds", "OPENLIBRARY_CIRCUIT_BREAK_SECONDS", "20")]
    public void Compose_and_the_env_sample_map_each_open_library_resilience_setting_with_its_default(string setting, string variable, string expectedDefault)
    {
        var compose = RepoFiles.Text("docker-compose.yml");
        var sample = RepoFiles.Text(".env.sample");

        Assert.Contains($"OpenLibrary__{setting}: ${{{variable}:-{expectedDefault}}}", compose);
        Assert.Contains(variable + "=", sample);
        Assert.Equal(expectedDefault, Convert.ToString(typeof(OpenLibraryOptions).GetProperty(setting)!.GetValue(new OpenLibraryOptions()), System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }
}
