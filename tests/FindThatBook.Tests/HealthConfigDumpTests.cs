using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Tests;

public class HealthConfigDumpTests
{
    private static ConfigDump Build(
        string provider = "Gemini",
        GeminiOptions? gemini = null,
        OpenAiCompatibleOptions? openAi = null,
        OpenLibraryOptions? openLibrary = null)
        => ConfigDump.Build(new AiProviderSelection(provider), gemini ?? new GeminiOptions(), openAi ?? new OpenAiCompatibleOptions(), openLibrary ?? new OpenLibraryOptions());

    [Fact]
    public void A_long_key_keeps_only_its_last_four_characters()
    {
        const string key = "freellmapi-66556a145888667463961087c5cd313902fa3ddd48d2654c";

        var dump = Build(openAi: new OpenAiCompatibleOptions { ApiKey = key });

        var masked = dump.OpenAiCompatible.ApiKey;
        Assert.Equal(key.Length, masked.Length);
        Assert.EndsWith("654c", masked);
        Assert.Equal(new string('*', key.Length - 4), masked[..^4]);
        Assert.DoesNotContain(key, JsonSerializer.Serialize(dump));
    }

    [Theory]
    [InlineData("gem-secret")]
    [InlineData("12345678901")]
    [InlineData("abcdefghwxyz")]
    [InlineData("abcdefghijklmno")]
    [InlineData("a")]
    public void A_short_key_is_fully_masked(string key)
    {
        var dump = Build(gemini: new GeminiOptions { ApiKey = key });

        Assert.Equal(new string('*', key.Length), dump.Gemini.ApiKey);
    }

    [Fact]
    public void A_sixteen_character_key_shows_its_last_four_characters()
        => Assert.Equal("************wxyz", Build(gemini: new GeminiOptions { ApiKey = "abcdefghijklwxyz" }).Gemini.ApiKey);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_key_is_reported_as_not_set(string? key)
    {
        var dump = Build(gemini: new GeminiOptions { ApiKey = key }, openAi: new OpenAiCompatibleOptions { ApiKey = key });

        Assert.Equal("not set", dump.Gemini.ApiKey);
        Assert.Equal("not set", dump.OpenAiCompatible.ApiKey);
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored_when_masking()
        => Assert.Equal("************wxyz", Build(gemini: new GeminiOptions { ApiKey = "  abcdefghijklwxyz " }).Gemini.ApiKey);

    [Fact]
    public void Blank_models_and_urls_show_the_effective_defaults()
    {
        var dump = Build(
            gemini: new GeminiOptions { Model = "" },
            openAi: new OpenAiCompatibleOptions { Model = " ", BaseUrl = "" });

        Assert.Equal(GeminiOptions.DefaultModel, dump.Gemini.Model);
        Assert.Equal(OpenAiCompatibleOptions.DefaultModel, dump.OpenAiCompatible.Model);
        Assert.Equal(OpenAiCompatibleOptions.DefaultBaseUrl, dump.OpenAiCompatible.BaseUrl);
    }

    [Fact]
    public void Credentials_embedded_in_a_url_are_removed()
    {
        var dump = Build(openAi: new OpenAiCompatibleOptions { BaseUrl = "http://user:pw-secret@host.test:8080/v1/" });

        Assert.Equal("http://host.test:8080/v1/", dump.OpenAiCompatible.BaseUrl);
        Assert.DoesNotContain("pw-secret", JsonSerializer.Serialize(dump));
    }

    [Fact]
    public void An_unparseable_url_is_shown_as_is_without_throwing()
    {
        var dump = Build(openLibrary: new OpenLibraryOptions { BaseUrl = "not a url" });

        Assert.Equal("not a url", dump.OpenLibrary.BaseUrl);
    }

    [Fact]
    public void Open_library_values_and_the_user_agent_are_included()
    {
        var dump = Build(openLibrary: new OpenLibraryOptions { MaxDetailLookups = 4, Contact = "me@example.test" });

        Assert.Equal(4, dump.OpenLibrary.MaxDetailLookups);
        Assert.Equal("me@example.test", dump.OpenLibrary.Contact);
        Assert.Contains("me@example.test", dump.OpenLibrary.UserAgent);
        Assert.Equal(2, dump.OpenLibrary.MaxRetries);
    }

    [Fact]
    public void The_selected_provider_is_reported_and_both_provider_sections_are_always_present()
    {
        var dump = Build(provider: "OpenAiCompatible");

        Assert.Equal("OpenAiCompatible", dump.Ai.Provider);
        Assert.NotNull(dump.Gemini);
        Assert.NotNull(dump.OpenAiCompatible);
    }

    // HTTP endpoint

    [Fact]
    public async Task Config_endpoint_returns_effective_values_with_masked_keys()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Ai:Provider", "OpenAiCompatible");
            b.UseSetting("Gemini:ApiKey", "gem-secret-key-123");
            b.UseSetting("OpenAiCompatible:ApiKey", "oai-secret-key-456");
            b.UseSetting("OpenAiCompatible:BaseUrl", "http://rpi-llm.local:3001/v1/");
            b.UseSetting("OpenLibrary:MaxDetailLookups", "3");
            b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(new RemoteIpFilter("127.0.0.1", "10.0.0.5")));
        });

        var response = await factory.CreateClient().GetAsync("/api/health/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("gem-secret-key-123", raw);
        Assert.DoesNotContain("oai-secret-key-456", raw);

        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("OpenAiCompatible", body.GetProperty("ai").GetProperty("provider").GetString());
        Assert.Equal("**************-123", body.GetProperty("gemini").GetProperty("apiKey").GetString());
        Assert.Equal("**************-456", body.GetProperty("openAiCompatible").GetProperty("apiKey").GetString());
        Assert.Equal("http://rpi-llm.local:3001/v1/", body.GetProperty("openAiCompatible").GetProperty("baseUrl").GetString());
        Assert.Equal(3, body.GetProperty("openLibrary").GetProperty("maxDetailLookups").GetInt32());
    }

    private sealed class RemoteIpFilter(string remote, string local) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
                context.Connection.LocalIpAddress = IPAddress.Parse(local);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private static HttpClient ClientFrom(string remote, string local = "10.0.0.5")
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(new RemoteIpFilter(remote, local))))
            .CreateClient();

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("172.17.0.1")]
    [InlineData("2001:db8::1")]
    public async Task Config_endpoint_hides_itself_from_non_local_callers(string remote)
    {
        var response = await ClientFrom(remote).GetAsync("/api/health/config");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1", "10.0.0.5")]
    [InlineData("::1", "10.0.0.5")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    public async Task Config_endpoint_answers_local_callers(string remote, string local)
    {
        var response = await ClientFrom(remote, local).GetAsync("/api/health/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Config_endpoint_hides_itself_when_the_caller_address_is_unknown()
    {
        var response = await new WebApplicationFactory<Program>().CreateClient().GetAsync("/api/health/config");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("Forwarded")]
    [InlineData("X-Real-IP")]
    public async Task Config_endpoint_hides_itself_from_callers_that_came_through_a_proxy(string header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health/config");
        request.Headers.TryAddWithoutValidation(header, "203.0.113.9");

        var response = await ClientFrom("127.0.0.1").SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_stays_open_to_non_local_callers()
    {
        var response = await ClientFrom("203.0.113.9").GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
