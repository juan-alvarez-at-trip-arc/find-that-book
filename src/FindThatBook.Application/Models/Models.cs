using System.Text.Json.Serialization;

namespace FindThatBook.Application.Models;

/// <summary>One interpretation of the reader's query, as extracted by the AI or guessed by the heuristic parser.</summary>
/// <param name="Title">The title part of the query, or null when none was identified.</param>
/// <param name="Author">The author part of the query, or null when none was identified.</param>
/// <param name="Keywords">Other terms from the query, such as a publication year or edition noise words.</param>
/// <param name="Guessed">True when the heuristic parser guessed this title/author split instead of the AI extracting it.</param>
public sealed record ParsedQuery(string? Title, string? Author, IReadOnlyList<string> Keywords, bool Guessed = false);

/// <summary>A work returned by the catalog before matching, with the facts the matcher and explanation writer use.</summary>
/// <param name="WorkId">The catalog identifier of the work (an Open Library work key such as OL123W).</param>
/// <param name="Title">The title of the work.</param>
/// <param name="PrimaryAuthors">Authors credited on the work record itself.</param>
/// <param name="Contributors">Authors listed in search results but not on the work record.</param>
/// <param name="FirstPublishYear">The year the work was first published, or null when unknown.</param>
/// <param name="EditionCount">The number of editions, used as a popularity tie-breaker.</param>
/// <param name="CoverId">The cover image identifier, or null when the work has no cover.</param>
/// <param name="Enriched">True when the work and author detail lookups succeeded, so primary authors are reliable.</param>
/// <param name="AlternativeTitles">Edition titles the catalog matched, which can differ from the work title (for example another language).</param>
/// <param name="PrimaryAuthorAliases">Other spellings and scripts of the primary authors' names.</param>
public sealed record CatalogCandidate(
    string WorkId,
    string Title,
    IReadOnlyList<string> PrimaryAuthors,
    IReadOnlyList<string> Contributors,
    int? FirstPublishYear,
    int EditionCount,
    int? CoverId,
    bool Enriched,
    IReadOnlyList<string> AlternativeTitles,
    IReadOnlyList<string> PrimaryAuthorAliases);

/// <summary>How well a book matched the query, from strongest (1) to weakest (5).</summary>
public enum MatchTier
{
    /// <summary>The title matches exactly and the queried author is a primary author.</summary>
    ExactTitlePrimaryAuthor = 1,
    /// <summary>The title matches exactly and the queried author is only a contributor.</summary>
    ExactTitleContributorAuthor = 2,
    /// <summary>The title is a near match and the queried author is a primary author or contributor.</summary>
    NearTitleAuthor = 3,
    /// <summary>The query has no title and the queried author is a primary author.</summary>
    AuthorOnly = 4,
    /// <summary>The title matches (exactly or nearly) but no queried author matches.</summary>
    TitleOnly = 5,
}

/// <summary>The role of the queried author on a matched book.</summary>
public enum AuthorMatchKind
{
    /// <summary>No author matched.</summary>
    None,
    /// <summary>The author is a primary author of the work.</summary>
    Primary,
    /// <summary>The author is listed only as a contributor.</summary>
    Contributor,
}

/// <summary>A candidate that matched a hypothesis, with the evidence used to rank and explain it.</summary>
/// <param name="Book">The matched candidate.</param>
/// <param name="Tier">The match tier assigned to the book.</param>
/// <param name="ExactTitle">True when the title matched exactly rather than nearly.</param>
/// <param name="AuthorMatch">Whether the queried author matched as a primary author, a contributor or not at all.</param>
/// <param name="MatchedAuthor">The author name that matched, or null when no author matched.</param>
/// <param name="YearMatches">True when the first publish year equals a year given in the query.</param>
/// <param name="FromGuess">True when the winning hypothesis came from the heuristic parser's guess, which lowers the rank of near-title matches.</param>
/// <param name="MatchedTitle">The edition title that matched when it differs from the work title, otherwise null.</param>
public sealed record MatchedBook(
    CatalogCandidate Book,
    MatchTier Tier,
    bool ExactTitle,
    AuthorMatchKind AuthorMatch,
    string? MatchedAuthor,
    bool YearMatches,
    bool FromGuess = false,
    string? MatchedTitle = null);

/// <summary>The result of parsing a query.</summary>
/// <param name="Hypotheses">The interpretations of the query, which can be empty.</param>
/// <param name="AiUsed">True when the AI produced the hypotheses, false for the heuristic fallback.</param>
public sealed record ParseOutcome(IReadOnlyList<ParsedQuery> Hypotheses, bool AiUsed);

/// <summary>The result of writing explanations for the ranked matches.</summary>
/// <param name="Explanations">One explanation per match, in match order.</param>
/// <param name="AiUsed">True when the AI wrote the explanations, false when templates did.</param>
public sealed record ExplanationOutcome(IReadOnlyList<string> Explanations, bool AiUsed);

/// <summary>The request body of a search.</summary>
/// <param name="Query">The raw search text, or null when none was sent.</param>
public sealed record SearchRequest(string? Query);

/// <summary>One ranked book as returned to the client.</summary>
/// <param name="Title">The title of the work.</param>
/// <param name="Author">The authors joined for display (primary authors, or the first contributor when there are none).</param>
/// <param name="Authors">The same authors as a list.</param>
/// <param name="FirstPublishYear">The year of first publication, or null when unknown. Serialized as <c>first_publish_year</c>.</param>
/// <param name="WorkId">The catalog identifier of the work.</param>
/// <param name="Url">The link to the work on Open Library.</param>
/// <param name="CoverUrl">The link to the cover image, or null when the work has no cover.</param>
/// <param name="Explanation">The one-sentence reason this book matched.</param>
/// <param name="MatchTier">The match tier from 1 (strongest) to 5,.</param>
/// <param name="ExactTitle">True when the title matched exactly rather than nearly.</param>
/// <param name="MatchedTitle">The edition title that matched when it differs from the work title, otherwise null.</param>
public sealed record BookResult(
    string Title,
    string Author,
    IReadOnlyList<string> Authors,
    [property: JsonPropertyName("first_publish_year")] int? FirstPublishYear,
    string WorkId,
    string Url,
    string? CoverUrl,
    string Explanation,
    int MatchTier,
    bool ExactTitle,
    string? MatchedTitle = null);

/// <summary>The response of a search.</summary>
/// <param name="AiUsed">True only when both query parsing and explanations used the AI.</param>
/// <param name="Message">A notice for the reader, such as the no-results hint, or null.</param>
/// <param name="Results">The ranked books, empty when nothing matched.</param>
public sealed record SearchResponse(bool AiUsed, string? Message, IReadOnlyList<BookResult> Results);
