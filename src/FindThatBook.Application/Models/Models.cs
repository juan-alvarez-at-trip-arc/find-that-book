using System.Text.Json.Serialization;

namespace FindThatBook.Application.Models;

public sealed record ParsedQuery(string? Title, string? Author, IReadOnlyList<string> Keywords);

public sealed record CatalogCandidate(
    string WorkId,
    string Title,
    IReadOnlyList<string> PrimaryAuthors,
    IReadOnlyList<string> Contributors,
    int? FirstPublishYear,
    int EditionCount,
    int? CoverId,
    bool Enriched);

public enum MatchTier
{
    ExactTitlePrimaryAuthor = 1,
    ExactTitleContributorAuthor = 2,
    NearTitleAuthor = 3,
    AuthorOnly = 4,
    TitleOnly = 5,
}

public enum AuthorMatchKind { None, Primary, Contributor }

public sealed record MatchedBook(
    CatalogCandidate Book,
    MatchTier Tier,
    bool ExactTitle,
    AuthorMatchKind AuthorMatch,
    string? MatchedAuthor,
    bool YearMatches);

public sealed record ParseOutcome(IReadOnlyList<ParsedQuery> Hypotheses, bool AiUsed);

public sealed record ExplanationOutcome(IReadOnlyList<string> Explanations, bool AiUsed);

public sealed record SearchRequest(string? Query);

public sealed record BookResult(
    string Title,
    string Author,
    IReadOnlyList<string> Authors,
    [property: JsonPropertyName("first_publish_year")] int? FirstPublishYear,
    string WorkId,
    string Url,
    string? CoverUrl,
    string Explanation);

public sealed record SearchResponse(bool AiUsed, string? Message, IReadOnlyList<BookResult> Results);
