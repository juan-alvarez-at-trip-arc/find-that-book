using System.Net.Http.Headers;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// Shared by every Open Library request in the process. Caps how many run at once and, after an HTTP 429,
/// holds all further requests back for the time Open Library asked for.
/// </summary>
/// <param name="maxConcurrent">Maximum simultaneous requests; values below 1 are treated as 1.</param>
/// <param name="clock">Time source, replaceable in tests. Defaults to the system clock.</param>
public sealed class OpenLibraryGate(int maxConcurrent, TimeProvider? clock = null)
{
    private static readonly TimeSpan MinCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _slots = new(Math.Max(1, maxConcurrent));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _lock = new();
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    /// <summary>Tells whether a 429 cooldown is active, so callers can skip sending requests while Open Library asked us to slow down.</summary>
    /// <param name="remaining">Time left in the cooldown, or zero when none is active.</param>
    /// <returns>True while the cooldown is active.</returns>
    public bool IsCoolingDown(out TimeSpan remaining)
    {
        lock (_lock)
        {
            var left = _until - _clock.GetUtcNow();
            remaining = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            return left > TimeSpan.Zero;
        }
    }

    /// <summary>Starts (or extends) a cooldown from a Retry-After header, or <paramref name="fallback"/> when it is absent. The wait is clamped to between 1 second and 5 minutes, and an existing longer cooldown is never shortened.</summary>
    /// <param name="retryAfter">The Retry-After header from the 429 response, if any.</param>
    /// <param name="fallback">Wait to use when the header is missing.</param>
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

    /// <summary>Waits for a free request slot, which enforces the concurrency cap across the process.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle that frees the slot when disposed; disposing more than once is safe.</returns>
    public async Task<IDisposable> WaitAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        return new Releaser(_slots);
    }

    /// <summary>Handle for an acquired request slot that returns the slot to the gate once.</summary>
    /// <param name="slots">The semaphore the slot was taken from.</param>
    private sealed class Releaser(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        /// <summary>Releases the slot the first time it is called and does nothing afterwards.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}
