using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Application.Parsing;

public sealed class FallbackQueryParser(IQueryParser primary, IQueryParser fallback, ILogger<FallbackQueryParser> log) : IQueryParser
{
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
