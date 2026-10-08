using System.Text.RegularExpressions;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Application.Parsing;

public sealed partial class HeuristicQueryParser : IQueryParser
{
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "illustrated", "deluxe", "edition", "hardcover", "paperback", "novel", "book", "by",
        "unabridged", "audiobook", "signed", "reprint", "anniversary", "collectors",
    };

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
            new(whole, null, keywords),
            new(null, whole, keywords),
        };
        if (words.Count >= 2)
        {
            hypotheses.Add(new(string.Join(' ', words.Skip(1)), words[0], keywords));
            hypotheses.Add(new(string.Join(' ', words.Take(words.Count - 1)), words[^1], keywords));
        }
        if (words.Count >= 3)
        {
            hypotheses.Add(new(string.Join(' ', words.Skip(2)), string.Join(' ', words.Take(2)), keywords));
            hypotheses.Add(new(string.Join(' ', words.Take(words.Count - 2)), string.Join(' ', words.Skip(words.Count - 2)), keywords));
        }

        return Task.FromResult(new ParseOutcome(hypotheses, false));
    }

    [GeneratedRegex(@"^(1[5-9]\d\d|20\d\d)$")]
    private static partial Regex YearPattern();
}
