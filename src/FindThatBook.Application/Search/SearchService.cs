using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Matching;
using FindThatBook.Application.Models;

namespace FindThatBook.Application.Search;

public sealed class SearchService(IQueryParser parser, IBookCatalog catalog, IExplanationWriter writer) : ISearchService
{
    public const int MaxQueryLength = 200;
    public const int MaxResults = 5;

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
