using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Application.Explanations;

/// <summary>Writes explanations with the primary (AI) writer and switches to the fallback (template) writer when the AI is unavailable, so results always carry an explanation.</summary>
/// <param name="primary">The preferred writer, normally AI based.</param>
/// <param name="fallback">The writer used when <paramref name="primary"/> throws <see cref="AiUnavailableException"/>.</param>
/// <param name="log">Logger that records each fallback.</param>
public sealed class FallbackExplanationWriter(IExplanationWriter primary, IExplanationWriter fallback, ILogger<FallbackExplanationWriter> log) : IExplanationWriter
{
    /// <summary>Tries the primary writer and uses the fallback writer if the AI is unavailable.</summary>
    /// <param name="matches">The ranked matches to explain.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The explanations from the primary writer, or from the fallback writer when the AI failed.</returns>
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
