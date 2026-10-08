using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Matching;
using FindThatBook.Application.Models;

namespace FindThatBook.Application.Search;

/// <summary>Orchestrates a search: parses the query, fetches catalog candidates, ranks them and attaches explanations.</summary>
/// <param name="parser">Turns the query text into hypotheses.</param>
/// <param name="catalog">Finds candidate books for the hypotheses.</param>
/// <param name="writer">Writes the explanation for each ranked result.</param>
public sealed class SearchService(IQueryParser parser, IBookCatalog catalog, IExplanationWriter writer) : ISearchService
{
    /// <summary>The maximum number of characters of the query that are used, and longer text is truncated.</summary>
    public const int MaxQueryLength = 200;
    /// <summary>The maximum number of results returned for a search.</summary>
    public const int MaxResults = 5;

    /// <summary>Trims and truncates the query, then parses, searches, ranks and explains. Returns a hint message instead of results when nothing matches.</summary>
    /// <param name="query">The raw search text typed by the reader.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The response with up to <see cref="MaxResults"/> results, where <c>AiUsed</c> is true only if both parsing and explanations used the AI.</returns>
    public async Task<SearchResponse> SearchAsync(string query, CancellationToken ct)
    {
        var text = query.Trim();
        if (text.Length > MaxQueryLength) text = text[..MaxQueryLength];

        var parsed = await parser.ParseAsync(text, ct);
        var candidates = await catalog.FindAsync(parsed.Hypotheses, ct);
        var matches = Matcher.Rank(parsed.Hypotheses, candidates, MaxResults);

        if (matches.Count == 0)
        {
            return new SearchResponse(parsed.AiUsed,
                "No matching books found. Try adding an author or a different part of the title.", []);
        }

        var explanations = await writer.WriteAsync(matches, ct);
        var results = matches.Select((m, i) => ToResult(m.Book, explanations.Explanations[i])).ToList();
        return new SearchResponse(parsed.AiUsed && explanations.AiUsed, null, results);
    }

    /// <summary>Converts a matched candidate into the client result, using the first contributor as the author when the work has no primary authors.</summary>
    /// <param name="book">The matched candidate.</param>
    /// <param name="explanation">The explanation written for the match.</param>
    /// <returns>The result with Open Library work and cover links.</returns>
    private static BookResult ToResult(CatalogCandidate book, string explanation)
    {
        var authors = book.PrimaryAuthors.Count > 0 ? book.PrimaryAuthors : book.Contributors.Take(1).ToList();
        return new BookResult(
            book.Title,
            string.Join(", ", authors),
            authors,
            book.FirstPublishYear,
            book.WorkId,
            $"https://openlibrary.org/works/{book.WorkId}",
            book.CoverId is { } id ? $"https://covers.openlibrary.org/b/id/{id}-M.jpg" : null,
            explanation);
    }
}
