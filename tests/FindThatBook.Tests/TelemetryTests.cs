using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Parsing;
using FindThatBook.Application.Search;
using FindThatBook.Application.Telemetry;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class TelemetryTests
{
    private sealed class Measurements : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _all = new();

        public Measurements()
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == FindThatBookTelemetry.MeterName) l.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => { lock (_all) _all.Add((i.Name, v, t.ToArray())); });
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => { lock (_all) _all.Add((i.Name, v, t.ToArray())); });
            _listener.Start();
        }

        public int Count(string name, string? tag = null, object? value = null)
        {
            lock (_all)
                return _all.Count(m => m.Name == name && (tag is null || m.Tags.Any(t => t.Key == tag && Equals(t.Value, value))));
        }

        public void Dispose() => _listener.Dispose();
    }

    private static ActivityListener Listen(List<string> names)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == FindThatBookTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (names) names.Add(a.OperationName); },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Search_emits_a_span_for_each_stage()
    {
        var names = new List<string>();
        using var listener = Listen(names);
        var hobbit = Books.Make("The Hobbit", ["J.R.R. Tolkien"], [], 1937, 120, id: "OL1W");
        var service = new SearchService(new HeuristicQueryParser(), new FakeCatalog(_ => [hobbit]), new TemplateExplanationWriter());

        await service.SearchAsync("tolkien hobbit", default);

        foreach (var stage in new[] { "search", "parse", "catalog", "rank", "explain" })
            Assert.Contains(stage, names);
    }

    [Fact]
    public async Task Search_records_the_number_of_results()
    {
        using var measurements = new Measurements();
        var hobbit = Books.Make("The Hobbit", ["J.R.R. Tolkien"], [], 1937, 120, id: "OL1W");
        var service = new SearchService(new HeuristicQueryParser(), new FakeCatalog(_ => [hobbit]), new TemplateExplanationWriter());

        await service.SearchAsync("tolkien hobbit", default);

        Assert.True(measurements.Count("ftb.search.results") >= 1);
    }

    [Fact]
    public async Task Parser_fallback_increments_the_ai_fallback_counter()
    {
        using var measurements = new Measurements();
        var failing = new AiQueryParser(new FakeAiClient(_ => throw new AiUnavailableException("down")));
        var parser = new FallbackQueryParser(failing, new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance);

        await parser.ParseAsync("tolkien hobbit", default);

        Assert.True(measurements.Count("ftb.ai.fallback", "step", "parse") >= 1);
    }

    [Fact]
    public async Task Explanation_fallback_increments_the_ai_fallback_counter()
    {
        using var measurements = new Measurements();
        var failing = new AiExplanationWriter(new FakeAiClient(_ => throw new AiUnavailableException("down")));
        var writer = new FallbackExplanationWriter(failing, new TemplateExplanationWriter(), NullLogger<FallbackExplanationWriter>.Instance);
        var match = FindThatBook.Application.Matching.Matcher.Rank(
            [Q.Make("the hobbit", "tolkien")],
            [Books.Make("The Hobbit", ["J.R.R. Tolkien"], [], 1937, 120, id: "OL1W")], 5);

        await writer.WriteAsync(match, default);

        Assert.True(measurements.Count("ftb.ai.fallback", "step", "explain") >= 1);
    }

    [Fact]
    public async Task Open_library_429_increments_the_cooldown_counter()
    {
        using var measurements = new Measurements();
        var catalog = new OpenLibraryCatalog(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))) { BaseAddress = new Uri("https://openlibrary.org/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(new OpenLibraryOptions { RetryDelayMs = 0 }));

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default));

        Assert.True(measurements.Count("ftb.openlibrary.cooldown") >= 1);
    }

    [Fact]
    public async Task The_ai_circuit_opening_and_closing_are_counted()
    {
        using var measurements = new Measurements();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var healthy = false;
        var handler = new StubHandler(_ => healthy
            ? StubHandler.Json("""{"choices":[{"message":{"role":"assistant","content":"{}"}}]}""")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var options = new OpenAiCompatibleOptions { ApiKey = "k" };
        var client = new OpenAiCompatibleClient(
            new HttpClient(handler) { BaseAddress = options.BaseUri }, Options.Create(options), null,
            AiResiliencePipeline.Create(new AiResilienceOptions { RetryCount = 0, BreakerMinimumThroughput = 2, BreakerBreakSeconds = 30 }, clock));
        var prompt = new AiPrompt("Reply in JSON.", "hi");
        var schema = System.Text.Json.Nodes.JsonNode.Parse("""{"type":"object"}""")!;
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(prompt, schema, default));
        await Assert.ThrowsAsync<AiUnavailableException>(() => client.GenerateJsonAsync(prompt, schema, default));
        healthy = true;
        clock.Advance(TimeSpan.FromSeconds(31));

        await client.GenerateJsonAsync(prompt, schema, default);

        Assert.True(measurements.Count("ftb.ai.circuit", "state", "opened") >= 1);
        Assert.True(measurements.Count("ftb.ai.circuit", "state", "closed") >= 1);
    }

    [Fact]
    public async Task The_open_library_circuit_opening_is_counted()
    {
        using var measurements = new Measurements();
        var settings = new OpenLibraryOptions { RetryDelayMs = 0, MaxRetries = 0, CircuitBreakerMinimumThroughput = 2 };
        var catalog = new OpenLibraryCatalog(
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))) { BaseAddress = new Uri("https://openlibrary.org/") },
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<OpenLibraryCatalog>.Instance,
            Options.Create(settings),
            null,
            OpenLibraryResiliencePipeline.Create(settings));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default));
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.FindAsync([Q.Make("the hobbit", "tolkien")], default));

        Assert.True(measurements.Count("ftb.openlibrary.circuit", "state", "opened") >= 1);
    }
}
