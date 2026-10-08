using FindThatBook.Application.Models;

namespace FindThatBook.Application.Abstractions;

public interface IQueryParser
{
    Task<ParseOutcome> ParseAsync(string query, CancellationToken ct);
}

public interface IBookCatalog
{
    /// <summary>Searches every hypothesis and returns de-duplicated candidates. Throws <see cref="CatalogUnavailableException"/> if the catalog cannot be searched.</summary>
    Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct);
}

public interface IExplanationWriter
{
    Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct);
}

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(string query, CancellationToken ct);
}

public sealed class AiUnavailableException : Exception
{
    public AiUnavailableException(string message) : base(message) { }
    public AiUnavailableException(string message, Exception inner) : base(message, inner) { }
}

public sealed class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(string message) : base(message) { }
    public CatalogUnavailableException(string message, Exception inner) : base(message, inner) { }
}
