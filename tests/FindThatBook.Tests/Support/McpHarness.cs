using System.IO.Pipelines;
using FindThatBook.Application.Abstractions;
using FindThatBook.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FindThatBook.Tests.Support;

/// <summary>The outcome of one tool call: whether the server flagged an error and the text of the first content block.</summary>
public sealed record ToolReply(bool IsError, string Text);

/// <summary>An MCP server built from <c>AddFindThatBookMcp</c> and an MCP client joined by in-memory pipes.</summary>
public sealed class McpHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly McpClient _client;

    private McpHarness(ServiceProvider provider, McpClient client)
    {
        _provider = provider;
        _client = client;
    }

    public static async Task<McpHarness> StartAsync(ISearchService search, ILoggerProvider? logs = null)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null) logging.AddProvider(logs);
        });
        services.AddSingleton(search);
        services.AddFindThatBookMcp()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<McpServer>().RunAsync();
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return new McpHarness(provider, client);
    }

    public async Task<ToolReply> CallAsync(string query)
    {
        var result = await _client.CallToolAsync("search_books", new Dictionary<string, object?> { ["query"] = query });
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        return new ToolReply(result.IsError == true, text);
    }

    public async Task<IList<McpClientTool>> ListToolsAsync() => await _client.ListToolsAsync();

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
