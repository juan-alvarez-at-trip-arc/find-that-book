using System.Globalization;
using System.Reflection;
using System.Text;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace FindThatBook.Tests;

public class AppSettingsTests
{
    private static IConfigurationRoot Load()
        => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(RepoFiles.Text("src/FindThatBook.Api/appsettings.json")))).Build();

    public static TheoryData<string, Type> OptionSections => new()
    {
        { "Ai", typeof(AiGenerationOptions) },
        { "Ai:Resilience", typeof(AiResilienceOptions) },
        { "Ai:Gemini", typeof(GeminiOptions) },
        { "Ai:OpenAiCompatible", typeof(OpenAiCompatibleOptions) },
        { "OpenLibrary", typeof(OpenLibraryOptions) },
        { "OpenLibrary:Resilience", typeof(OpenLibraryResilienceOptions) },
    };

    [Theory]
    [MemberData(nameof(OptionSections))]
    public void Appsettings_lists_every_setting_of_the_options_class_with_its_default(string section, Type options)
    {
        var config = Load().GetSection(section);
        var defaults = Activator.CreateInstance(options)!;
        var settable = options.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0);

        foreach (var property in settable)
        {
            var entry = config.GetChildren().SingleOrDefault(c => c.Key == property.Name);
            if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                Assert.True(entry is not null && entry.GetChildren().Any(), $"{section}:{property.Name} is missing from appsettings.json.");
                continue;
            }
            Assert.True(entry is not null, $"{section}:{property.Name} is missing from appsettings.json.");

            var expected = property.GetValue(defaults);
            if (expected is null || property.Name == "ApiKey") continue;
            var actual = Convert.ChangeType(entry!.Value, expected.GetType(), CultureInfo.InvariantCulture);
            Assert.True(Equals(expected, actual), $"{section}:{property.Name} in appsettings.json differs from the default of the options class.");
        }
    }

    [Fact]
    public void Appsettings_lists_the_settings_that_are_not_options_classes()
    {
        var config = Load();

        Assert.NotNull(config.GetSection("Api:Telemetry").GetChildren().SingleOrDefault(c => c.Key == "Enabled"));
        Assert.NotNull(config.GetSection("Api:Telemetry").GetChildren().SingleOrDefault(c => c.Key == "LogHttpBodies"));
        Assert.NotNull(config.GetSection("Api:Health").GetChildren().SingleOrDefault(c => c.Key == "ExposeConfig"));
        Assert.NotNull(config.GetSection("Api:RateLimit").GetChildren().SingleOrDefault(c => c.Key == "SearchPermitsPerMinute"));
        Assert.NotNull(config.GetSection("Api:RateLimit").GetChildren().SingleOrDefault(c => c.Key == "MaxConcurrentSearches"));
    }

    [Theory]
    [InlineData("Ai__Gemini__ApiKey: ${AI_GEMINI_API_KEY:-}")]
    [InlineData("Ai__Model: ${AI_MODEL:-}")]
    [InlineData("Ai__OpenAiCompatible__ApiKey: ${AI_OPEN_AI_COMPATIBLE_API_KEY:-}")]
    [InlineData("Ai__OpenAiCompatible__BaseUrl: ${AI_OPEN_AI_COMPATIBLE_BASE_URL:-https://api.groq.com/openai/v1/}")]
    public void Compose_maps_the_provider_settings_under_the_ai_section(string line)
    {
        Assert.Contains(line, RepoFiles.Text("docker-compose.yml"));
    }

    [Fact]
    public void The_provider_sections_are_not_top_level_sections()
    {
        var config = Load();

        Assert.Null(config.GetSection("Gemini").GetChildren().FirstOrDefault());
        Assert.Null(config.GetSection("OpenAiCompatible").GetChildren().FirstOrDefault());
    }

    [Theory]
    [InlineData("Api__RateLimit__SearchPermitsPerMinute: ${API_RATE_LIMIT_SEARCH_PERMITS_PER_MINUTE:-10}")]
    [InlineData("Api__RateLimit__MaxConcurrentSearches: ${API_RATE_LIMIT_MAX_CONCURRENT_SEARCHES:-8}")]
    [InlineData("Api__Health__ExposeConfig: ${API_HEALTH_EXPOSE_CONFIG:-false}")]
    [InlineData("Api__Telemetry__Enabled: ${API_TELEMETRY_ENABLED:-false}")]
    [InlineData("Api__Telemetry__LogHttpBodies: ${API_TELEMETRY_LOG_HTTP_BODIES:-true}")]
    public void Compose_maps_the_api_settings_under_the_api_section(string line)
    {
        Assert.Contains(line, RepoFiles.Text("docker-compose.yml"));
    }

    [Theory]
    [InlineData("Telemetry")]
    [InlineData("Health")]
    [InlineData("RateLimit")]
    public void The_api_sections_are_not_top_level_sections(string section)
    {
        Assert.Null(Load().GetSection(section).GetChildren().FirstOrDefault());
    }

    [Theory]
    [InlineData("GEMINI_MODEL")]
    [InlineData("OPENAI_MODEL")]
    public void The_per_provider_model_variables_are_gone(string variable)
    {
        Assert.DoesNotContain(variable, RepoFiles.Text("docker-compose.yml"));
        Assert.DoesNotContain(variable, RepoFiles.Text(".env.sample"));
    }

    [Fact]
    public void The_env_sample_has_the_single_model_variable()
        => Assert.Contains("AI_MODEL=", RepoFiles.Text(".env.sample"));

    private static string Snake(string configPath)
        => string.Join("_", configPath.Split("__").Select(segment => string.Join("_", System.Text.RegularExpressions.Regex.Split(segment, "(?<=[a-z0-9])(?=[A-Z])"))).Select(s => s.ToUpperInvariant()));

    public static TheoryData<string, string> ComposeMappings()
    {
        var data = new TheoryData<string, string>();
        var mapping = new System.Text.RegularExpressions.Regex(@"^\s+((?:[A-Za-z]+__)+[A-Za-z]+):\s*\$\{([A-Z0-9_]+):-", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (System.Text.RegularExpressions.Match m in mapping.Matches(RepoFiles.Text("docker-compose.yml")))
            data.Add(m.Groups[1].Value, m.Groups[2].Value);
        return data;
    }

    [Theory]
    [MemberData(nameof(ComposeMappings))]
    public void Every_compose_variable_is_the_config_path_in_upper_snake_case(string configPath, string variable)
    {
        Assert.Equal(Snake(configPath), variable);
        Assert.Contains(variable + "=", RepoFiles.Text(".env.sample"));
    }

    [Fact]
    public void The_compose_file_has_at_least_the_expected_mappings()
        => Assert.True(ComposeMappings().Count() >= 30);
}
