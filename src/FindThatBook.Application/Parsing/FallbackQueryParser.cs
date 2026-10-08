using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Application.Parsing;

/// <summary>Parses with the primary (AI) parser and switches to the fallback (heuristic) parser when the AI is unavailable, so a search never fails because of the AI.</summary>
/// <param name="primary">The preferred parser, normally AI based.</param>
/// <param name="fallback">The parser used when <paramref name="primary"/> throws <see cref="AiUnavailableException"/>.</param>
/// <param name="log">Logger that records each fallback.</param>
public sealed class FallbackQueryParser(IQueryParser primary, IQueryParser fallback, ILogger<FallbackQueryParser> log) : IQueryParser
{
    /// <summary>Tries the primary parser and uses the fallback parser if the AI is unavailable.</summary>
    /// <param name="query">The raw search text typed by the reader.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The primary parser's outcome, or the fallback parser's outcome when the AI failed.</returns>
    public async Task<ParseOutcome> ParseAsync(string query, CancellationToken ct)
    {
        try
        {
            return await primary.ParseAsync(query, ct);
        }
        catch (AiUnavailableException ex)
        {
            log.LogWarning(ex, "AI query parsing unavailable, using heuristic parser");
            return await fallback.ParseAsync(query, ct);
        }
    }
}
