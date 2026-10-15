using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Application.Abstractions;
using FindThatBook.Infrastructure.Ai;
using FindThatBook.Infrastructure.OpenAi;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests;

public class OpenAiCompatibleClientTests
{
    private static readonly JsonNode ObjectSchema = JsonNode.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""")!;
    private static readonly JsonNode ArraySchema = JsonNode.Parse("""{"type":"array","items":{"type":"string"}}""")!;

    private static string Completion(string? content)
        => JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    private static OpenAiCompatibleClient Client(StubHandler handler, OpenAiCompatibleOptions? options = null, string? model = null)
    {
        options ??= new OpenAiCompatibleOptions { ApiKey = "test-key" };
        return new OpenAiCompatibleClient(
            new HttpClient(handler) { BaseAddress = options.BaseUri },
            Options.Create(options), Options.Create(new AiGenerationOptions { Model = model }));
    }

    [Fact]
    public async Task Posts_a_json_mode_chat_completion_with_bearer_auth()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var handler = new StubHandler(req =>
        {
            seen = req;
            body = req.Content!.ReadAsStringAsync().Result;
            return StubHandler.Json(Completion("""{"a":"x"}"""));
        });

        var text = await Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "find a book"), ObjectSchema, default);

        Assert.Equal("""{"a":"x"}""", text);
        Assert.Equal("/openai/v1/chat/completions", seen!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer test-key", seen.Headers.Authorization!.ToString());

        var json = JsonNode.Parse(body!)!;
        Assert.Equal("openai/gpt-oss-20b", json["model"]!.GetValue<string>());
        Assert.Equal("json_object", json["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(0, json["temperature"]!.GetValue<int>());
        Assert.False(json["stream"]!.GetValue<bool>(), "gateways that default to streaming must be told not to");
        var messages = json["messages"]!.AsArray();
        Assert.Contains("find a book", messages.Select(m => m!["content"]!.GetValue<string>()));
        Assert.Contains(messages, m => m!["role"]!.GetValue<string>() == "system"
            && m["content"]!.GetValue<string>().Contains("JSON") && m["content"]!.GetValue<string>().Contains("\"properties\""));
    }

    [Fact]
    public async Task Uses_the_configured_model_and_the_default_when_blank()
    {
        string? model = null;
        var handler = new StubHandler(req =>
        {
            model = JsonNode.Parse(req.Content!.ReadAsStringAsync().Result)!["model"]!.GetValue<string>();
            return StubHandler.Json(Completion("{}"));
        });

        await Client(handler, new OpenAiCompatibleOptions { ApiKey = "k" }, "llama-x").GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default);
        Assert.Equal("llama-x", model);

        await Client(handler, new OpenAiCompatibleOptions { ApiKey = "k" }, "  ").GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default);
        Assert.Equal(OpenAiCompatibleOptions.DefaultModel, model);
    }

    [Fact]
    public async Task Sends_no_authorization_header_when_no_key_is_configured()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(req =>
        {
            seen = req;
            return StubHandler.Json(Completion("{}"));
        });

        await Client(handler, new OpenAiCompatibleOptions { ApiKey = "" }).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default);

        Assert.Null(seen!.Headers.Authorization);
    }

    [Fact]
    public async Task Array_root_schemas_are_wrapped_for_json_mode_and_unwrapped_in_the_reply()
    {
        string? system = null;
        var handler = new StubHandler(req =>
        {
            var messages = JsonNode.Parse(req.Content!.ReadAsStringAsync().Result)!["messages"]!.AsArray();
            system = messages.First(m => m!["role"]!.GetValue<string>() == "system")!["content"]!.GetValue<string>();
            return StubHandler.Json(Completion("""{"result":["a","b"]}"""));
        });

        var text = await Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ArraySchema, default);

        Assert.Equal("""["a","b"]""", text);
        Assert.Contains("\"result\"", system);
    }

    [Theory]
    [InlineData("""{"other":[]}""")]
    [InlineData("""["not","wrapped"]""")]
    [InlineData("not json")]
    public async Task Wrapped_reply_without_a_result_is_unavailable(string content)
    {
        var handler = new StubHandler(_ => StubHandler.Json(Completion(content)));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ArraySchema, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Http_errors_are_unavailable(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default));
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":null}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":42}}]}""")]
    [InlineData("""{}""")]
    [InlineData("""<html>gateway</html>""")]
    public async Task Missing_or_malformed_message_content_is_unavailable(string reply)
    {
        var handler = new StubHandler(_ => StubHandler.Json(reply));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default));
    }

    [Fact]
    public async Task Network_failure_is_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<AiUnavailableException>(() => Client(handler).GenerateJsonAsync(new AiPrompt("Reply in JSON.", "p"), ObjectSchema, default));
    }

    [Theory]
    [InlineData("https://api.groq.com/openai/v1", "https://api.groq.com/openai/v1/")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/")]
    [InlineData("", "https://api.groq.com/openai/v1/")]
    [InlineData("   ", "https://api.groq.com/openai/v1/")]
    public void Base_uri_always_ends_with_a_slash_and_defaults_to_groq(string baseUrl, string expected)
        => Assert.Equal(expected, new OpenAiCompatibleOptions { BaseUrl = baseUrl }.BaseUri.ToString());
}
