using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Application.Text;

public static partial class Normalizer
{
    private static readonly string[] SubtitleMarkers = [":", " - ", " – ", ", or "];
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];

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

    public static string TitleKey(string? title)
        => WithoutLeadingArticle(Normalize(StripSubtitle(title ?? "")));

    /// <summary>Like <see cref="TitleKey"/> but keeps the subtitle, so "Series: Book 1" and "Series: Book 2" stay distinct.</summary>
    public static string FullTitleKey(string? title)
        => WithoutLeadingArticle(Normalize(Parenthetical().Replace(title ?? "", "")));

    private static string WithoutLeadingArticle(string key)
    {
        foreach (var article in LeadingArticles)
        {
            if (key.StartsWith(article, StringComparison.Ordinal)) return key[article.Length..];
        }
        return key;
    }

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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parenthetical();
}
