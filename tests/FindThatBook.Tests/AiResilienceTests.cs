using System.Net;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FindThatBook.Tests;

public class AiResilienceTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""")!;
    private static readonly AiPrompt Prompt = new("Reply in JSON.", "hi");
    private const string OpenAiReply = """{"choices":[{"message":{"role":"assistant","content":"{\"a\":\"x\"}"}}]}""";

    private static AiResilienceOptions Settings(int retries = 1) => new()
    {
        RetryCount = retries,
        RetryDelayMs = 0,
        BreakerMinimumThroughput = 2,
        BreakerBreakSeconds = 30,
    };

    private static IAiClient OpenAi(HttpMessageHandler handler, AiResilienceOptions settings, TimeProvider? clock = null)
    {
        var options = new OpenAiCompatibleOptions { ApiKey = "k" };
        return new OpenAiCompatibleClient(new HttpClient(handler) { BaseAddress = options.BaseUri }, Options.Create(options), null, AiResiliencePipeline.Create(settings, clock));
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    [Fact]
    public async Task A_5xx_is_retried_once_and_the_second_answer_is_used()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1 ? Status(HttpStatusCode.ServiceUnavailable) : StubHandler.Json(OpenAiReply));

        var text = await OpenAi(handler, Settings()).GenerateJsonAsync(Prompt, Schema, default);

        Assert.Equal("""{"a":"x"}""", text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Repeated_5xx_stops_after_the_retry_and_reports_the_provider_unavailable()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(handler, Settings()).GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_429_or_other_4xx_is_not_retried(HttpStatusCode code)
    {
        var handler = new StubHandler(_ => Status(code));

        await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(handler, Settings()).GenerateJsonAsync(Prompt, Schema, default));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_hung_attempt_times_out_and_is_not_retried()
    {
        var settings = Settings();
        settings.AttemptTimeoutMs = 50;
        var handler = new HangingHandler();

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(handler, settings).GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(1, handler.Calls);
        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task The_total_timeout_caps_the_whole_call()
    {
        var settings = Settings();
        settings.TotalTimeoutMs = 80;
        var handler = new HangingHandler();

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => OpenAi(handler, settings).GenerateJsonAsync(Prompt, Schema, default));

        Assert.Contains("timed out", error.Message);
    }

    [Fact]
    public async Task The_circuit_opens_after_repeated_failures_and_later_calls_send_no_request()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.ServiceUnavailable));
        var client = OpenAi(handler, Settings(retries: 0), new FakeTimeProvider());
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));
        var sent = handler.Requests.Count;

        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(sent, handler.Requests.Count);
        Assert.Contains("temporarily unavailable", error.Message);
    }

    [Fact]
    public async Task A_rate_limited_provider_counts_towards_the_circuit()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.TooManyRequests));
        var client = OpenAi(handler, Settings(retries: 0), new FakeTimeProvider());
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task The_circuit_closes_again_after_the_break_when_a_call_succeeds()
    {
        var clock = new FakeTimeProvider();
        var healthy = false;
        var handler = new StubHandler(_ => healthy ? StubHandler.Json(OpenAiReply) : Status(HttpStatusCode.ServiceUnavailable));
        var client = OpenAi(handler, Settings(retries: 0), clock);
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));
        healthy = true;
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));

        clock.Advance(TimeSpan.FromSeconds(31));
        var text = await client.GenerateJsonAsync(Prompt, Schema, default);

        Assert.Equal("""{"a":"x"}""", text);
    }

    [Fact]
    public async Task Gemini_uses_the_same_pipeline()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.ServiceUnavailable));
        var client = new GeminiClient(
            new HttpClient(handler) { BaseAddress = new Uri(GeminiOptions.ApiBaseUrl) },
            Options.Create(new GeminiOptions { ApiKey = "k" }), null, AiResiliencePipeline.Create(Settings()));

        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenAiCompatible")]
    public async Task The_registered_clients_share_one_circuit(string provider)
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.ServiceUnavailable));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = provider, ["Gemini:ApiKey"] = "k",
            ["Ai:Resilience:RetryCount"] = "0", ["Ai:Resilience:BreakerMinimumThroughput"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFindThatBook(config);
        services.Configure<HttpClientFactoryOptions>(nameof(IAiClient), o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));
        var provider0 = services.BuildServiceProvider();
        await Assert.ThrowsAsync<AiUnavailableException>(() => provider0.GetRequiredService<IAiClient>().GenerateJsonAsync(Prompt, Schema, default));
        await Assert.ThrowsAsync<AiUnavailableException>(() => provider0.GetRequiredService<IAiClient>().GenerateJsonAsync(Prompt, Schema, default));
        var sent = handler.Requests.Count;

        await Assert.ThrowsAsync<AiUnavailableException>(() => provider0.GetRequiredService<IAiClient>().GenerateJsonAsync(Prompt, Schema, default));

        Assert.Equal(sent, handler.Requests.Count);
    }

    [Fact]
    public void The_readiness_config_check_fails_on_invalid_ai_resilience_values_without_printing_them()
    {
        var resilience = new AiResilienceOptions { RetryCount = -7, BreakerFailureRatio = 9.5, BreakerMinimumThroughput = 1, BreakerBreakSeconds = 0, TotalTimeoutMs = 1 };

        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions(), null, resilience);

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Equal(5, config.Issues.Count);
        Assert.Contains(config.Issues, i => i.Contains("Ai:Resilience:RetryCount"));
        Assert.DoesNotContain(config.Issues, i => i.Contains("-7") || i.Contains("9.5"));
    }

    [Fact]
    public void The_readiness_config_check_fails_on_invalid_open_library_circuit_values()
    {
        var openLibrary = new OpenLibraryOptions { CircuitBreakerFailureRatio = 0, CircuitBreakerMinimumThroughput = 1, CircuitBreakerSamplingSeconds = 0, CircuitBreakerBreakSeconds = -77, AttemptTimeoutMs = 0 };

        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), openLibrary);

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Equal(5, config.Issues.Count);
        Assert.DoesNotContain(config.Issues, i => i.Contains("-77"));
    }

    [Fact]
    public void The_default_resilience_settings_are_valid()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions(), null, new AiResilienceOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Theory]
    [InlineData("RetryCount", "AI_RESILIENCE_RETRY_COUNT", "1")]
    [InlineData("RetryDelayMs", "AI_RESILIENCE_RETRY_DELAY_MS", "300")]
    [InlineData("AttemptTimeoutMs", "AI_RESILIENCE_ATTEMPT_TIMEOUT_MS", "30000")]
    [InlineData("TotalTimeoutMs", "AI_RESILIENCE_TOTAL_TIMEOUT_MS", "45000")]
    [InlineData("BreakerFailureRatio", "AI_RESILIENCE_BREAKER_FAILURE_RATIO", "0.5")]
    [InlineData("BreakerMinimumThroughput", "AI_RESILIENCE_BREAKER_MINIMUM_THROUGHPUT", "4")]
    [InlineData("BreakerSamplingSeconds", "AI_RESILIENCE_BREAKER_SAMPLING_SECONDS", "30")]
    [InlineData("BreakerBreakSeconds", "AI_RESILIENCE_BREAKER_BREAK_SECONDS", "30")]
    public void Compose_and_the_env_sample_map_each_ai_resilience_setting_with_its_default(string setting, string variable, string expectedDefault)
    {
        var compose = RepoFiles.Text("docker-compose.yml");
        var sample = RepoFiles.Text(".env.sample");
        var defaults = new AiResilienceOptions();

        Assert.Contains($"Ai__Resilience__{setting}: ${{{variable}:-{expectedDefault}}}", compose);
        Assert.Contains(variable + "=", sample);
        Assert.Equal(expectedDefault, Convert.ToString(typeof(AiResilienceOptions).GetProperty(setting)!.GetValue(defaults), System.Globalization.CultureInfo.InvariantCulture));
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
