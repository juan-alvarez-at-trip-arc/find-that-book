namespace FindThatBook.Infrastructure.Health;

/// <summary>Status values for a single dependency or the configuration check.</summary>
public static class HealthStatus
{
    /// <summary>Working as expected.</summary>
    public const string Ok = "ok";
    /// <summary>Usable with reduced function, for example an AI provider without a key.</summary>
    public const string Degraded = "degraded";
    /// <summary>Not usable.</summary>
    public const string Down = "down";
}

/// <summary>Result of probing one dependency.</summary>
/// <param name="Name">Stable dependency name, such as <c>openLibrary</c>.</param>
/// <param name="Status">One of the <see cref="HealthStatus"/> values.</param>
/// <param name="Detail">Short reason when the status is not ok. Never contains secrets.</param>
/// <param name="LatencyMs">Probe duration in milliseconds, when a probe ran.</param>
public sealed record DependencyHealth(string Name, string Status, string? Detail = null, long? LatencyMs = null);

/// <summary>Result of validating configuration values.</summary>
/// <param name="Status">One of the <see cref="HealthStatus"/> values.</param>
/// <param name="Issues">One message per problem found, naming the setting but never its value.</param>
public sealed record ConfigHealth(string Status, IReadOnlyList<string> Issues);

/// <summary>Overall readiness of the app.</summary>
/// <param name="Status"><see cref="Ready"/>, <see cref="Degraded"/> or <see cref="Unready"/>.</param>
/// <param name="Config">Configuration check result.</param>
/// <param name="Dependencies">One entry per probed dependency.</param>
public sealed record HealthReport(string Status, ConfigHealth Config, IReadOnlyList<DependencyHealth> Dependencies)
{
    /// <summary>Everything works.</summary>
    public const string Ready = "ready";
    /// <summary>Searches work, but something optional (the AI provider) is missing or failing.</summary>
    public const string Degraded = "degraded";
    /// <summary>Searches cannot work: configuration is invalid or Open Library is down.</summary>
    public const string Unready = "unready";
}

/// <summary>The AI provider chosen by <c>Ai:Provider</c>, after validation at startup.</summary>
/// <param name="Name">Either <c>Gemini</c> or <c>OpenAiCompatible</c>.</param>
public sealed record AiProviderSelection(string Name)
{
    /// <summary>The Gemini provider name.</summary>
    public const string Gemini = "Gemini";
    /// <summary>The OpenAI-compatible provider name.</summary>
    public const string OpenAiCompatible = "OpenAiCompatible";
}

/// <summary>Checks the readiness of one external dependency with a lightweight request.</summary>
public interface IHealthProbe
{
    /// <summary>Stable dependency name used in the report.</summary>
    string Name { get; }

    /// <summary>When true, a <see cref="HealthStatus.Down"/> result makes the app unready. Otherwise it only degrades it.</summary>
    bool Critical { get; }

    /// <summary>Probes the dependency. Failures are returned as results, not thrown.</summary>
    /// <param name="ct">Cancellation token tied to the caller.</param>
    /// <returns>The dependency's health.</returns>
    Task<DependencyHealth> CheckAsync(CancellationToken ct);
}
