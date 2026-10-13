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
    private static readonly Histogram<int> SearchResults = AppMeter.CreateHistogram<int>("ftb.search.results", description: "Number of results returned per search.");

    /// <summary>Counts one AI step that fell back to its non-AI version.</summary>
    /// <param name="step">The step that fell back, <c>parse</c> or <c>explain</c>.</param>
    public static void RecordAiFallback(string step) => AiFallbacks.Add(1, new KeyValuePair<string, object?>("step", step));

    /// <summary>Counts one Open Library 429 response that started a cooldown.</summary>
    public static void RecordOpenLibraryCooldown() => OpenLibraryCooldowns.Add(1);

    /// <summary>Records how many results one search returned.</summary>
    /// <param name="count">The number of results in the response.</param>
    public static void RecordSearchResults(int count) => SearchResults.Record(count);
}
