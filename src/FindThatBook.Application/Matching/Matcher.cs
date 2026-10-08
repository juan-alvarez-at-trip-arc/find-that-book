using System.Text.RegularExpressions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Application.Matching;

public static partial class Matcher
{
    public static IReadOnlyList<MatchedBook> Rank(
        IReadOnlyList<ParsedQuery> hypotheses,
        IReadOnlyList<CatalogCandidate> candidates,
        int max = 5)
    {
        var years = hypotheses
            .SelectMany(h => h.Keywords)
            .Where(k => YearPattern().IsMatch(k))
            .Select(int.Parse)
            .ToHashSet();

        var matched = new List<MatchedBook>();
        foreach (var candidate in Dedupe(candidates))
        {
            MatchedBook? best = null;
            foreach (var hypothesis in hypotheses)
            {
                var match = Evaluate(hypothesis, candidate, years);
                if (match is not null && (best is null || IsBetter(match, best))) best = match;
            }
            if (best is not null) matched.Add(best);
        }

        return matched
            .OrderBy(m => (int)m.Tier)
            .ThenByDescending(m => m.ExactTitle)
            .ThenByDescending(m => m.YearMatches)
            .ThenByDescending(m => m.Book.EditionCount)
            .ThenBy(m => m.Book.Title, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    private static IEnumerable<CatalogCandidate> Dedupe(IEnumerable<CatalogCandidate> candidates)
        => candidates
            .GroupBy(c => (Normalizer.FullTitleKey(c.Title), Normalizer.NormalizeName(c.PrimaryAuthors.FirstOrDefault())))
            .Select(g => g.OrderByDescending(c => c.EditionCount).First());

    private static bool IsBetter(MatchedBook a, MatchedBook b)
        => a.Tier < b.Tier || (a.Tier == b.Tier && a.ExactTitle && !b.ExactTitle);

    private static MatchedBook? Evaluate(ParsedQuery hypothesis, CatalogCandidate candidate, HashSet<int> years)
    {
        var queryTitle = Normalizer.TitleKey(hypothesis.Title);
        var hasTitle = queryTitle.Length > 0;
        var titleExact = false;
        var titleNear = false;
        if (hasTitle)
        {
            var candidateTitle = Normalizer.TitleKey(candidate.Title);
            titleExact = candidateTitle == queryTitle;
            titleNear = !titleExact && IsNear(queryTitle, candidateTitle);
        }

        var (kind, matchedAuthor) = MatchAuthor(hypothesis.Author, candidate);

        MatchTier? tier = null;
        if (titleExact && kind == AuthorMatchKind.Primary) tier = MatchTier.ExactTitlePrimaryAuthor;
        else if (titleExact && kind == AuthorMatchKind.Contributor) tier = MatchTier.ExactTitleContributorAuthor;
        else if (titleNear && kind != AuthorMatchKind.None) tier = MatchTier.NearTitleAuthor;
        else if (!hasTitle && kind == AuthorMatchKind.Primary) tier = MatchTier.AuthorOnly;
        else if ((titleExact || titleNear) && kind == AuthorMatchKind.None) tier = MatchTier.TitleOnly;

        if (tier is null) return null;

        var yearMatches = candidate.FirstPublishYear is { } y && years.Contains(y);
        return new MatchedBook(candidate, tier.Value, titleExact, kind, matchedAuthor, yearMatches);
    }

    private static bool IsNear(string query, string candidate)
    {
        var q = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var c = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (q.Count == 0 || c.Count == 0) return false;

        var shared = q.Intersect(c).Count();
        if (shared == q.Count || shared == c.Count) return true;
        return (double)shared / q.Union(c).Count() >= 0.6;
    }

    private static (AuthorMatchKind, string?) MatchAuthor(string? queryAuthor, CatalogCandidate candidate)
    {
        var tokens = Normalizer.NormalizeName(queryAuthor).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return (AuthorMatchKind.None, null);

        foreach (var name in candidate.PrimaryAuthors)
            if (NameMatches(tokens, name)) return (AuthorMatchKind.Primary, name);
        foreach (var name in candidate.Contributors)
            if (NameMatches(tokens, name)) return (AuthorMatchKind.Contributor, name);
        return (AuthorMatchKind.None, null);
    }

    private static bool NameMatches(string[] queryTokens, string name)
    {
        var nameTokens = Normalizer.NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return queryTokens.All(q => nameTokens.Any(n => n == q || (q.Length >= 3 && n.StartsWith(q, StringComparison.Ordinal))));
    }

    [GeneratedRegex(@"^(1[5-9]\d\d|20\d\d)$")]
    private static partial Regex YearPattern();
}
