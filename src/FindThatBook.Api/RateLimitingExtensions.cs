using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api;

/// <summary>Rate limiting for the search endpoint, which spends AI quota and Open Library requests on every call.</summary>
public static class RateLimitingExtensions
{
    /// <summary>The default number of searches one client address may start per minute.</summary>
    public const int DefaultSearchPermitsPerMinute = 10;

    /// <summary>The default number of searches that may run at the same time across all clients.</summary>
    public const int DefaultMaxConcurrentSearches = 8;

    private const string SearchPath = "/api/search";
    private const string UnknownClient = "unknown";
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);

    /// <summary>Limits <c>/api/search</c> per client address and by the number of searches running at once. Other paths are not limited. A limited request gets a 429 problem details response with a <c>Retry-After</c> header.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="config">Application configuration. <c>Api:RateLimit:SearchPermitsPerMinute</c> and <c>Api:RateLimit:MaxConcurrentSearches</c> override the defaults, and values below 1 use the defaults.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddFindThatBookRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = Positive(config.GetValue("Api:RateLimit:SearchPermitsPerMinute", DefaultSearchPermitsPerMinute), DefaultSearchPermitsPerMinute);
        var concurrent = Positive(config.GetValue("Api:RateLimit:MaxConcurrentSearches", DefaultMaxConcurrentSearches), DefaultMaxConcurrentSearches);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsSearch(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = perMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("other")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsSearch(context)
                    ? RateLimitPartition.GetConcurrencyLimiter("search", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = concurrent,
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("other")));
            options.OnRejected = async (context, ct) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : DefaultRetryAfter;
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many searches. Please wait a moment and try again." },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: ct);
            };
        });
        return services;
    }

    /// <summary>Tells whether the request is a search request. Trailing slashes are ignored because routing accepts them and would otherwise reach the endpoint unlimited.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>True when the request path is the search endpoint, with or without trailing slashes.</returns>
    private static bool IsSearch(HttpContext context)
        => context.Request.Path.Value.AsSpan().TrimEnd('/').Equals(SearchPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Identifies the caller by remote address.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The remote address as text, or a shared key when it is unknown.</returns>
    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;

    /// <summary>Falls back to a default when a configured limit is not positive.</summary>
    /// <param name="value">The configured value.</param>
    /// <param name="fallback">The value to use instead.</param>
    /// <returns>The configured value when it is at least 1, otherwise the fallback.</returns>
    private static int Positive(int value, int fallback) => value >= 1 ? value : fallback;
}
