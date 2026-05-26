using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.KnowledgeBase.Queries.GetStats;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/stats")]
public sealed class KnowledgeBaseController(IMediator mediator, IRetrievalAgent retrieval) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Stats(CancellationToken ct) =>
        Ok(await mediator.Send(new GetKnowledgeBaseStatsQuery(), ct));

    [HttpGet("health")]
    public async Task<IActionResult> Health() =>
        Ok(new { alive = await retrieval.IsAliveAsync() });
}
