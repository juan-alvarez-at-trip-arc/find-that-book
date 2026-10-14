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

namespace FindThatBook.Tests;

public class AiGenerationTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""")!;
    private const string GeminiReply = """{"candidates":[{"content":{"parts":[{"text":"{\"a\":\"x\"}"}]}}]}""";
    private const string OpenAiReply = """{"choices":[{"message":{"role":"assistant","content":"{\"a\":\"x\"}"}}]}""";

    private static async Task<JsonNode> GeminiBody(AiGenerationOptions? sampling)
    {
        string? body = null;
        var handler = new StubHandler(req => { body = req.Content!.ReadAsStringAsync().Result; return StubHandler.Json(GeminiReply); });
        var client = new GeminiClient(
            new HttpClient(handler) { BaseAddress = new Uri(GeminiOptions.ApiBaseUrl) },
            Options.Create(new GeminiOptions { ApiKey = "k" }),
            sampling is null ? null : Options.Create(sampling));

        await client.GenerateJsonAsync(new AiPrompt("Reply in JSON.", "hi"), Schema, default);

        return JsonNode.Parse(body!)!["generationConfig"]!;
    }

    private static async Task<JsonNode> OpenAiBody(AiGenerationOptions? sampling)
    {
        string? body = null;
        var handler = new StubHandler(req => { body = req.Content!.ReadAsStringAsync().Result; return StubHandler.Json(OpenAiReply); });
        var options = new OpenAiCompatibleOptions { ApiKey = "k" };
        var client = new OpenAiCompatibleClient(
            new HttpClient(handler) { BaseAddress = options.BaseUri },
            Options.Create(options),
            sampling is null ? null : Options.Create(sampling));

        await client.GenerateJsonAsync(new AiPrompt("Reply in JSON.", "hi"), Schema, default);

        return JsonNode.Parse(body!)!;
    }

    [Fact]
    public void Defaults_keep_the_deterministic_temperature_and_send_no_top_k_or_top_p()
    {
        var sampling = new AiGenerationOptions();

        Assert.Equal(0, sampling.EffectiveTemperature);
        Assert.Null(sampling.EffectiveTopK);
        Assert.Null(sampling.EffectiveTopP);
        Assert.Null(sampling.EffectiveSeed);
        Assert.Null(sampling.EffectiveReasoningEffort);
        Assert.Empty(sampling.Problems());
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("minimal", "minimal")]
    [InlineData("none", "none")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("  High ", "high")]
    [InlineData("LOW", "low")]
    public void A_reasoning_effort_word_is_trimmed_and_lowercased(string configured, string expected)
    {
        var generation = new AiGenerationOptions { ReasoningEffort = configured };

        Assert.Equal(expected, generation.EffectiveReasoningEffort);
        Assert.Empty(generation.Problems());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reasoning_effort_counts_as_not_set(string? configured)
    {
        var generation = new AiGenerationOptions { ReasoningEffort = configured };

        Assert.Null(generation.EffectiveReasoningEffort);
        Assert.Empty(generation.Problems());
    }

    [Theory]
    [InlineData("very high")]
    [InlineData("low;drop")]
    [InlineData("hi\"gh")]
    [InlineData("123456789012345678901")]
    [InlineData("-low")]
    public void A_reasoning_effort_that_is_not_a_short_word_is_not_sent_and_is_reported(string configured)
    {
        var generation = new AiGenerationOptions { ReasoningEffort = configured };

        Assert.Null(generation.EffectiveReasoningEffort);
        var problem = Assert.Single(generation.Problems());
        Assert.Contains("Ai:ReasoningEffort", problem);
        Assert.DoesNotContain(configured, problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-7)]
    [InlineData(int.MaxValue)]
    public void Any_integer_seed_is_accepted(int seed)
    {
        var generation = new AiGenerationOptions { Seed = seed };

        Assert.Equal(seed, generation.EffectiveSeed);
        Assert.Empty(generation.Problems());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    public void A_temperature_outside_0_to_2_falls_back_to_the_default_and_is_reported(double temperature)
    {
        var sampling = new AiGenerationOptions { Temperature = temperature };

        Assert.Equal(0, sampling.EffectiveTemperature);
        Assert.Contains("Ai:Temperature", Assert.Single(sampling.Problems()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_top_k_below_1_is_not_sent_and_is_reported(int topK)
    {
        var sampling = new AiGenerationOptions { TopK = topK };

        Assert.Null(sampling.EffectiveTopK);
        Assert.Contains("Ai:TopK", Assert.Single(sampling.Problems()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.2)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void A_top_p_outside_0_exclusive_to_1_is_not_sent_and_is_reported(double topP)
    {
        var sampling = new AiGenerationOptions { TopP = topP };

        Assert.Null(sampling.EffectiveTopP);
        Assert.Contains("Ai:TopP", Assert.Single(sampling.Problems()));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(2, 40, 0.95)]
    public void Boundary_values_are_accepted(double temperature, int topK, double topP)
    {
        var sampling = new AiGenerationOptions { Temperature = temperature, TopK = topK, TopP = topP };

        Assert.Equal(temperature, sampling.EffectiveTemperature);
        Assert.Equal(topK, sampling.EffectiveTopK);
        Assert.Equal(topP, sampling.EffectiveTopP);
        Assert.Empty(sampling.Problems());
    }

    [Fact]
    public async Task Gemini_sends_the_seed_and_the_reasoning_effort_as_a_thinking_level()
    {
        var config = await GeminiBody(new AiGenerationOptions { Seed = 42, ReasoningEffort = "Low" });

        Assert.Equal(42, config["seed"]!.GetValue<int>());
        Assert.Equal("low", config["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gemini_omits_the_seed_and_the_thinking_config_when_they_are_not_set(bool optionsRegistered)
    {
        var config = await GeminiBody(optionsRegistered ? new AiGenerationOptions() : null);

        Assert.Null(config["seed"]);
        Assert.Null(config["thinkingConfig"]);
    }

    [Fact]
    public async Task Gemini_does_not_send_an_invalid_reasoning_effort()
    {
        var config = await GeminiBody(new AiGenerationOptions { ReasoningEffort = "not a word" });

        Assert.Null(config["thinkingConfig"]);
    }

    [Fact]
    public async Task Openai_compatible_sends_the_seed_and_the_reasoning_effort()
    {
        var body = await OpenAiBody(new AiGenerationOptions { Seed = 7, ReasoningEffort = "High" });

        Assert.Equal(7, body["seed"]!.GetValue<int>());
        Assert.Equal("high", body["reasoning_effort"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Openai_compatible_omits_the_seed_and_the_reasoning_effort_when_they_are_not_set(bool optionsRegistered)
    {
        var body = await OpenAiBody(optionsRegistered ? new AiGenerationOptions() : null);

        Assert.Null(body["seed"]);
        Assert.Null(body["reasoning_effort"]);
    }

    [Fact]
    public async Task Openai_compatible_does_not_send_an_invalid_reasoning_effort()
    {
        var body = await OpenAiBody(new AiGenerationOptions { ReasoningEffort = "x;y" });

        Assert.Null(body["reasoning_effort"]);
    }

    [Fact]
    public async Task Gemini_sends_the_configured_temperature_top_k_and_top_p()
    {
        var config = await GeminiBody(new AiGenerationOptions { Temperature = 0.4, TopK = 20, TopP = 0.9 });

        Assert.Equal(0.4, config["temperature"]!.GetValue<double>());
        Assert.Equal(20, config["topK"]!.GetValue<int>());
        Assert.Equal(0.9, config["topP"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gemini_defaults_send_temperature_0_and_omit_top_k_and_top_p(bool optionsRegistered)
    {
        var config = await GeminiBody(optionsRegistered ? new AiGenerationOptions() : null);

        Assert.Equal(0, config["temperature"]!.GetValue<double>());
        Assert.Null(config["topK"]);
        Assert.Null(config["topP"]);
    }

    [Fact]
    public async Task Gemini_does_not_send_invalid_values()
    {
        var config = await GeminiBody(new AiGenerationOptions { Temperature = 9, TopK = 0, TopP = 3 });

        Assert.Equal(0, config["temperature"]!.GetValue<double>());
        Assert.Null(config["topK"]);
        Assert.Null(config["topP"]);
    }

    [Fact]
    public async Task Openai_compatible_sends_the_configured_temperature_top_k_and_top_p()
    {
        var body = await OpenAiBody(new AiGenerationOptions { Temperature = 0.7, TopK = 30, TopP = 0.8 });

        Assert.Equal(0.7, body["temperature"]!.GetValue<double>());
        Assert.Equal(30, body["top_k"]!.GetValue<int>());
        Assert.Equal(0.8, body["top_p"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Openai_compatible_defaults_send_temperature_0_and_omit_top_k_and_top_p(bool optionsRegistered)
    {
        var body = await OpenAiBody(optionsRegistered ? new AiGenerationOptions() : null);

        Assert.Equal(0, body["temperature"]!.GetValue<double>());
        Assert.Null(body["top_k"]);
        Assert.Null(body["top_p"]);
    }

    [Fact]
    public async Task Openai_compatible_does_not_send_invalid_values()
    {
        var body = await OpenAiBody(new AiGenerationOptions { Temperature = -1, TopK = -3, TopP = 0 });

        Assert.Equal(0, body["temperature"]!.GetValue<double>());
        Assert.Null(body["top_k"]);
        Assert.Null(body["top_p"]);
    }

    private static ServiceProvider Provider(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFindThatBook(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_values_are_read_from_the_ai_configuration_section()
    {
        var sampling = Provider(("Ai:Temperature", "0.3"), ("Ai:TopK", "25"), ("Ai:TopP", "0.85"), ("Ai:Seed", "99"), ("Ai:ReasoningEffort", "Medium"))
            .GetRequiredService<IOptions<AiGenerationOptions>>().Value;

        Assert.Equal(0.3, sampling.Temperature);
        Assert.Equal(25, sampling.TopK);
        Assert.Equal(0.85, sampling.TopP);
        Assert.Equal(99, sampling.Seed);
        Assert.Equal("medium", sampling.EffectiveReasoningEffort);
    }

    [Fact]
    public void Blank_values_count_as_not_set()
    {
        var sampling = Provider(("Ai:Temperature", ""), ("Ai:TopK", ""), ("Ai:TopP", ""), ("Ai:Seed", ""), ("Ai:ReasoningEffort", ""))
            .GetRequiredService<IOptions<AiGenerationOptions>>().Value;

        Assert.Equal(0, sampling.EffectiveTemperature);
        Assert.Null(sampling.EffectiveTopK);
        Assert.Null(sampling.EffectiveTopP);
        Assert.Null(sampling.EffectiveSeed);
        Assert.Null(sampling.EffectiveReasoningEffort);
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenAiCompatible")]
    public async Task The_registered_client_sends_the_configured_values(string provider)
    {
        string? body = null;
        var handler = new StubHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().Result;
            return StubHandler.Json(provider == "Gemini" ? GeminiReply : OpenAiReply);
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = provider, ["Gemini:ApiKey"] = "k", ["Ai:Temperature"] = "0.25", ["Ai:TopK"] = "12", ["Ai:TopP"] = "0.6",
            ["Ai:Seed"] = "123", ["Ai:ReasoningEffort"] = "low",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFindThatBook(config);
        services.Configure<HttpClientFactoryOptions>(nameof(IAiClient), o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = handler));

        await services.BuildServiceProvider().GetRequiredService<IAiClient>().GenerateJsonAsync(new AiPrompt("Reply in JSON.", "hi"), Schema, default);

        var json = JsonNode.Parse(body!)!;
        var sent = provider == "Gemini" ? json["generationConfig"]! : json;
        Assert.Equal(0.25, sent["temperature"]!.GetValue<double>());
        Assert.Equal(12, sent[provider == "Gemini" ? "topK" : "top_k"]!.GetValue<int>());
        Assert.Equal(0.6, sent[provider == "Gemini" ? "topP" : "top_p"]!.GetValue<double>());
        Assert.Equal(123, sent["seed"]!.GetValue<int>());
        var effort = provider == "Gemini" ? sent["thinkingConfig"]!["thinkingLevel"] : sent["reasoning_effort"];
        Assert.Equal("low", effort!.GetValue<string>());
    }

    [Fact]
    public void The_readiness_config_check_fails_on_invalid_values_without_printing_them()
    {
        var sampling = new AiGenerationOptions { Temperature = 7, TopK = 0, TopP = 5, ReasoningEffort = "Q9z top secret" };

        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions(), sampling);

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Equal(4, config.Issues.Count);
        Assert.Contains(config.Issues, i => i.Contains("Ai:Temperature"));
        Assert.Contains(config.Issues, i => i.Contains("Ai:TopK"));
        Assert.Contains(config.Issues, i => i.Contains("Ai:TopP"));
        Assert.Contains(config.Issues, i => i.Contains("Ai:ReasoningEffort"));
        Assert.DoesNotContain(config.Issues, i => i.Contains('7') || i.Contains('5') || i.Contains("secret"));
    }

    [Fact]
    public void The_readiness_config_check_accepts_valid_values()
    {
        var sampling = new AiGenerationOptions { Temperature = 0.5, TopK = 40, TopP = 0.95 };

        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions(), sampling);

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Fact]
    public void The_config_dump_reports_the_values_that_are_sent()
    {
        var dump = ConfigDump.Build(new AiProviderSelection("Gemini"), new GeminiOptions(), new OpenAiCompatibleOptions(), new OpenLibraryOptions(),
            new AiGenerationOptions { Temperature = 0.2, TopK = 15, TopP = 0.5, Seed = 8, ReasoningEffort = "High" });

        Assert.Equal(0.2, dump.Ai.Temperature);
        Assert.Equal(15, dump.Ai.TopK);
        Assert.Equal(0.5, dump.Ai.TopP);
        Assert.Equal(8, dump.Ai.Seed);
        Assert.Equal("high", dump.Ai.ReasoningEffort);
    }

    [Fact]
    public void The_config_dump_shows_the_defaults_when_nothing_is_configured()
    {
        var dump = ConfigDump.Build(new AiProviderSelection("Gemini"), new GeminiOptions(), new OpenAiCompatibleOptions(), new OpenLibraryOptions());

        Assert.Equal(0, dump.Ai.Temperature);
        Assert.Null(dump.Ai.TopK);
        Assert.Null(dump.Ai.TopP);
        Assert.Null(dump.Ai.Seed);
        Assert.Null(dump.Ai.ReasoningEffort);
    }

    [Fact]
    public void Compose_and_the_env_sample_map_the_five_settings()
    {
        var compose = RepoFiles.Text("docker-compose.yml");
        var sample = RepoFiles.Text(".env.sample");

        Assert.Contains("Ai__Temperature: ${AI_TEMPERATURE:-}", compose);
        Assert.Contains("Ai__TopK: ${AI_TOP_K:-}", compose);
        Assert.Contains("Ai__TopP: ${AI_TOP_P:-}", compose);
        Assert.Contains("Ai__Seed: ${AI_SEED:-}", compose);
        Assert.Contains("Ai__ReasoningEffort: ${AI_REASONING_EFFORT:-}", compose);
        Assert.Contains("AI_SEED=", sample);
        Assert.Contains("AI_REASONING_EFFORT=", sample);
        Assert.Contains("AI_TEMPERATURE=", sample);
        Assert.Contains("AI_TOP_K=", sample);
        Assert.Contains("AI_TOP_P=", sample);
    }
}
