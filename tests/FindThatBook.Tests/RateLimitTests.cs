using System.Net;
using System.Net.Http.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Tests;

public class RateLimitTests
{
    private static WebApplicationFactory<Program> Factory(
        int perMinute = 100, int concurrent = 100, Func<IReadOnlyList<ParsedQuery>, IReadOnlyList<CatalogCandidate>>? catalog = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gemini:ApiKey", "");
            b.UseSetting("RateLimit:SearchPermitsPerMinute", perMinute.ToString());
            b.UseSetting("RateLimit:MaxConcurrentSearches", concurrent.ToString());
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IBookCatalog>();
                s.AddSingleton<IBookCatalog>(new FakeCatalog(catalog ?? (_ => [Books.Make("The Hobbit", ["J.R.R. Tolkien"], id: "OL1W")])));
            });
        });

    private static Task<HttpResponseMessage> Search(HttpClient client)
        => client.PostAsJsonAsync("/api/search", new { query = "tolkien hobbit" });

    [Fact]
    public async Task Search_returns_429_with_retry_after_once_the_per_client_limit_is_used()
    {
        var client = Factory(perMinute: 2).CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Search(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Search(client)).StatusCode);
        var limited = await Search(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/api/search/")]
    [InlineData("/API/Search/")]
    public async Task Search_is_limited_on_path_variants_that_routing_still_accepts(string path)
    {
        var client = Factory(perMinute: 2).CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { query = "tolkien hobbit" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { query = "tolkien hobbit" })).StatusCode);
        var limited = await client.PostAsJsonAsync(path, new { query = "tolkien hobbit" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Search_limit_is_shared_between_the_plain_and_trailing_slash_paths()
    {
        var client = Factory(perMinute: 2).CreateClient();

        await Search(client);
        await client.PostAsJsonAsync("/api/search/", new { query = "tolkien hobbit" });
        var limited = await Search(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Health_and_static_paths_are_not_rate_limited()
    {
        var client = Factory(perMinute: 1).CreateClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Search_rejects_requests_beyond_the_concurrent_search_cap()
    {
        var release = new ManualResetEventSlim();
        var entered = new SemaphoreSlim(0);
        var client = Factory(concurrent: 1, catalog: _ =>
        {
            entered.Release();
            release.Wait(TimeSpan.FromSeconds(10));
            return [Books.Make("The Hobbit", ["J.R.R. Tolkien"], id: "OL1W")];
        }).CreateClient();

        var first = Task.Run(() => Search(client));
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)));
        var second = await Search(client);
        release.Set();

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }
}
