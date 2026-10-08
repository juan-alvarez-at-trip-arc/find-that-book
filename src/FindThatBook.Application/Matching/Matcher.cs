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
            .OrderBy(Strength)
            .ThenByDescending(m => m.ExactTitle)
            .ThenByDescending(m => m.YearMatches)
            .ThenByDescending(m => m.Book.EditionCount)
            .ThenBy(m => m.Book.Title, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Lower is stronger. A near title plus an author the AI extracted is strong, but the same match with an author the
    /// heuristic parser merely guessed ranks below an exact title on its own: typing exactly a title is better evidence
    /// than a guessed word that happens to be part of someone's name.
    /// </summary>
    private static int Strength(MatchedBook m) => m.Tier switch
    {
        MatchTier.ExactTitlePrimaryAuthor => 10,
        MatchTier.ExactTitleContributorAuthor => 20,
        MatchTier.NearTitleAuthor => m.FromGuess ? 60 : 30,
        MatchTier.AuthorOnly => 40,
        _ => m.ExactTitle ? 50 : 70,
    };

    private static IEnumerable<CatalogCandidate> Dedupe(IEnumerable<CatalogCandidate> candidates)
        => candidates
            .GroupBy(c => (Normalizer.FullTitleKey(c.Title), Normalizer.NormalizeName(c.PrimaryAuthors.FirstOrDefault())))
            .Select(g => g.OrderByDescending(c => c.EditionCount).First());

    private static bool IsBetter(MatchedBook a, MatchedBook b)
        => Strength(a) < Strength(b) || (Strength(a) == Strength(b) && a.ExactTitle && !b.ExactTitle);

    private static MatchedBook? Evaluate(ParsedQuery hypothesis, CatalogCandidate candidate, HashSet<int> years)
    {
        var queryTitle = Normalizer.TitleKey(hypothesis.Title);
        var hasTitle = queryTitle.Length > 0;
        var (titleExact, titleNear, matchedTitle) = hasTitle ? MatchTitle(queryTitle, candidate) : (false, false, null);

        var (kind, matchedAuthor) = MatchAuthor(hypothesis.Author, candidate);

        MatchTier? tier = null;
        if (titleExact && kind == AuthorMatchKind.Primary) tier = MatchTier.ExactTitlePrimaryAuthor;
        else if (titleExact && kind == AuthorMatchKind.Contributor) tier = MatchTier.ExactTitleContributorAuthor;
        else if (titleNear && kind != AuthorMatchKind.None) tier = MatchTier.NearTitleAuthor;
        else if (!hasTitle && kind == AuthorMatchKind.Primary) tier = MatchTier.AuthorOnly;
        else if ((titleExact || titleNear) && kind == AuthorMatchKind.None) tier = MatchTier.TitleOnly;

        if (tier is null) return null;

        var yearMatches = candidate.FirstPublishYear is { } y && years.Contains(y);
        return new MatchedBook(candidate, tier.Value, titleExact, kind, matchedAuthor, yearMatches, hypothesis.Guessed, matchedTitle);
    }

    /// <summary>
    /// Compares against the work title first, then the edition titles Open Library matched (a work can be titled in
    /// another language while its English edition carries the title the reader typed).
    /// </summary>
    private static (bool Exact, bool Near, string? ViaTitle) MatchTitle(string queryTitle, CatalogCandidate candidate)
    {
        var workKey = Normalizer.TitleKey(candidate.Title);
        if (workKey == queryTitle) return (true, false, null);

        var near = IsNear(queryTitle, workKey);
        foreach (var title in candidate.AlternativeTitles)
        {
            var key = Normalizer.TitleKey(title);
            if (key == queryTitle) return (true, false, title);
        }

        if (near) return (false, true, null);

        foreach (var title in candidate.AlternativeTitles)
        {
            if (IsNear(queryTitle, Normalizer.TitleKey(title))) return (false, true, title);
        }
        return (false, false, null);
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

        // Aliases (other spellings and scripts) belong to the work's primary authors; report the first one by name.
        foreach (var alias in candidate.PrimaryAuthorAliases)
            if (NameMatches(tokens, alias)) return (AuthorMatchKind.Primary, candidate.PrimaryAuthors.FirstOrDefault() ?? alias);

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
