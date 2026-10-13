using System.Net;
using System.Net.Http.Json;
using FindThatBook.Api;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.Telemetry;
using FindThatBook.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FindThatBook.Tests;

public class TelemetryWiringTests
{
    private static WebApplicationFactory<Program> Factory(params (string Key, string Value)[] settings)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Gemini:ApiKey", "");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBookCatalog>();
                services.AddSingleton<IBookCatalog>(new FakeCatalog(_ => [Books.Make("The Hobbit", ["J.R.R. Tolkien"], [], 1937, 120, id: "OL1W")]));
            });
        });

    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Telemetry_is_off_by_default()
    {
        Assert.False(TelemetryExtensions.IsTelemetryEnabled(Config()));
        Assert.False(TelemetryExtensions.IsTelemetryEnabled(Config(("OTEL_EXPORTER_OTLP_ENDPOINT", " "))));
    }

    [Fact]
    public void Telemetry_turns_on_with_the_flag_or_an_otlp_endpoint()
    {
        Assert.True(TelemetryExtensions.IsTelemetryEnabled(Config(("Telemetry:Enabled", "true"))));
        Assert.True(TelemetryExtensions.IsTelemetryEnabled(Config(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"))));
    }

    [Fact]
    public void Http_body_logging_is_not_registered_while_telemetry_is_off()
    {
        using var factory = Factory();

        Assert.Null(factory.Services.GetService<HttpLoggingHandler>());
    }

    [Fact]
    public async Task With_telemetry_on_the_app_serves_searches_and_registers_body_logging()
    {
        using var factory = Factory(("Telemetry:Enabled", "true"), ("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1"));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "tolkien hobbit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(factory.Services.GetService<HttpLoggingHandler>());
    }

    [Fact]
    public void Body_logging_can_be_switched_off_while_telemetry_is_on()
    {
        using var factory = Factory(("Telemetry:Enabled", "true"), ("Telemetry:LogHttpBodies", "false"), ("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1"));

        Assert.Null(factory.Services.GetService<HttpLoggingHandler>());
    }
}
