using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Matching;
using FindThatBook.Application.Models;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class GeminiTests
{
    private static string Envelope(string text)
        => JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } });

    private static GeminiClient Client(StubHandler handler, string? key = "test-key", string? model = null)
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") },
               Options.Create(model is null ? new GeminiOptions { ApiKey = key } : new GeminiOptions { ApiKey = key, Model = model }));

    private static readonly JsonNode Schema = JsonNode.Parse("""{"type":"OBJECT"}""")!;

    [Fact]
    public async Task Client_returns_the_text_part_and_sends_the_key_header()
    {
        string? keyHeader = null;
        var handler = new StubHandler(req =>
        {
            keyHeader = req.Headers.GetValues("x-goog-api-key").Single();
            return StubHandler.Json(Envelope("""{"ok":true}"""));
        });

        var text = await Client(handler).GenerateJsonAsync("hi", Schema, default);

        Assert.Equal("""{"ok":true}""", text);
        Assert.Equal("test-key", keyHeader);
        Assert.Contains("models/gemini-3.8-flash:generateContent", handler.Requests.Single());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Client_uses_the_default_model_when_the_configured_one_is_blank(string model)
    {
        var handler = new StubHandler(_ => StubHandler.Json(Envelope("""{"ok":true}""")));

        await Client(handler, model: model).GenerateJsonAsync("hi", Schema, default);

        Assert.Contains("models/gemini-3.8-flash:generateContent", handler.Requests.Single());
    }

    [Fact]
    public async Task Client_without_a_key_throws_before_any_request()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no request expected"));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler, key: "").GenerateJsonAsync("hi", Schema, default));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Client_wraps_http_errors(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync("hi", Schema, default));
    }

    [Fact]
    public async Task Client_throws_when_the_reply_has_no_text()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"candidates":[]}"""));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync("hi", Schema, default));
    }

    [Fact]
    public async Task Client_throws_ai_unavailable_when_the_text_part_is_not_a_string()
    {
        var handler = new StubHandler(_ => StubHandler.Json("""{"candidates":[{"content":{"parts":[{"text":123}]}}]}"""));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync("hi", Schema, default));
    }

    [Fact]
    public async Task Parser_maps_json_to_a_single_hypothesis()
    {
        var gemini = new FakeGeminiClient(_ => """{"title":"The Hobbit","author":"J.R.R. Tolkien","keywords":["1937","illustrated"]}""");

        var outcome = await new GeminiQueryParser(gemini).ParseAsync("tolkien hobbit illustrated deluxe 1937", default);

        Assert.True(outcome.AiUsed);
        var h = Assert.Single(outcome.Hypotheses);
        Assert.Equal("The Hobbit", h.Title);
        Assert.Equal("J.R.R. Tolkien", h.Author);
        Assert.Equal(["1937", "illustrated"], h.Keywords);
    }

    [Fact]
    public async Task Parser_accepts_a_null_title_for_author_only_queries()
    {
        var gemini = new FakeGeminiClient(_ => """{"title":null,"author":"Charles Dickens","keywords":[]}""");

        var outcome = await new GeminiQueryParser(gemini).ParseAsync("dickens", default);

        Assert.Null(Assert.Single(outcome.Hypotheses).Title);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("```json\n{\"title\":\"x\"}\n```")]
    [InlineData("""{"title":null,"author":null,"keywords":[]}""")]
    [InlineData("""{"title":"  ","author":"","keywords":[]}""")]
    [InlineData("[]")]
    public async Task Parser_rejects_malformed_or_empty_output(string reply)
    {
        var gemini = new FakeGeminiClient(_ => reply);

        await Assert.ThrowsAsync<AiUnavailableException>(() => new GeminiQueryParser(gemini).ParseAsync("x", default));
    }

    private static MatchedBook[] Matches()
    {
        var a = Books.Make("The Hobbit", ["J.R.R. Tolkien"], ["Dixon"]);
        var b = Books.Make("Oliver Twist", ["Charles Dickens"]);
        return [Matcher.Rank([Q.Make("the hobbit", "tolkien")], [a])[0], Matcher.Rank([Q.Make("oliver twist", "dickens")], [b])[0]];
    }

    [Fact]
    public async Task Writer_returns_explanations_in_match_order()
    {
        var gemini = new FakeGeminiClient(_ => """[{"index":1,"explanation":"Second."},{"index":0,"explanation":"First."}]""");

        var outcome = await new GeminiExplanationWriter(gemini).WriteAsync(Matches(), default);

        Assert.True(outcome.AiUsed);
        Assert.Equal(["First.", "Second."], outcome.Explanations);
    }

    [Theory]
    [InlineData("""[{"index":0,"explanation":"Only one."}]""")]
    [InlineData("""[{"index":0,"explanation":""},{"index":1,"explanation":"Two."}]""")]
    [InlineData("""{"not":"an array"}""")]
    [InlineData("garbage")]
    public async Task Writer_rejects_incomplete_or_malformed_output(string reply)
    {
        var gemini = new FakeGeminiClient(_ => reply);

        await Assert.ThrowsAsync<AiUnavailableException>(() => new GeminiExplanationWriter(gemini).WriteAsync(Matches(), default));
    }

    [Fact]
    public async Task Writer_prompt_contains_the_facts_it_may_cite()
    {
        string? prompt = null;
        var gemini = new FakeGeminiClient(p =>
        {
            prompt = p;
            return """[{"index":0,"explanation":"a"},{"index":1,"explanation":"b"}]""";
        });

        await new GeminiExplanationWriter(gemini).WriteAsync(Matches(), default);

        Assert.Contains("J.R.R. Tolkien", prompt);
        Assert.Contains("Dixon", prompt);
        Assert.Contains("ExactTitlePrimaryAuthor", prompt);
    }
}
