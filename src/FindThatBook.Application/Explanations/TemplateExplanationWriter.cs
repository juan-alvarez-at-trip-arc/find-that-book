using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;

namespace FindThatBook.Application.Explanations;

/// <summary>Non-AI explanation writer that builds each sentence from the match tier and the book's facts, so the text can never contradict the ranking.</summary>
public sealed class TemplateExplanationWriter : IExplanationWriter
{
    /// <summary>Describes every match with <see cref="Describe"/>.</summary>
    /// <param name="matches">The ranked matches to explain.</param>
    /// <param name="ct">Token that cancels the operation (not used, the work is synchronous).</param>
    /// <returns>A completed task holding one explanation per match, with <c>aiUsed</c> false.</returns>
    public Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct)
        => Task.FromResult(new ExplanationOutcome(matches.Select(Describe).ToList(), false));

    /// <summary>Builds the explanation for one match from its tier, matched author or edition title, contributors, matching year and whether author details were fetched.</summary>
    /// <param name="match">The ranked match to describe.</param>
    /// <returns>A single sentence ending in a period.</returns>
    public static string Describe(MatchedBook match)
    {
        var book = match.Book;
        var primary = JoinNames(book.PrimaryAuthors);
        var parts = new List<string>();

        switch (match.Tier)
        {
            case MatchTier.ExactTitlePrimaryAuthor:
                parts.Add($"Exact title match; {match.MatchedAuthor} is primary author");
                break;
            case MatchTier.ExactTitleContributorAuthor:
                parts.Add($"Exact title match; {match.MatchedAuthor} is listed only as a contributor");
                if (primary.Length > 0) parts.Add($"primary author is {primary}");
                break;
            case MatchTier.NearTitleAuthor:
                var role = match.AuthorMatch == AuthorMatchKind.Primary ? "primary author" : "a contributor";
                parts.Add($"Close title match; {match.MatchedAuthor} is {role}");
                break;
            case MatchTier.AuthorOnly:
                parts.Add($"No title given; top work by primary author {match.MatchedAuthor}");
                break;
            default:
                parts.Add(match.ExactTitle
                    ? "Exact title match; no matching author in the query"
                    : "Close title match; no matching author in the query");
                if (primary.Length > 0) parts.Add($"primary author is {primary}");
                break;
        }

        if (match.MatchedTitle is not null)
            parts[0] = parts[0].Replace("title match", $"title match on the edition title \"{match.MatchedTitle}\"");

        if (book.Contributors.Count > 0 && match.Tier != MatchTier.ExactTitleContributorAuthor)
            parts.Add($"{JoinNames(book.Contributors)} listed as contributor");
        if (match.YearMatches)
            parts.Add($"first published {book.FirstPublishYear}, matching the query");
        if (!book.Enriched)
            parts.Add("author details unavailable");

        return string.Join("; ", parts) + ".";
    }

    /// <summary>Joins author names for display, listing up to two and abbreviating longer lists as "first, second and others".</summary>
    /// <param name="names">The names to join.</param>
    /// <returns>The display text, empty when there are no names.</returns>
    private static string JoinNames(IReadOnlyList<string> names)
        => names.Count <= 2 ? string.Join(", ", names) : $"{names[0]}, {names[1]} and others";
}
