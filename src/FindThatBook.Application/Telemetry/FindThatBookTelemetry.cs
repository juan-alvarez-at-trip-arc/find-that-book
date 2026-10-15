using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FindThatBook.Application.Telemetry;

/// <summary>The trace source and metrics of the app. They cost nothing until an OpenTelemetry listener subscribes to <see cref="SourceName"/> and <see cref="MeterName"/>.</summary>
public static class FindThatBookTelemetry
{
    /// <summary>Name of the <see cref="ActivitySource"/> that the OpenTelemetry tracer subscribes to.</summary>
    public const string SourceName = "FindThatBook";
    /// <summary>Name of the <see cref="Meter"/> that the OpenTelemetry meter provider subscribes to.</summary>
    public const string MeterName = "FindThatBook";

    /// <summary>Source of the search stage spans.</summary>
    public static readonly ActivitySource Source = new(SourceName);

    private static readonly Meter AppMeter = new(MeterName);

    private static readonly Counter<long> AiFallbacks = AppMeter.CreateCounter<long>("ftb.ai.fallback", description: "AI steps that fell back to the non-AI version.");
    private static readonly Counter<long> OpenLibraryCooldowns = AppMeter.CreateCounter<long>("ftb.openlibrary.cooldown", description: "Open Library 429 responses that started a cooldown.");
    private static readonly Counter<long> AiCircuitChanges = AppMeter.CreateCounter<long>("ftb.ai.circuit", description: "Times the AI circuit breaker opened or closed.");
    private static readonly Counter<long> OpenLibraryCircuitChanges = AppMeter.CreateCounter<long>("ftb.openlibrary.circuit", description: "Times the Open Library circuit breaker opened or closed.");
    private static readonly Histogram<int> SearchResults = AppMeter.CreateHistogram<int>("ftb.search.results", description: "Number of results returned per search.");

    /// <summary>Counts one AI step that fell back to its non-AI version.</summary>
    /// <param name="step">The step that fell back, <c>parse</c> or <c>explain</c>.</param>
    public static void RecordAiFallback(string step) => AiFallbacks.Add(1, new KeyValuePair<string, object?>("step", step));

    /// <summary>Counts one Open Library 429 response that started a cooldown.</summary>
    public static void RecordOpenLibraryCooldown() => OpenLibraryCooldowns.Add(1);

    /// <summary>Counts one change of the AI circuit breaker.</summary>
    /// <param name="state">The new state, <c>opened</c> or <c>closed</c>.</param>
    public static void RecordAiCircuit(string state) => AiCircuitChanges.Add(1, new KeyValuePair<string, object?>("state", state));

    /// <summary>Counts one change of the Open Library circuit breaker.</summary>
    /// <param name="state">The new state, <c>opened</c> or <c>closed</c>.</param>
    public static void RecordOpenLibraryCircuit(string state) => OpenLibraryCircuitChanges.Add(1, new KeyValuePair<string, object?>("state", state));

    /// <summary>Records how many results one search returned.</summary>
    /// <param name="count">The number of results in the response.</param>
    public static void RecordSearchResults(int count) => SearchResults.Record(count);
}
