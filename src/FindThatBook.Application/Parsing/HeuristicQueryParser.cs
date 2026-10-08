using System.Text.RegularExpressions;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Application.Parsing;

/// <summary>Non-AI query parser that cannot tell title from author, so it emits several guessed hypotheses (whole text as title, as author, first or last words as author) for the matcher to test.</summary>
public sealed partial class HeuristicQueryParser : IQueryParser
{
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "illustrated", "deluxe", "edition", "hardcover", "paperback", "novel", "book", "by",
        "unabridged", "audiobook", "signed", "reprint", "anniversary", "collectors",
    };

    /// <summary>Normalizes the text, sets aside years and edition noise words as keywords and builds guessed title and author splits from the remaining words.</summary>
    /// <param name="query">The raw search text typed by the reader.</param>
    /// <param name="ct">Token that cancels the operation (not used, the work is synchronous).</param>
    /// <returns>A completed task holding the guessed hypotheses with <c>aiUsed</c> false, or no hypotheses when only keywords remain.</returns>
    public Task<ParseOutcome> ParseAsync(string query, CancellationToken ct)
    {
        var keywords = new List<string>();
        var words = new List<string>();
        foreach (var token in Normalizer.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (YearPattern().IsMatch(token) || NoiseWords.Contains(token)) keywords.Add(token);
            else words.Add(token);
        }

        if (words.Count == 0) return Task.FromResult(new ParseOutcome([], false));

        var whole = string.Join(' ', words);
        var hypotheses = new List<ParsedQuery>
        {
            new(whole, null, keywords, Guessed: true),
            new(null, whole, keywords, Guessed: true),
        };
        if (words.Count >= 2)
        {
            hypotheses.Add(new(string.Join(' ', words.Skip(1)), words[0], keywords, Guessed: true));
            hypotheses.Add(new(string.Join(' ', words.Take(words.Count - 1)), words[^1], keywords, Guessed: true));
        }
        if (words.Count >= 3)
        {
            hypotheses.Add(new(string.Join(' ', words.Skip(2)), string.Join(' ', words.Take(2)), keywords, Guessed: true));
            hypotheses.Add(new(string.Join(' ', words.Take(words.Count - 2)), string.Join(' ', words.Skip(words.Count - 2)), keywords, Guessed: true));
        }

        return Task.FromResult(new ParseOutcome(hypotheses, false));
    }

    /// <summary>Regular expression for a four-digit year between 1500 and 2099.</summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"^(1[5-9]\d\d|20\d\d)$")]
    private static partial Regex YearPattern();
}
