using FindThatBook.Application.Matching;
using FindThatBook.Application.Models;
using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class MatcherTests
{
    private static IReadOnlyList<MatchedBook> Rank(ParsedQuery hypothesis, params CatalogCandidate[] books)
        => Matcher.Rank([hypothesis], books);

    [Fact]
    public void Exact_title_and_primary_author_is_tier_1_even_with_subtitle()
    {
        var book = Books.Make("The Hobbit, or There and Back Again", ["J.R.R. Tolkien"], ["Dixon"], 1937, 120);

        var match = Assert.Single(Rank(Q.Make("The Hobbit", "tolkien"), book));

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, match.Tier);
        Assert.True(match.ExactTitle);
        Assert.Equal(AuthorMatchKind.Primary, match.AuthorMatch);
        Assert.Equal("J.R.R. Tolkien", match.MatchedAuthor);
    }

    [Fact]
    public void Exact_title_with_contributor_only_author_is_tier_2_and_ranks_below_tier_1()
    {
        var primary = Books.Make("The Hobbit", ["J.R.R. Tolkien"], editions: 5);
        var contributorOnly = Books.Make("The Hobbit", ["Someone Else"], ["Tolkien"], editions: 500, id: "OL2W");

        var ranked = Rank(Q.Make("the hobbit", "tolkien"), contributorOnly, primary);

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, ranked[0].Tier);
        Assert.Equal(MatchTier.ExactTitleContributorAuthor, ranked[1].Tier);
        Assert.Equal(AuthorMatchKind.Contributor, ranked[1].AuthorMatch);
    }

    [Fact]
    public void Near_title_with_author_is_tier_3()
    {
        var book = Books.Make("Adventures of Huckleberry Finn", ["Mark Twain"]);

        var match = Assert.Single(Rank(Q.Make("huckleberry", "mark"), book));

        Assert.Equal(MatchTier.NearTitleAuthor, match.Tier);
        Assert.False(match.ExactTitle);
    }

    [Fact]
    public void Author_only_returns_that_authors_works_by_edition_count()
    {
        var small = Books.Make("Hard Times", ["Charles Dickens"], editions: 50);
        var big = Books.Make("Oliver Twist", ["Charles Dickens"], editions: 100);
        var other = Books.Make("Dickens: A Biography", ["Peter Ackroyd"], ["Dickens"], editions: 999);

        var ranked = Rank(Q.Make(null, "dickens"), small, other, big);

        Assert.Equal(["Oliver Twist", "Hard Times"], ranked.Select(r => r.Book.Title));
        Assert.All(ranked, r => Assert.Equal(MatchTier.AuthorOnly, r.Tier));
    }

    [Fact]
    public void Title_without_matching_author_is_tier_5()
    {
        var book = Books.Make("A Tale of Two Cities", ["Charles Dickens"]);

        var match = Assert.Single(Rank(Q.Make("tale two cities", null), book));

        Assert.Equal(MatchTier.TitleOnly, match.Tier);
    }

    [Fact]
    public void Unrelated_books_are_excluded()
    {
        var book = Books.Make("Moby Dick", ["Herman Melville"]);

        Assert.Empty(Rank(Q.Make("the hobbit", "tolkien"), book));
    }

    [Fact]
    public void Initials_and_diacritics_match_plain_forms()
    {
        var tolkien = Books.Make("The Hobbit", ["J.R.R. Tolkien"]);
        var marquez = Books.Make("Cien años de soledad", ["Gabriel García Márquez"]);

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, Rank(Q.Make("hobbit", "jrr tolkien"), tolkien)[0].Tier);
        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, Rank(Q.Make("cien anos de soledad", "garcia marquez"), marquez)[0].Tier);
    }

    [Fact]
    public void Unenriched_candidate_with_only_contributors_ranks_as_contributor()
    {
        var book = Books.Make("The Hobbit", [], ["J.R.R. Tolkien"], enriched: false);

        var match = Assert.Single(Rank(Q.Make("the hobbit", "tolkien"), book));

        Assert.Equal(MatchTier.ExactTitleContributorAuthor, match.Tier);
    }

    [Fact]
    public void Duplicate_works_collapse_to_the_one_with_most_editions()
    {
        var a = Books.Make("The Hobbit", ["J.R.R. Tolkien"], editions: 10, id: "OL1W");
        var b = Books.Make("The Hobbit", ["J. R. R. Tolkien"], editions: 90, id: "OL2W");

        var match = Assert.Single(Rank(Q.Make("the hobbit", "tolkien"), a, b));

        Assert.Equal("OL2W", match.Book.WorkId);
    }

    [Fact]
    public void Year_keyword_breaks_ties_before_edition_count()
    {
        var persuasion = Books.Make("Persuasion", ["Jane Austen"], year: 1817, editions: 5, id: "OL1W");
        var mansfield = Books.Make("Mansfield Park", ["Jane Austen"], year: 1814, editions: 500, id: "OL2W");
        var hypothesis = Q.Make(null, "austen", "1817");

        var ranked = Matcher.Rank([hypothesis], [mansfield, persuasion]);

        Assert.Equal("OL1W", ranked[0].Book.WorkId);
        Assert.True(ranked[0].YearMatches);
    }

    [Fact]
    public void Best_hypothesis_wins_for_each_candidate()
    {
        var book = Books.Make("The Hobbit", ["J.R.R. Tolkien"]);

        var ranked = Matcher.Rank([Q.Make("tolkien hobbit", null), Q.Make("hobbit", "tolkien")], [book]);

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, Assert.Single(ranked).Tier);
    }

    [Fact]
    public void Returns_at_most_the_requested_number_of_results()
    {
        var books = Enumerable.Range(1, 9)
            .Select(i => Books.Make($"Work {i}", ["Charles Dickens"], editions: i, id: $"OL{i}W"))
            .ToArray();

        Assert.Equal(5, Rank(Q.Make(null, "dickens"), books).Count);
    }
}
