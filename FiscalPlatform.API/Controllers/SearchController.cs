using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Search.Queries.SearchLegalDocuments;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(IMediator mediator, ISearchAgent searchAgent) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequestDto req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query)) return BadRequest(new { error = "Query required." });
        return Ok(await mediator.Send(new SearchLegalDocumentsQuery(req), ct));
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var alive = await searchAgent.IsAliveAsync();
        var count = alive ? await searchAgent.CountAsync() : 0;
        return Ok(new { alive, count });
    }
}
