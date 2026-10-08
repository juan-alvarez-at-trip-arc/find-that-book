using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Controllers;

/// <summary>HTTP API for finding books from a free-text query, plus a health probe.</summary>
/// <param name="search">Service that runs the search pipeline.</param>
[ApiController]
[Route("api")]
public sealed class SearchController(ISearchService search) : ControllerBase
{
    /// <summary>Runs a book search for the free-text query in the request body.</summary>
    /// <param name="request">The request carrying the user's query.</param>
    /// <param name="ct">Cancellation token tied to the HTTP request.</param>
    /// <returns>The search response, or a 400 problem details result when the query is empty or whitespace.</returns>
    [HttpPost("search")]
    public async Task<ActionResult<SearchResponse>> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Problem(title: "Query is required.", statusCode: StatusCodes.Status400BadRequest);

        return await search.SearchAsync(request.Query, ct);
    }

    /// <summary>Reports that the API is running, for container and load balancer health checks. It does not call Open Library.</summary>
    /// <returns>A 200 result with <c>{ "status": "ok" }</c>.</returns>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok" });
}
