using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Application.Text;

/// <summary>Pure text normalization shared by the parser and matcher, so titles and names compare equal despite case, diacritics, punctuation, subtitles, leading articles and initials.</summary>
public static partial class Normalizer
{
    private static readonly string[] SubtitleMarkers = [":", " - ", " – ", ", or "];
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];

    /// <summary>Lowercases the text, strips diacritics and apostrophes and turns every other non-alphanumeric character into a single space.</summary>
    /// <param name="input">The text to normalize, which may be null or blank.</param>
    /// <returns>The normalized text, or an empty string for null or blank input.</returns>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is '\'' or '’' or 'ʼ') continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Removes parenthetical text and cuts the title at the first subtitle marker (colon, dash or ", or ").</summary>
    /// <param name="title">The title to strip.</param>
    /// <returns>The title without parentheticals or subtitle.</returns>
    public static string StripSubtitle(string title)
    {
        var withoutParens = Parenthetical().Replace(title, "");
        var cut = withoutParens.Length;
        foreach (var marker in SubtitleMarkers)
        {
            var i = withoutParens.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i > 0 && i < cut) cut = i;
        }
        return withoutParens[..cut];
    }

    /// <summary>Builds the key used to compare titles: subtitle removed, normalized and without a leading article.</summary>
    /// <param name="title">The title, which may be null.</param>
    /// <returns>The comparison key, empty when there is no title.</returns>
    public static string TitleKey(string? title)
        => WithoutLeadingArticle(Normalize(StripSubtitle(title ?? "")));

    /// <summary>Like <see cref="TitleKey"/> but keeps the subtitle, so "Series: Book 1" and "Series: Book 2" stay distinct.</summary>
    /// <param name="title">The title, which may be null.</param>
    /// <returns>The comparison key with the subtitle kept, empty when there is no title.</returns>
    public static string FullTitleKey(string? title)
        => WithoutLeadingArticle(Normalize(Parenthetical().Replace(title ?? "", "")));

    /// <summary>Removes a leading "the", "a" or "an" from an already normalized key.</summary>
    /// <param name="key">The normalized title key.</param>
    /// <returns>The key without its leading article.</returns>
    private static string WithoutLeadingArticle(string key)
    {
        foreach (var article in LeadingArticles)
        {
            if (key.StartsWith(article, StringComparison.Ordinal)) return key[article.Length..];
        }
        return key;
    }

    /// <summary>Normalizes a person's name and merges consecutive single-letter initials into one token, so "J. R. R. Tolkien" and "JRR Tolkien" compare equal.</summary>
    /// <param name="name">The name to normalize, which may be null.</param>
    /// <returns>The normalized name tokens separated by single spaces.</returns>
    public static string NormalizeName(string? name)
    {
        var result = new List<string>();
        var initials = new StringBuilder();
        foreach (var token in Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 1)
            {
                initials.Append(token);
                continue;
            }
            if (initials.Length > 0)
            {
                result.Add(initials.ToString());
                initials.Clear();
            }
            result.Add(token);
        }
        if (initials.Length > 0) result.Add(initials.ToString());
        return string.Join(' ', result);
    }

    /// <summary>Regular expression that matches a run of whitespace.</summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Regular expression that matches a parenthetical with the whitespace before it.</summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parenthetical();
}
