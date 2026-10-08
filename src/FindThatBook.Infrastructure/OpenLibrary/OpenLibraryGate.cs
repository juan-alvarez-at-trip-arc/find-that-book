using System.Net.Http.Headers;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// Shared by every Open Library request in the process. Caps how many run at once and, after an HTTP 429,
/// holds all further requests back for the time Open Library asked for.
/// </summary>
public sealed class OpenLibraryGate(int maxConcurrent, TimeProvider? clock = null)
{
    private static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _slots = new(Math.Max(1, maxConcurrent));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _lock = new();
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    public bool IsCoolingDown(out TimeSpan remaining)
    {
        lock (_lock)
        {
            var left = _until - _clock.GetUtcNow();
            remaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            return left > TimeSpan.Zero;
        }
    }

    /// <summary>Starts (or extends) a cooldown from a Retry-After header, or <paramref name="fallback"/> when it is absent.</summary>
    public void StartCooldown(RetryConditionHeaderValue? retryAfter, TimeSpan fallback)
    {
        var now = _clock.GetUtcNow();
        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : fallback);
        wait = wait < MinCooldown ? MinCooldown : wait > MaxCooldown ? MaxCooldown : wait;

        lock (_lock)
        {
            var until = now + wait;
            if (until > _until) _until = until;
        }
    }

    public async Task<IDisposable> WaitAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        return new Releaser(_slots);
    }

    private sealed class Releaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
