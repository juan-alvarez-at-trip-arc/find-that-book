using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FindThatBook.Mcp;

/// <summary>Dependency injection registration for the MCP server.</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>Registers the MCP server and the book search tools. It adds no transport, so the caller chooses one. <c>ISearchService</c> must be registered separately.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The MCP server builder, for attaching a transport.</returns>
    public static IMcpServerBuilder AddFindThatBookMcp(this IServiceCollection services)
        => services.AddMcpServer().WithTools<BookSearchTools>();
}
