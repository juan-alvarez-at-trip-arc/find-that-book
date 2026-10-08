using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Application.Explanations;

public sealed class FallbackExplanationWriter(IExplanationWriter primary, IExplanationWriter fallback, ILogger<FallbackExplanationWriter> log) : IExplanationWriter
{
    public async Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct)
    {
        try
        {
            return await primary.WriteAsync(matches, ct);
        }
        catch (AiUnavailableException ex)
        {
            log.LogWarning(ex, "AI explanations unavailable, using templates");
            return await fallback.WriteAsync(matches, ct);
        }
    }
}
