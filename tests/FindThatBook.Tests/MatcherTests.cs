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
    public void Different_books_in_the_same_series_are_not_deduplicated()
    {
        var thief = Books.Make("Percy Jackson: The Lightning Thief", ["Rick Riordan"], editions: 50, id: "OL1W");
        var monsters = Books.Make("Percy Jackson: The Sea of Monsters", ["Rick Riordan"], editions: 40, id: "OL2W");

        var ranked = Rank(Q.Make(null, "riordan"), thief, monsters);

        Assert.Equal(["OL1W", "OL2W"], ranked.Select(r => r.Book.WorkId));
    }

    [Fact]
    public void Title_typed_without_an_apostrophe_still_matches()
    {
        var book = Books.Make("Ender's Game", ["Orson Scott Card"]);

        var match = Assert.Single(Rank(Q.Make("enders game", "card"), book));

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, match.Tier);
    }

    // F1: guessed author splits must not beat an exact title.

    private static readonly CatalogCandidate LittlePrince = Books.Make("The Little Prince", ["Thomas Batchelle"], editions: 3, id: "OL1W");
    private static readonly CatalogCandidate ThreePigs = Books.Make("The Three Little Pigs", ["Sarah Prince"], editions: 9, id: "OL2W");

    [Fact]
    public void An_exact_title_outranks_a_near_title_whose_author_was_only_guessed()
    {
        var guesses = new[] { Q.Guess("the little prince", null), Q.Guess("the little", "prince"), Q.Guess(null, "the little prince") };

        var ranked = Matcher.Rank(guesses, [ThreePigs, LittlePrince]);

        Assert.Equal(["The Little Prince", "The Three Little Pigs"], ranked.Select(r => r.Book.Title));
        Assert.True(ranked[1].FromGuess);
    }

    [Fact]
    public void A_near_title_with_an_author_the_ai_extracted_still_outranks_an_exact_title_alone()
    {
        var exactOnly = Books.Make("Huckleberry", ["Someone Else"], editions: 500, id: "OL9W");
        var twain = Books.Make("Adventures of Huckleberry Finn", ["Mark Twain"], editions: 5, id: "OL8W");

        var ranked = Matcher.Rank([Q.Make("huckleberry", "mark twain")], [exactOnly, twain]);

        Assert.Equal(["OL8W", "OL9W"], ranked.Select(r => r.Book.WorkId));
        Assert.False(ranked[0].FromGuess);
    }

    [Fact]
    public void A_guessed_near_title_with_author_is_still_returned_when_nothing_better_exists()
    {
        var twain = Books.Make("Adventures of Huckleberry Finn", ["Mark Twain"]);

        var match = Assert.Single(Matcher.Rank([Q.Guess("huckleberry", "mark")], [twain]));

        Assert.Equal(MatchTier.NearTitleAuthor, match.Tier);
    }

    [Fact]
    public void Guessed_exact_title_with_a_matching_author_is_still_the_strongest_match()
    {
        var hobbit = Books.Make("The Hobbit", ["J.R.R. Tolkien"], editions: 1);
        var other = Books.Make("The Hobbit", ["Someone Else"], editions: 900, id: "OL7W");

        var ranked = Matcher.Rank([Q.Guess("hobbit", "tolkien"), Q.Guess("tolkien hobbit", null)], [other, hobbit]);

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, ranked[0].Tier);
        Assert.Equal("J.R.R. Tolkien", ranked[0].MatchedAuthor);
    }

    // F2: works whose canonical title and author name are not English.

    private static readonly CatalogCandidate Kafka = Books.Make(
        "海辺のカフカ", ["村上春樹"], editions: 61, id: "OL2625431W",
        altTitles: ["Kafka on the Shore"],
        aliases: ["Murakami, Haruki", "Haruki Murakami", "村上, 春樹"]);

    [Fact]
    public void An_edition_title_matches_when_the_work_title_is_in_another_language()
    {
        var match = Assert.Single(Matcher.Rank([Q.Make("kafka on the shore", null)], [Kafka]));

        Assert.True(match.ExactTitle);
        Assert.Equal("Kafka on the Shore", match.MatchedTitle);
    }

    [Fact]
    public void Author_aliases_match_when_the_display_name_is_not_in_latin_script()
    {
        var match = Assert.Single(Matcher.Rank([Q.Make("kafka on the shore", "murakami")], [Kafka]));

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, match.Tier);
        Assert.Equal(AuthorMatchKind.Primary, match.AuthorMatch);
        Assert.Equal("村上春樹", match.MatchedAuthor);
    }

    [Fact]
    public void The_novel_beats_the_study_guide_for_the_murakami_query()
    {
        var guide = Books.Make("A Study Guide for Haruki Murakami's \"Kafka on the Shore\"", ["Gale Cengage Learning"], editions: 1, id: "OL3W");
        var hypotheses = new[] { Q.Guess("kafka on the shore", "murakami"), Q.Guess("kafka on the shore murakami", null) };

        var ranked = Matcher.Rank(hypotheses, [guide, Kafka]);

        Assert.Equal("OL2625431W", ranked[0].Book.WorkId);
    }

    [Fact]
    public void A_subtitled_edition_title_matches_exactly_once_the_subtitle_is_ignored()
    {
        var work = Books.Make("戦争と平和", ["トルストイ"], altTitles: ["War and Peace: The Maude Translation"], aliases: ["Leo Tolstoy"]);

        var match = Assert.Single(Matcher.Rank([Q.Make("war and peace", "tolstoy")], [work]));

        Assert.Equal(MatchTier.ExactTitlePrimaryAuthor, match.Tier);
        Assert.Equal("War and Peace: The Maude Translation", match.MatchedTitle);
    }

    [Fact]
    public void The_work_title_is_preferred_over_an_edition_title_when_both_match()
    {
        var book = Books.Make("The Hobbit", ["J.R.R. Tolkien"], altTitles: ["The Hobbit"]);

        var match = Assert.Single(Matcher.Rank([Q.Make("the hobbit", "tolkien")], [book]));

        Assert.Null(match.MatchedTitle);
    }

    [Fact]
    public void A_work_with_no_matching_title_or_edition_title_is_still_excluded()
    {
        var other = Books.Make("1Q84", ["Haruki Murakami"], altTitles: ["Ichi-kyū-hachi-yon"]);

        Assert.Empty(Matcher.Rank([Q.Make("kafka on the shore", "murakami")], [other]));
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
