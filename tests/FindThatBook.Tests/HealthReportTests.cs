using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.Health;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class HealthReportTests
{
    private sealed class FakeProbe(string name, string status, bool critical = false, string? detail = null) : IHealthProbe
    {
        public int Calls { get; private set; }
        public string Name => name;
        public bool Critical => critical;

        public Task<DependencyHealth> CheckAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DependencyHealth(name, status, detail, 5));
        }
    }

    private static HealthReportService Service(
        IEnumerable<IHealthProbe> probes,
        TimeProvider? clock = null,
        GeminiOptions? gemini = null,
        OpenLibraryOptions? openLibrary = null)
        => new(probes,
            Options.Create(gemini ?? new GeminiOptions { ApiKey = "k" }),
            Options.Create(new OpenAiCompatibleOptions()),
            Options.Create(openLibrary ?? new OpenLibraryOptions()),
            new AiProviderSelection("Gemini"),
            clock);

    // Config validation

    [Fact]
    public void Default_configuration_with_a_gemini_key_is_ok()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
        Assert.Empty(config.Issues);
    }

    [Fact]
    public void Gemini_without_a_key_is_degraded()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions(), new OpenAiCompatibleOptions(), new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Degraded, config.Status);
        Assert.Contains("Ai:Gemini:ApiKey", Assert.Single(config.Issues));
    }

    [Fact]
    public void Openai_compatible_without_a_key_is_ok_because_local_servers_need_none()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("OpenAiCompatible"), new GeminiOptions(), new OpenAiCompatibleOptions { ApiKey = "" }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Fact]
    public void Settings_of_the_unselected_provider_are_not_checked()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions { BaseUrl = "nope" }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/")]
    [InlineData("/relative/path")]
    public void An_invalid_open_library_base_url_is_down(string url)
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions { BaseUrl = url });

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Contains("OpenLibrary:BaseUrl", Assert.Single(config.Issues));
    }

    [Fact]
    public void An_invalid_openai_compatible_base_url_is_down_when_that_provider_is_selected()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("OpenAiCompatible"), new GeminiOptions(), new OpenAiCompatibleOptions { BaseUrl = "nope" }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Contains("Ai:OpenAiCompatible:BaseUrl", Assert.Single(config.Issues));
    }

    [Fact]
    public void A_blank_openai_compatible_base_url_falls_back_to_the_default_and_is_ok()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("OpenAiCompatible"), new GeminiOptions(), new OpenAiCompatibleOptions { BaseUrl = "" }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Fact]
    public void Non_positive_open_library_limits_are_down_and_each_is_named()
    {
        var options = new OpenLibraryOptions { MaxDetailLookups = 0, MaxAuthorsPerWork = 0, MaxConcurrentRequests = -1, DefaultCooldownSeconds = 0 };

        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "k" }, new OpenAiCompatibleOptions(), options);

        Assert.Equal(HealthStatus.Down, config.Status);
        Assert.Equal(4, config.Issues.Count);
        Assert.Contains(config.Issues, i => i.Contains("MaxDetailLookups"));
        Assert.Contains(config.Issues, i => i.Contains("MaxConcurrentRequests"));
    }

    [Fact]
    public void Issues_never_contain_the_api_key()
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("Gemini"), new GeminiOptions { ApiKey = "super-secret" }, new OpenAiCompatibleOptions(), new OpenLibraryOptions { BaseUrl = "nope" });

        Assert.DoesNotContain(config.Issues, i => i.Contains("super-secret"));
    }

    // Overall status

    [Fact]
    public async Task All_ok_is_ready()
    {
        var service = Service([new FakeProbe("openLibrary", HealthStatus.Ok, critical: true), new FakeProbe("gemini", HealthStatus.Ok)]);

        var report = await service.GetAsync(CancellationToken.None);

        Assert.Equal(HealthReport.Ready, report.Status);
        Assert.Equal(2, report.Dependencies.Count);
    }

    [Fact]
    public async Task A_down_ai_dependency_is_degraded_not_unready()
    {
        var service = Service([new FakeProbe("openLibrary", HealthStatus.Ok, critical: true), new FakeProbe("gemini", HealthStatus.Down)]);

        Assert.Equal(HealthReport.Degraded, (await service.GetAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_down_critical_dependency_is_unready()
    {
        var service = Service([new FakeProbe("openLibrary", HealthStatus.Down, critical: true), new FakeProbe("gemini", HealthStatus.Ok)]);

        Assert.Equal(HealthReport.Unready, (await service.GetAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Invalid_configuration_is_unready_even_when_every_probe_is_ok()
    {
        var service = Service([new FakeProbe("openLibrary", HealthStatus.Ok, critical: true)], openLibrary: new OpenLibraryOptions { MaxDetailLookups = 0 });

        var report = await service.GetAsync(CancellationToken.None);

        Assert.Equal(HealthReport.Unready, report.Status);
        Assert.Equal(HealthStatus.Down, report.Config.Status);
    }

    [Fact]
    public async Task A_configuration_warning_makes_a_healthy_report_degraded()
    {
        var service = Service([new FakeProbe("openLibrary", HealthStatus.Ok, critical: true)], gemini: new GeminiOptions());

        Assert.Equal(HealthReport.Degraded, (await service.GetAsync(CancellationToken.None)).Status);
    }

    // Caching

    [Fact]
    public async Task The_report_is_cached_for_thirty_seconds_then_probes_run_again()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var probe = new FakeProbe("openLibrary", HealthStatus.Ok, critical: true);
        var service = Service([probe], clock);

        var first = await service.GetAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(29));
        var second = await service.GetAsync(CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(1, probe.Calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        await service.GetAsync(CancellationToken.None);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_probe_run()
    {
        var probe = new FakeProbe("openLibrary", HealthStatus.Ok, critical: true);
        var service = Service([probe]);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetAsync(CancellationToken.None)));

        Assert.Equal(1, probe.Calls);
    }

    // HTTP endpoint

    private static HttpClient CreateClient(params IHealthProbe[] probes) => CreateClient(false, probes);

    private static HttpClient CreateClient(bool diagnostics, params IHealthProbe[] probes)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Gemini:ApiKey", "k");
            if (diagnostics) builder.UseSetting("Api:Health:ExposeConfig", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHealthProbe>();
                foreach (var probe in probes) services.AddSingleton(probe);
                if (diagnostics) services.AddSingleton<IStartupFilter>(new RemoteIpFilter("127.0.0.1", "10.0.0.5"));
            });
        });
        return factory.CreateClient();
    }

    [Theory]
    [InlineData(HealthStatus.Ok, HttpStatusCode.OK, "ready")]
    [InlineData(HealthStatus.Down, HttpStatusCode.OK, "degraded")]
    public async Task Ready_endpoint_gives_ordinary_callers_only_the_overall_status(string aiStatus, HttpStatusCode code, string status)
    {
        var client = CreateClient(new FakeProbe("openLibrary", HealthStatus.Ok, critical: true), new FakeProbe("gemini", aiStatus, detail: "Gemini rejected the API key (401)"));

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(code, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(status, body.GetProperty("status").GetString());
        Assert.Single(body.EnumerateObject());
        Assert.DoesNotContain("Gemini", raw);
        Assert.DoesNotContain("401", raw);
    }

    [Fact]
    public async Task Ready_endpoint_keeps_the_503_status_but_hides_the_breakdown_from_ordinary_callers()
    {
        var client = CreateClient(new FakeProbe("openLibrary", HealthStatus.Down, critical: true, detail: "returned 500"));

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unready", body.GetProperty("status").GetString());
        Assert.Single(body.EnumerateObject());
    }

    [Fact]
    public async Task Ready_endpoint_hides_the_breakdown_from_a_local_caller_when_diagnostics_are_off()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Gemini:ApiKey", "k");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHealthProbe>();
                services.AddSingleton<IHealthProbe>(new FakeProbe("openLibrary", HealthStatus.Ok, critical: true));
                services.AddSingleton<IStartupFilter>(new RemoteIpFilter("127.0.0.1", "10.0.0.5"));
            });
        });

        var body = await (await factory.CreateClient().GetAsync("/api/health/ready")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Single(body.EnumerateObject());
    }

    [Theory]
    [InlineData("http://api.example.test/v1/")]
    [InlineData("http://192.168.1.20:3001/v1/")]
    [InlineData("http://rpi-llm.local:3001/v1/")]
    public void An_api_key_sent_over_plain_http_to_a_remote_host_is_a_warning(string baseUrl)
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("OpenAiCompatible"), new GeminiOptions(), new OpenAiCompatibleOptions { ApiKey = "k", BaseUrl = baseUrl }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Degraded, config.Status);
        Assert.Contains("Ai:OpenAiCompatible:BaseUrl", Assert.Single(config.Issues));
        Assert.DoesNotContain(baseUrl, string.Join(' ', config.Issues));
    }

    [Theory]
    [InlineData("https://api.groq.com/openai/v1/", "k")]
    [InlineData("http://localhost:11434/v1/", "k")]
    [InlineData("http://127.0.0.1:11434/v1/", "k")]
    [InlineData("http://[::1]:11434/v1/", "k")]
    [InlineData("http://rpi-llm.local:3001/v1/", "")]
    public void Plain_http_is_fine_for_loopback_or_when_no_key_is_sent(string baseUrl, string key)
    {
        var config = HealthConfigCheck.Validate(new AiProviderSelection("OpenAiCompatible"), new GeminiOptions(), new OpenAiCompatibleOptions { ApiKey = key, BaseUrl = baseUrl }, new OpenLibraryOptions());

        Assert.Equal(HealthStatus.Ok, config.Status);
    }

    [Fact]
    public async Task Ready_endpoint_returns_200_with_a_per_dependency_breakdown_to_a_local_caller_with_diagnostics_on()
    {
        var client = CreateClient(true, new FakeProbe("openLibrary", HealthStatus.Ok, critical: true), new FakeProbe("gemini", HealthStatus.Ok));

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("config").GetProperty("status").GetString());
        var deps = body.GetProperty("dependencies");
        Assert.Equal("openLibrary", deps[0].GetProperty("name").GetString());
        Assert.Equal(5, deps[0].GetProperty("latencyMs").GetInt32());
    }

    [Fact]
    public async Task Ready_endpoint_returns_200_when_only_the_ai_is_down()
    {
        var client = CreateClient(new FakeProbe("openLibrary", HealthStatus.Ok, critical: true), new FakeProbe("gemini", HealthStatus.Down, detail: "boom"));

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_endpoint_returns_503_when_open_library_is_down()
    {
        var client = CreateClient(new FakeProbe("openLibrary", HealthStatus.Down, critical: true, detail: "returned 500"));

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unready", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Liveness_endpoint_is_unchanged_and_makes_no_probe_calls()
    {
        var probe = new FakeProbe("openLibrary", HealthStatus.Down, critical: true);
        var client = CreateClient(probe);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public void The_real_wiring_registers_open_library_and_only_the_selected_ai_probe()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Ai:Provider", "OpenAiCompatible"));

        var names = factory.Services.GetServices<IHealthProbe>().Select(p => p.Name).Order().ToList();

        Assert.Equal(["openAiCompatible", "openLibrary"], names);
        Assert.True(factory.Services.GetServices<IHealthProbe>().Single(p => p.Name == "openLibrary").Critical);
    }
}
