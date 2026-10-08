using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;

namespace FindThatBook.Application.Explanations;

public sealed class TemplateExplanationWriter : IExplanationWriter
{
    public Task<ExplanationOutcome> WriteAsync(IReadOnlyList<MatchedBook> matches, CancellationToken ct)
        => Task.FromResult(new ExplanationOutcome(matches.Select(Describe).ToList(), false));

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

        if (book.Contributors.Count > 0 && match.Tier != MatchTier.ExactTitleContributorAuthor)
            parts.Add($"{JoinNames(book.Contributors)} listed as contributor");
        if (match.YearMatches)
            parts.Add($"first published {book.FirstPublishYear}, matching the query");
        if (!book.Enriched)
            parts.Add("author details unavailable");

        return string.Join("; ", parts) + ".";
    }

    private static string JoinNames(IReadOnlyList<string> names)
        => names.Count <= 2 ? string.Join(", ", names) : $"{names[0]}, {names[1]} and others";
}
