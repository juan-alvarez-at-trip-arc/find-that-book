using System.Diagnostics;

namespace FindThatBook.Infrastructure.Health;

/// <summary>Runs a probe with a timeout and turns network and configuration failures into a <see cref="HealthStatus.Down"/> result.</summary>
internal static class ProbeRunner
{
    /// <summary>Name of the HTTP client the probes use.</summary>
    public const string ClientName = "health";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Runs <paramref name="check"/> and measures it.</summary>
    /// <param name="name">Dependency name for the result.</param>
    /// <param name="check">The probe body. It returns a status and an optional detail.</param>
    /// <param name="ct">Caller cancellation token. Cancelling it propagates instead of reporting down.</param>
    /// <returns>The measured result.</returns>
    public static async Task<DependencyHealth> RunAsync(string name, Func<CancellationToken, Task<(string Status, string? Detail)>> check, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var (status, detail) = await check(timeout.Token);
            return new DependencyHealth(name, status, detail, clock.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            return new DependencyHealth(name, HealthStatus.Down, "unreachable", clock.ElapsedMilliseconds);
        }
        catch (UriFormatException)
        {
            return new DependencyHealth(name, HealthStatus.Down, "invalid base URL", clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DependencyHealth(name, HealthStatus.Down, "timed out", clock.ElapsedMilliseconds);
        }
    }

    /// <summary>Appends a trailing slash so a relative path appends to the base path instead of replacing its last segment.</summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <returns>An absolute URI ending in a slash.</returns>
    /// <exception cref="UriFormatException">The value is not an absolute URL.</exception>
    public static Uri BaseUri(string baseUrl)
    {
        var url = baseUrl.Trim();
        return new Uri(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);
    }
}
