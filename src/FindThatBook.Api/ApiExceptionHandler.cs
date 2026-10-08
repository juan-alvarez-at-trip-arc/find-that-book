using FindThatBook.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api;

/// <summary>
/// Turns unhandled exceptions into problem details responses. An unavailable catalog becomes a 502 with a retry hint, and anything else becomes a 500 without exposing internals.
/// </summary>
/// <param name="problems">Service that writes the problem details body.</param>
/// <param name="log">Logger for the handled exception.</param>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    /// <summary>Maps the exception to a status code and title, logs it, and writes the problem details response.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the problem details response was written, which stops further exception handling.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            CatalogUnavailableException => (StatusCodes.Status502BadGateway, "Open Library is unavailable. Please try again shortly."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError) log.LogError(exception, "Unhandled exception");
        else log.LogWarning(exception, "Upstream failure");

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
        });
    }
}
