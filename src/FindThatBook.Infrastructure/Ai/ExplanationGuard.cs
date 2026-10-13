using System.Text.RegularExpressions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Infrastructure.Ai;

/// <summary>Checks that an explanation written by the AI says only what the facts support. A reply that fails is treated as unusable, so a poisoned catalog record or a manipulated model cannot put its own message, link or contact detail in the app's voice. Every check lets a word through when it is part of the facts, because a real title can contain it.</summary>
internal static class ExplanationGuard
{
    /// <summary>The most characters allowed in one explanation.</summary>
    public const int MaxLength = 240;

    /// <summary>The most words allowed in one explanation.</summary>
    public const int MaxWords = 40;

    private static readonly Regex Link = new(@"https?://|www\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Contact = new(
        @"[\w.+-]+@[\w-]+|\bdot\s+(?:com|org|net|io|ru|cn|info|xyz|co|me)\b|\[\.\]|\(\.\)|hxxp|\b[\w-]+\.(?:com|org|net|io|ru|cn|info|xyz|co|me)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex ExactClaim = new(@"\bexact", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContributorClaim = new(@"\b(?:contributor|contributors|adaptor|adapter|illustrator|editor|translator)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> BannedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "best", "recommend", "recommended", "ignore", "disregard", "instruction", "instructions",
        "assistant", "click", "visit", "buy", "download", "contact", "email", "password",
    };

    private static readonly HashSet<string> MatchVocabulary = new(StringComparer.Ordinal)
    {
        "exact", "near", "title", "primary", "contributor", "author", "edition", "match", "matched", "open", "library",
    };

    /// <summary>Tells whether the explanation is short enough, free of links and contact details, and made only of the facts about its own book.</summary>
    /// <param name="explanation">The trimmed explanation from the AI provider.</param>
    /// <param name="facts">The cleaned facts of the book the explanation is for.</param>
    /// <returns>True when every check passes.</returns>
    public static bool IsAcceptable(string explanation, ExplanationFacts facts)
    {
        var words = explanation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (explanation.Length > MaxLength || words.Length > MaxWords) return false;
        if (Link.IsMatch(explanation)) return false;
        if (Contact.Matches(explanation).Any(m => !facts.Blob.Contains(m.Value.ToLowerInvariant(), StringComparison.Ordinal))) return false;
        if (Digits.Matches(explanation).Any(m => !facts.Tokens.Contains(m.Value))) return false;
        if (HasOutsideWord(words, facts)) return false;
        if (ExactClaim.IsMatch(explanation) && !facts.Match.ExactTitle) return false;
        return !(ContributorClaim.IsMatch(explanation) && facts.Contributors.Count == 0 && facts.Match.AuthorMatch != AuthorMatchKind.Contributor);
    }

    /// <summary>Finds a banned word, or a capitalised word after the first one, that is neither part of the facts nor plain match vocabulary.</summary>
    /// <param name="words">The words of the explanation.</param>
    /// <param name="facts">The cleaned facts of the book.</param>
    /// <returns>True when such a word exists.</returns>
    private static bool HasOutsideWord(string[] words, ExplanationFacts facts)
    {
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i].Trim(',', '.', ';', ':', '!', '?', '"', '(', ')', '\'', '“', '”', '’');
            if (word.Length == 0) continue;

            var pieces = Normalizer.Normalize(word).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (BannedWords.Contains(word) && !pieces.All(facts.Tokens.Contains)) return true;
            if (i == 0 || !char.IsUpper(word[0])) continue;
            if (!pieces.All(p => facts.Tokens.Contains(p) || MatchVocabulary.Contains(p))) return true;
        }
        return false;
    }
}
