using FindThatBook.Application.Parsing;

namespace FindThatBook.Tests;

public class HeuristicQueryParserTests
{
    private readonly HeuristicQueryParser _parser = new();

    [Fact]
    public async Task Noisy_blob_yields_split_hypotheses_and_keywords()
    {
        var outcome = await _parser.ParseAsync("tolkien hobbit illustrated deluxe 1937", default);

        Assert.False(outcome.AiUsed);
        Assert.Contains(outcome.Hypotheses, h => h.Author == "tolkien" && h.Title == "hobbit");
        Assert.Contains(outcome.Hypotheses, h => h.Title == "tolkien hobbit" && h.Author is null);
        Assert.Contains(outcome.Hypotheses, h => h.Author == "tolkien hobbit" && h.Title is null);
        Assert.All(outcome.Hypotheses, h => Assert.Equal(["illustrated", "deluxe", "1937"], h.Keywords));
    }

    [Fact]
    public async Task Two_word_author_at_the_end_is_tried_as_author()
    {
        var outcome = await _parser.ParseAsync("pride and prejudice jane austen", default);

        Assert.Contains(outcome.Hypotheses, h => h.Author == "jane austen" && h.Title == "pride and prejudice");
    }

    [Fact]
    public async Task Single_word_is_tried_as_title_and_as_author()
    {
        var outcome = await _parser.ParseAsync("dickens", default);

        Assert.Equal(2, outcome.Hypotheses.Count);
        Assert.Contains(outcome.Hypotheses, h => h.Title == "dickens");
        Assert.Contains(outcome.Hypotheses, h => h.Author == "dickens");
    }

    [Theory]
    [InlineData("1937")]
    [InlineData("???")]
    [InlineData("illustrated deluxe 1937")]
    public async Task Query_with_no_real_words_yields_no_hypotheses(string query)
    {
        var outcome = await _parser.ParseAsync(query, default);

        Assert.Empty(outcome.Hypotheses);
    }

    [Fact]
    public async Task Diacritics_are_stripped()
    {
        var outcome = await _parser.ParseAsync("García Márquez", default);

        Assert.Contains(outcome.Hypotheses, h => h.Title == "garcia marquez");
    }
}
