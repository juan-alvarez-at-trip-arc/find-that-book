using FindThatBook.Application.Models;

namespace FindThatBook.Application.Abstractions;

/// <summary>Turns the raw search text into one or more structured interpretations (hypotheses) of what the reader is looking for.</summary>
public interface IQueryParser
{
    /// <summary>Parses free-form search text into hypotheses of title, author and keywords.</summary>
    /// <param name="query">The raw search text typed by the reader.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The hypotheses and whether the AI provider produced them (<c>aiUsed</c>).</returns>
    Task<ParseOutcome> ParseAsync(string query, CancellationToken ct);
}

/// <summary>Source of candidate books for a set of query hypotheses, such as Open Library.</summary>
public interface IBookCatalog
{
    /// <summary>Searches every hypothesis and returns de-duplicated candidates. Throws <see cref="CatalogUnavailableException"/> if the catalog cannot be searched.</summary>
    /// <param name="hypotheses">The parsed interpretations of the query to search for.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The de-duplicated candidate works found for all hypotheses, with primary authors and contributors where known.</returns>
    /// <exception cref="CatalogUnavailableException">The catalog cannot be searched.</exception>
    Task<IReadOnlyList<CatalogCandidate>> FindAsync(IReadOnlyList<ParsedQuery> hypotheses, CancellationToken ct);
}

/// <summary>Writes the one-sentence explanation shown with each ranked result, stating why the book matched.</summary>
public interface IExplanationWriter
{
    /// <summary>Writes one explanation per matched book, in the same order as the matches.</summary>
    /// <param name="matches">The ranked matches to explain.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>One explanation per match and whether the AI provider wrote them (<c>aiUsed</c>).</returns>
    Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct);
}

/// <summary>Entry point of the search use case: parse the query, find candidates, rank them and explain the results.</summary>
public interface ISearchService
{
    /// <summary>Runs the full search pipeline for a reader's query.</summary>
    /// <param name="query">The raw search text typed by the reader.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The ranked results with explanations, or a message when nothing matched, plus whether AI was used end to end.</returns>
    Task<SearchResponse> SearchAsync(string query, CancellationToken ct);
}

/// <summary>Signals that the AI provider cannot be used (missing key, rate limit, timeout or malformed output), so callers fall back to the non-AI path.</summary>
public sealed class AiUnavailableException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Describes why the AI provider is unavailable.</param>
    public AiUnavailableException(string message) : base(message) { }
    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    /// <param name="message">Describes why the AI provider is unavailable.</param>
    /// <param name="inner">The exception that caused the failure.</param>
    public AiUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Signals that the book catalog cannot be searched (for example Open Library errors or a 429 cooldown), which surfaces to the reader as a failed search.</summary>
public sealed class CatalogUnavailableException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Describes why the catalog is unavailable.</param>
    public CatalogUnavailableException(string message) : base(message) { }
    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    /// <param name="message">Describes why the catalog is unavailable.</param>
    /// <param name="inner">The exception that caused the failure.</param>
    public CatalogUnavailableException(string message, Exception inner) : base(message, inner) { }
}
