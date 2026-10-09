using System.Net;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class HealthProbeTests
{
    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static StubFactory Factory(StubHandler handler) => new(handler);

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    // Open Library

    [Fact]
    public async Task Open_library_is_ok_when_a_tiny_search_succeeds()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{}"));
        var probe = new OpenLibraryProbe(Factory(handler), Options.Create(new OpenLibraryOptions()), new OpenLibraryGate(2));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("openLibrary", result.Name);
        Assert.Equal(HealthStatus.Ok, result.Status);
        Assert.NotNull(result.LatencyMs);
        Assert.StartsWith("/search.json", Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Open_library_is_down_on_a_failure_status(HttpStatusCode code)
    {
        var probe = new OpenLibraryProbe(Factory(new StubHandler(_ => Status(code))), Options.Create(new OpenLibraryOptions()), new OpenLibraryGate(2));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains(((int)code).ToString(), result.Detail);
    }

    [Fact]
    public async Task Open_library_is_down_without_a_request_while_a_cooldown_is_active()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{}"));
        var gate = new OpenLibraryGate(2);
        gate.StartCooldown(null, TimeSpan.FromSeconds(30));
        var probe = new OpenLibraryProbe(Factory(handler), Options.Create(new OpenLibraryOptions()), gate);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains("cooling down", result.Detail);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Open_library_is_down_when_the_request_times_out()
    {
        var handler = new AsyncStubHandler((_, _) => throw new TaskCanceledException());
        var probe = new OpenLibraryProbe(new StubFactory(handler), Options.Create(new OpenLibraryOptions()), new OpenLibraryGate(2));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains("timed out", result.Detail);
    }

    [Fact]
    public async Task Open_library_is_down_when_it_cannot_be_reached()
    {
        var handler = new AsyncStubHandler((_, _) => throw new HttpRequestException("boom"));
        var probe = new OpenLibraryProbe(new StubFactory(handler), Options.Create(new OpenLibraryOptions()), new OpenLibraryGate(2));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains("unreachable", result.Detail);
    }

    [Fact]
    public async Task Open_library_with_an_invalid_base_url_is_down_not_an_exception()
    {
        var options = Options.Create(new OpenLibraryOptions { BaseUrl = "not a url" });
        var probe = new OpenLibraryProbe(Factory(new StubHandler(_ => Status(HttpStatusCode.OK))), options, new OpenLibraryGate(2));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains("base URL", result.Detail);
    }

    // Gemini

    [Fact]
    public async Task Gemini_without_a_key_is_degraded_and_sends_no_request()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.OK));
        var probe = new GeminiProbe(Factory(handler), Options.Create(new GeminiOptions { ApiKey = " " }));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("gemini", result.Name);
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("API key", result.Detail);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gemini_is_ok_when_the_model_lookup_succeeds()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json("{}"); });
        var probe = new GeminiProbe(Factory(handler), Options.Create(new GeminiOptions { ApiKey = "k", Model = "gemini-x" }));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Ok, result.Status);
        Assert.Equal("/v1beta/models/gemini-x", Assert.Single(handler.Requests));
        Assert.Equal("generativelanguage.googleapis.com", seen!.RequestUri!.Host);
        Assert.Equal("k", seen.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Gemini_with_a_blank_model_checks_the_default_model()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{}"));
        var probe = new GeminiProbe(Factory(handler), Options.Create(new GeminiOptions { ApiKey = "k", Model = "" }));

        await probe.CheckAsync(CancellationToken.None);

        Assert.Equal($"/v1beta/models/{GeminiOptions.DefaultModel}", Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "API key")]
    [InlineData(HttpStatusCode.Forbidden, "API key")]
    [InlineData(HttpStatusCode.NotFound, "model")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Gemini_is_down_when_the_key_or_model_is_rejected_or_the_server_fails(HttpStatusCode code, string expected)
    {
        var probe = new GeminiProbe(Factory(new StubHandler(_ => Status(code))), Options.Create(new GeminiOptions { ApiKey = "k" }));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Down, result.Status);
        Assert.Contains(expected, result.Detail);
    }

    [Fact]
    public async Task Gemini_is_degraded_when_rate_limited()
    {
        var probe = new GeminiProbe(Factory(new StubHandler(_ => Status(HttpStatusCode.TooManyRequests))), Options.Create(new GeminiOptions { ApiKey = "k" }));

        Assert.Equal(HealthStatus.Degraded, (await probe.CheckAsync(CancellationToken.None)).Status);
    }

    // OpenAI-compatible

    [Fact]
    public async Task Openai_compatible_lists_models_with_a_bearer_token()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json("{}"); });
        var options = Options.Create(new OpenAiCompatibleOptions { ApiKey = "secret", BaseUrl = "https://example.test/v1" });
        var probe = new OpenAiCompatibleProbe(Factory(handler), options);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal("openAiCompatible", result.Name);
        Assert.Equal(HealthStatus.Ok, result.Status);
        Assert.Equal("/v1/models", Assert.Single(handler.Requests));
        Assert.Equal("Bearer secret", seen!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Openai_compatible_without_a_key_sends_no_authorization_header()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json("{}"); });
        var probe = new OpenAiCompatibleProbe(Factory(handler), Options.Create(new OpenAiCompatibleOptions { ApiKey = "" }));

        Assert.Equal(HealthStatus.Ok, (await probe.CheckAsync(CancellationToken.None)).Status);
        Assert.Null(seen!.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task Openai_compatible_server_without_a_models_route_still_counts_as_reachable(HttpStatusCode code)
    {
        var probe = new OpenAiCompatibleProbe(Factory(new StubHandler(_ => Status(code))), Options.Create(new OpenAiCompatibleOptions()));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Ok, result.Status);
        Assert.Contains("models", result.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Openai_compatible_is_down_on_auth_failure_or_server_error(HttpStatusCode code)
    {
        var probe = new OpenAiCompatibleProbe(Factory(new StubHandler(_ => Status(code))), Options.Create(new OpenAiCompatibleOptions()));

        Assert.Equal(HealthStatus.Down, (await probe.CheckAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Openai_compatible_is_degraded_when_rate_limited()
    {
        var probe = new OpenAiCompatibleProbe(Factory(new StubHandler(_ => Status(HttpStatusCode.TooManyRequests))), Options.Create(new OpenAiCompatibleOptions()));

        Assert.Equal(HealthStatus.Degraded, (await probe.CheckAsync(CancellationToken.None)).Status);
    }
}
