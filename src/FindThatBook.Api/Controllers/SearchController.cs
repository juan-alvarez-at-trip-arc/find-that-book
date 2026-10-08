using FindThatBook.Application.Abstractions;
using FindThatBook.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SearchController(ISearchService search) : ControllerBase
{
    [HttpPost("search")]
    public async Task<ActionResult<SearchResponse>> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Problem(title: "Query is required.", statusCode: StatusCodes.Status400BadRequest);

        return await search.SearchAsync(request.Query, ct);
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok" });
}
