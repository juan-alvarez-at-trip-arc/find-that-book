using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Explanations;
using FindThatBook.Application.Matching;
using FindThatBook.Application.Parsing;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace FindThatBook.Tests;

public class FallbackTests
{
    [Fact]
    public async Task Parser_falls_back_when_gemini_is_unavailable()
    {
        var failing = new GeminiQueryParser(new FakeGeminiClient(_ => throw new AiUnavailableException("down")));
        var parser = new FallbackQueryParser(failing, new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance);

        var outcome = await parser.ParseAsync("tolkien hobbit", default);

        Assert.False(outcome.AiUsed);
        Assert.NotEmpty(outcome.Hypotheses);
    }

    [Fact]
    public async Task Parser_falls_back_on_malformed_gemini_output()
    {
        var bad = new GeminiQueryParser(new FakeGeminiClient(_ => "nonsense"));
        var parser = new FallbackQueryParser(bad, new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance);

        Assert.False((await parser.ParseAsync("tolkien hobbit", default)).AiUsed);
    }

    [Fact]
    public async Task Parser_uses_the_primary_when_it_works()
    {
        var good = new GeminiQueryParser(new FakeGeminiClient(_ => """{"title":"The Hobbit","author":"Tolkien","keywords":[]}"""));
        var parser = new FallbackQueryParser(good, new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance);

        Assert.True((await parser.ParseAsync("x", default)).AiUsed);
    }

    [Fact]
    public async Task Other_exceptions_are_not_swallowed()
    {
        var broken = new GeminiQueryParser(new FakeGeminiClient(_ => throw new InvalidOperationException("bug")));
        var parser = new FallbackQueryParser(broken, new HeuristicQueryParser(), NullLogger<FallbackQueryParser>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => parser.ParseAsync("x", default));
    }

    [Fact]
    public async Task Writer_falls_back_to_templates()
    {
        var book = Books.Make("The Hobbit", ["J.R.R. Tolkien"]);
        var matches = Matcher.Rank([Q.Make("the hobbit", "tolkien")], [book]);
        var failing = new GeminiExplanationWriter(new FakeGeminiClient(_ => "garbage"));
        var writer = new FallbackExplanationWriter(failing, new TemplateExplanationWriter(), NullLogger<FallbackExplanationWriter>.Instance);

        var outcome = await writer.WriteAsync(matches, default);

        Assert.False(outcome.AiUsed);
        Assert.StartsWith("Exact title match", outcome.Explanations[0]);
    }
}
