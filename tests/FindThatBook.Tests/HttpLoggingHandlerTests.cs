using System.Net;
using System.Text;
using FindThatBook.Infrastructure.Telemetry;
using FindThatBook.Tests.Support;
using Microsoft.Extensions.Logging;

namespace FindThatBook.Tests;

public class HttpLoggingHandlerTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private static (HttpClient Client, ListLogger<HttpLoggingHandler> Log) Client(Func<HttpRequestMessage, HttpResponseMessage> respond, int maxBodyChars = 8192)
    {
        var log = new ListLogger<HttpLoggingHandler>();
        var handler = new HttpLoggingHandler(log, new SensitiveDataMasker(["sk-live-123456"]), maxBodyChars)
        {
            InnerHandler = new StubHandler(respond),
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }, log);
    }

    [Fact]
    public async Task Logs_request_and_response_bodies_and_the_caller_still_reads_them()
    {
        var (client, log) = Client(_ => StubHandler.Json("""{"answer":"hobbit"}"""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat")
        {
            Content = new StringContent("""{"prompt":"find hobbit"}""", Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request);

        Assert.Equal("""{"answer":"hobbit"}""", await response.Content.ReadAsStringAsync());
        var all = string.Join("\n", log.Messages);
        Assert.Contains("""{"prompt":"find hobbit"}""", all);
        Assert.Contains("""{"answer":"hobbit"}""", all);
        Assert.Contains("200", all);
    }

    [Fact]
    public async Task Never_logs_credentials_from_headers_urls_or_bodies()
    {
        var (client, log) = Client(_ => StubHandler.Json("""{"echo":"sk-live-123456","token":"t0k"}"""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat?key=urlsecret")
        {
            Content = new StringContent("""{"apiKey":"bodysecret"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-goog-api-key", "headersecret");
        request.Headers.Add("Authorization", "Bearer bearersecret");

        using var response = await client.SendAsync(request);

        var all = string.Join("\n", log.Messages);
        foreach (var secret in new[] { "urlsecret", "bodysecret", "headersecret", "bearersecret", "sk-live-123456", "t0k" })
            Assert.DoesNotContain(secret, all);
    }

    [Fact]
    public async Task Truncates_long_bodies()
    {
        var (client, log) = Client(_ => StubHandler.Json(new string('a', 500)), maxBodyChars: 100);

        using var response = await client.GetAsync("big");

        var all = string.Join("\n", log.Messages);
        Assert.DoesNotContain(new string('a', 101), all);
        Assert.Contains("truncated", all);
    }

    [Fact]
    public async Task Logs_a_failed_request_and_rethrows()
    {
        var (client, log) = Client(_ => throw new HttpRequestException("boom"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("x"));

        Assert.Contains(log.Messages, m => m.Contains("boom"));
    }

    [Fact]
    public async Task Does_not_dump_non_text_bodies()
    {
        var (client, log) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/jpeg") } },
        });

        using var response = await client.GetAsync("cover");

        Assert.Contains(log.Messages, m => m.Contains("binary"));
    }
}
