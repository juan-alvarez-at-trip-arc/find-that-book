using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Tests;

public class ApiTests
{
    private static readonly CatalogCandidate Hobbit =
        Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"], 1937, 120, id: "OL1W");

    private static HttpClient CreateClient(
        Func<IReadOnlyList<ParsedQuery>, IReadOnlyList<CatalogCandidate>>? catalog = null,
        IGeminiClient? gemini = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Gemini:ApiKey", "");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBookCatalog>();
                services.AddSingleton<IBookCatalog>(new FakeCatalog(catalog ?? (_ => [Hobbit])));
                if (gemini is not null)
                {
                    services.RemoveAll<IGeminiClient>();
                    services.AddSingleton(gemini);
                }
            });
        });
        return factory.CreateClient();
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>());

    [Fact]
    public async Task Without_a_gemini_key_search_still_works_and_reports_ai_unused()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "tolkien hobbit illustrated deluxe 1937" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.False(body.GetProperty("aiUsed").GetBoolean());
        var first = body.GetProperty("results")[0];
        Assert.Equal("The Hobbit", first.GetProperty("title").GetString());
        Assert.Equal(1937, first.GetProperty("first_publish_year").GetInt32());
        Assert.Contains("Exact title match", first.GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task With_gemini_available_ai_used_is_true()
    {
        var gemini = new FakeGeminiClient(prompt => prompt.Contains("Extract book search fields")
            ? """{"title":"The Hobbit","author":"J.R.R. Tolkien","keywords":["1937"]}"""
            : """[{"index":0,"explanation":"Exact title; Tolkien is primary author; Dixon listed as contributor."}]""");
        var client = CreateClient(gemini: gemini);

        var body = await Body(await client.PostAsJsonAsync("/api/search", new { query = "tolkien hobbit 1937" }));

        Assert.True(body.GetProperty("aiUsed").GetBoolean());
        Assert.StartsWith("Exact title; Tolkien", body.GetProperty("results")[0].GetProperty("explanation").GetString());
    }

    [Fact]
    public async Task Malformed_gemini_output_degrades_instead_of_failing()
    {
        var client = CreateClient(gemini: new FakeGeminiClient(_ => "```not json```"));

        var response = await client.PostAsJsonAsync("/api/search", new { query = "tolkien hobbit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await Body(response)).GetProperty("aiUsed").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_query_returns_400_problem_details(string query)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/search", new { query });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Missing_body_returns_400()
    {
        var response = await CreateClient().PostAsync("/api/search", new StringContent("", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("1937")]
    [InlineData("???")]
    public async Task Query_with_no_words_returns_200_with_empty_results(string query)
    {
        var client = CreateClient(catalog: h => h.Count == 0 ? [] : throw new InvalidOperationException("catalog must not be searched"));

        var response = await client.PostAsJsonAsync("/api/search", new { query });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Overlong_query_is_accepted()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/search", new { query = new string('x', 5000) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Open_library_outage_returns_502_problem_without_a_stack_trace()
    {
        var client = CreateClient(catalog: _ => throw new CatalogUnavailableException("down"));

        var response = await client.PostAsJsonAsync("/api/search", new { query = "hobbit" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Open Library is unavailable", text);
        Assert.DoesNotContain("   at ", text);
    }

    [Fact]
    public async Task Health_returns_200()
    {
        var response = await CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
