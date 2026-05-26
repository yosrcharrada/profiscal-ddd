using FiscalPlatform.Application.Consultation.Commands.RefineConsultation;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.API.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/refinement")]
public sealed class RefinementController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Send a message in a consultation refinement conversation.
    /// Implements Human-in-the-Loop + State Machine patterns.
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] RefineConsultationApiRequest req, CancellationToken ct)
    {
        var command = new RefineConsultationCommand(
            req.SessionId, req.UserMessage,
            req.CurrentOutput ?? new ConsultationOutput(),
            req.Sources ?? new());

        var result = await mediator.Send(command, ct);
        return Ok(new
        {
            reply          = result.AssistantReply,
            sectionName    = result.SectionName,
            updatedOutput  = result.UpdatedOutput,
            sessionId      = result.SessionId,
        });
    }
}
