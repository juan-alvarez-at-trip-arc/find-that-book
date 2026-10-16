using System.ComponentModel;
using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FindThatBook.Mcp;

/// <summary>The MCP tools of the server. The only tool searches Open Library for the book a reader describes.</summary>
/// <param name="search">The search pipeline shared with the API.</param>
[McpServerToolType]
public sealed class BookSearchTools(ISearchService search)
{
    /// <summary>The error shown when the query is blank.</summary>
    public const string EmptyQueryMessage = "The query is empty. Describe the book with a title, an author or both.";
    /// <summary>The error shown when Open Library cannot be searched.</summary>
    public const string CatalogUnavailableMessage = "Open Library is unavailable or cooling down. Try again in a minute.";

    /// <summary>Finds up to 5 ranked books for a messy description and explains each match in one sentence.</summary>
    /// <param name="query">What the reader remembers: a title, an author, both with extra words, a misspelling or a year.</param>
    /// <param name="ct">Token that cancels the search.</param>
    /// <returns>The ranked results, whether the AI was used end to end, and a notice when the best match is weak or nothing matched.</returns>
    /// <exception cref="McpException">The query is blank, or Open Library cannot be searched. The message is shown to the client.</exception>
    [McpServerTool(Name = "search_books", ReadOnly = true)]
    [Description("Finds books from a messy or partial description: a title, an author, both with extra words, misspellings or a year (for example 'tolkien hobbit illustrated 1937'). Returns up to 5 ranked Open Library matches, each with a one-sentence explanation. Not for ISBNs or long text. Only the first 200 characters are used.")]
    public async Task<SearchResponse> SearchBooksAsync(
        [Description("What the reader remembers about the book.")] string query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new McpException(EmptyQueryMessage);
        try
        {
            return await search.SearchAsync(query, ct);
        }
        catch (CatalogUnavailableException)
        {
            throw new McpException(CatalogUnavailableMessage);
        }
    }
}
