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
        { "Gemini", typeof(GeminiOptions) },
        { "OpenAiCompatible", typeof(OpenAiCompatibleOptions) },
        { "OpenLibrary", typeof(OpenLibraryOptions) },
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

        Assert.NotNull(config.GetSection("Telemetry").GetChildren().SingleOrDefault(c => c.Key == "Enabled"));
        Assert.NotNull(config.GetSection("Telemetry").GetChildren().SingleOrDefault(c => c.Key == "LogHttpBodies"));
        Assert.NotNull(config.GetSection("Health").GetChildren().SingleOrDefault(c => c.Key == "ExposeConfig"));
        Assert.NotNull(config.GetSection("RateLimit").GetChildren().SingleOrDefault(c => c.Key == "SearchPermitsPerMinute"));
        Assert.NotNull(config.GetSection("RateLimit").GetChildren().SingleOrDefault(c => c.Key == "MaxConcurrentSearches"));
    }
}
