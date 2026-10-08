using System.Text.RegularExpressions;
using FindThatBook.Application.Models;
using FindThatBook.Application.Text;

namespace FindThatBook.Application.Matching;

/// <summary>Pure ranking logic that scores catalog candidates against the query hypotheses, assigns a match tier and orders the best results.</summary>
public static partial class Matcher
{
    /// <summary>Dedupes candidates, keeps each one's best match across all hypotheses and orders them by tier strength, then exact title, matching year, edition count and title.</summary>
    /// <param name="hypotheses">The parsed interpretations of the query, whose keywords supply the years to match.</param>
    /// <param name="candidates">The candidate works returned by the catalog.</param>
    /// <param name="max">The maximum number of matches to return.</param>
    /// <returns>Up to <paramref name="max"/> matches, strongest first. Candidates that match no hypothesis are left out.</returns>
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
    /// <param name="m">The match to score.</param>
    /// <returns>The strength value, where a lower number ranks higher.</returns>
    private static int Strength(MatchedBook m) => m.Tier switch
    {
        MatchTier.ExactTitlePrimaryAuthor => 10,
        MatchTier.ExactTitleContributorAuthor => 20,
        MatchTier.NearTitleAuthor => m.FromGuess ? 60 : 30,
        MatchTier.AuthorOnly => 40,
        _ => m.ExactTitle ? 50 : 70,
    };

    /// <summary>Collapses candidates that share a full title and first primary author, keeping the one with the most editions.</summary>
    /// <param name="candidates">The candidates to dedupe.</param>
    /// <returns>One candidate per full-title and primary-author key.</returns>
    private static IEnumerable<CatalogCandidate> Dedupe(IEnumerable<CatalogCandidate> candidates)
        => candidates
            .GroupBy(c => (Normalizer.FullTitleKey(c.Title), Normalizer.NormalizeName(c.PrimaryAuthors.FirstOrDefault())))
            .Select(g => g.OrderByDescending(c => c.EditionCount).First());

    /// <summary>Decides whether one match for the same candidate beats another, preferring lower strength and then an exact title.</summary>
    /// <param name="a">The match being tested.</param>
    /// <param name="b">The current best match.</param>
    /// <returns>True when <paramref name="a"/> should replace <paramref name="b"/>.</returns>
    private static bool IsBetter(MatchedBook a, MatchedBook b)
        => Strength(a) < Strength(b) || (Strength(a) == Strength(b) && a.ExactTitle && !b.ExactTitle);

    /// <summary>Compares one hypothesis with one candidate and assigns a match tier from the title and author results.</summary>
    /// <param name="hypothesis">The interpretation of the query to test.</param>
    /// <param name="candidate">The candidate work to test against.</param>
    /// <param name="years">The publication years found in the query keywords.</param>
    /// <returns>The match with its tier, or null when the candidate does not fit the hypothesis.</returns>
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
    /// <param name="queryTitle">The normalized title key of the query.</param>
    /// <param name="candidate">The candidate work whose title and edition titles are compared.</param>
    /// <returns>Whether the title matched exactly or nearly, and the edition title that produced the match (null when the work title matched or nothing did).</returns>
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

    /// <summary>Tests whether two normalized titles are close: one contains all words of the other, or the word overlap (Jaccard) is at least 0.6.</summary>
    /// <param name="query">The normalized query title key.</param>
    /// <param name="candidate">The normalized candidate title key.</param>
    /// <returns>True when the titles are near matches.</returns>
    private static bool IsNear(string query, string candidate)
    {
        var q = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var c = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (q.Count == 0 || c.Count == 0) return false;

        var shared = q.Intersect(c).Count();
        if (shared == q.Count || shared == c.Count) return true;
        return (double)shared / q.Union(c).Count() >= 0.6;
    }

    /// <summary>Finds how the queried author relates to the candidate, checking primary authors, then their aliases, then contributors.</summary>
    /// <param name="queryAuthor">The author text from the hypothesis, if any.</param>
    /// <param name="candidate">The candidate work whose authors are compared.</param>
    /// <returns>The kind of author match and the matched author name, or <see cref="AuthorMatchKind.None"/> and null.</returns>
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

    /// <summary>Checks that every query token equals a name token or, for tokens of three or more characters, prefixes one.</summary>
    /// <param name="queryTokens">The normalized tokens of the queried author name.</param>
    /// <param name="name">The author name to test.</param>
    /// <returns>True when all query tokens are found in the name.</returns>
    private static bool NameMatches(string[] queryTokens, string name)
    {
        var nameTokens = Normalizer.NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return queryTokens.All(q => nameTokens.Any(n => n == q || (q.Length >= 3 && n.StartsWith(q, StringComparison.Ordinal))));
    }

    /// <summary>Regular expression for a four-digit year between 1500 and 2099.</summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"^(1[5-9]\d\d|20\d\d)$")]
    private static partial Regex YearPattern();
}
