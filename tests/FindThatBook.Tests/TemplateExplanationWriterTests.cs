using FindThatBook.Application.Explanations;
using FindThatBook.Application.Matching;
using FindThatBook.Application.Models;
using FindThatBook.Tests.Support;

namespace FindThatBook.Tests;

public class TemplateExplanationWriterTests
{
    private static MatchedBook Match(ParsedQuery q, CatalogCandidate book) => Matcher.Rank([q], [book])[0];

    [Fact]
    public void Tier_1_cites_primary_author_contributors_and_year()
    {
        var book = Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"], year: 1937);

        var text = TemplateExplanationWriter.Describe(Match(Q.Make("the hobbit", "tolkien", "1937"), book));

        Assert.Equal("Exact title match; J.R.R. Tolkien is primary author; Dixon listed as contributor; first published 1937, matching the query.", text);
    }

    [Fact]
    public void Tier_2_says_the_author_is_only_a_contributor()
    {
        var book = Books.Make("The Hobbit", ["Someone Else"], ["Tolkien"]);

        var text = TemplateExplanationWriter.Describe(Match(Q.Make("the hobbit", "tolkien"), book));

        Assert.Equal("Exact title match; Tolkien is listed only as a contributor; primary author is Someone Else.", text);
    }

    [Fact]
    public void Author_only_and_title_only_have_distinct_wording()
    {
        var dickens = Books.Make("Oliver Twist", ["Charles Dickens"]);
        var tale = Books.Make("A Tale of Two Cities", ["Charles Dickens"]);

        Assert.Equal("No title given; top work by primary author Charles Dickens.",
            TemplateExplanationWriter.Describe(Match(Q.Make(null, "dickens"), dickens)));
        Assert.Equal("Close title match; no matching author in the query; primary author is Charles Dickens.",
            TemplateExplanationWriter.Describe(Match(Q.Make("tale two cities", null), tale)));
    }

    [Fact]
    public void Unenriched_candidate_mentions_missing_author_details()
    {
        var book = Books.Make("The Hobbit", [], ["Tolkien"], enriched: false);

        var text = TemplateExplanationWriter.Describe(Match(Q.Make("the hobbit", "tolkien"), book));

        Assert.EndsWith("author details unavailable.", text);
    }

    [Fact]
    public async Task WriteAsync_returns_one_explanation_per_match_with_ai_flag_false()
    {
        var book = Books.Make("The Hobbit", ["J.R.R. Tolkien"]);

        var outcome = await new TemplateExplanationWriter().WriteAsync([Match(Q.Make("the hobbit", "tolkien"), book)], default);

        Assert.False(outcome.AiUsed);
        Assert.Single(outcome.Explanations);
    }
}
