using System.Text;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>The catalog data about one match after it was cleaned for use in a prompt. Titles and names come from Open Library, where anyone can edit records, so they are treated as untrusted: control characters and markup are removed and every value is cut to a fixed length. The same cleaned values feed the prompt and the check of the reply, so the reply can only cite what the model was shown.</summary>
internal sealed class ExplanationFacts
{
    /// <summary>The most characters kept of a title.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The most characters kept of an author name.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The most primary authors and the most contributors kept.</summary>
    public const int MaxNames = 5;

    /// <summary>Builds the cleaned facts for a match.</summary>
    /// <param name="match">The ranked match.</param>
    public ExplanationFacts(MatchedBook match)
    {
        Match = match;
        Title = Clean(match.Book.Title, MaxTitleLength);
        PrimaryAuthors = CleanNames(match.Book.PrimaryAuthors);
        Contributors = CleanNames(match.Book.Contributors);
        MatchedAuthor = match.MatchedAuthor is null ? null : Clean(match.MatchedAuthor, MaxNameLength);
        MatchedTitle = match.MatchedTitle is null ? null : Clean(match.MatchedTitle, MaxTitleLength);

        var text = new StringBuilder();
        foreach (var part in new[] { Title, MatchedAuthor, MatchedTitle }.Concat(PrimaryAuthors).Concat(Contributors).OfType<string>())
            text.Append(part).Append(' ');
        if (match.YearMatches && match.Book.FirstPublishYear is { } year) text.Append(year);

        Blob = text.ToString().ToLowerInvariant();
        Tokens = Normalizer.Normalize(text.ToString()).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    /// <summary>The match these facts describe.</summary>
    public MatchedBook Match { get; }

    /// <summary>The cleaned work title.</summary>
    public string Title { get; }

    /// <summary>The cleaned primary author names.</summary>
    public IReadOnlyList<string> PrimaryAuthors { get; }

    /// <summary>The cleaned contributor names.</summary>
    public IReadOnlyList<string> Contributors { get; }

    /// <summary>The cleaned name of the author that matched the query, if any.</summary>
    public string? MatchedAuthor { get; }

    /// <summary>The cleaned edition title that matched, if the work is titled differently.</summary>
    public string? MatchedTitle { get; }

    /// <summary>All the cleaned text the model is shown, in lower case, for checking that a string comes from the facts.</summary>
    public string Blob { get; }

    /// <summary>The normalized words of the cleaned text, and the publication year when it matched the query.</summary>
    public HashSet<string> Tokens { get; }

    /// <summary>Replaces control characters, angle brackets and backticks with spaces, collapses whitespace and cuts the result to a length.</summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="max">The most characters to keep.</param>
    /// <returns>The cleaned text, which is empty for null.</returns>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(Math.Min(text.Length, max + 1));
        var lastWasSpace = true;
        foreach (var ch in text)
        {
            var blank = char.IsControl(ch) || char.IsWhiteSpace(ch) || ch is '<' or '>' or '`';
            if (blank)
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            if (sb.Length > max) break;
        }

        var cleaned = sb.ToString().Trim();
        return cleaned.Length > max ? cleaned[..max].TrimEnd() : cleaned;
    }

    /// <summary>Cleans a list of names, dropping blanks and keeping the first few.</summary>
    /// <param name="names">The names from the catalog.</param>
    /// <returns>At most <see cref="MaxNames"/> cleaned, non-empty names.</returns>
    private static IReadOnlyList<string> CleanNames(IEnumerable<string> names)
        => names.Select(n => Clean(n, MaxNameLength)).Where(n => n.Length > 0).Take(MaxNames).ToList();
}
