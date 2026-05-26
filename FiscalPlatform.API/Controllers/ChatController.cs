using FiscalPlatform.Application.Chat.Queries.Chat;
using FiscalPlatform.API.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatApiRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question)) return BadRequest(new { error = "Question required." });
        var result = await mediator.Send(new ChatQuery(req.Question, req.History ?? new()), ct);
        return Ok(result);
    }
}
