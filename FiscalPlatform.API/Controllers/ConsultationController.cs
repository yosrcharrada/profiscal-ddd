using FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;
using FiscalPlatform.Application.Consultation.Commands.RefineConsultation;
using FiscalPlatform.Application.Consultation.Queries.GetConsultationHistory;
using FiscalPlatform.API.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/consultation")]
public sealed class ConsultationController(IMediator mediator) : ControllerBase
{
    [HttpPost("generate")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Generate([FromBody] GenerateConsultationApiRequest req, CancellationToken ct)
    {
        var attachedTexts = new List<string>();

        var reference  = string.IsNullOrWhiteSpace(req.Reference) ? $"CONS-{DateTime.Now:yyyy-MMdd}" : req.Reference;
        var clientName = string.IsNullOrWhiteSpace(req.ClientName) ? "Client" : req.ClientName;

        var command = new GenerateConsultationCommand(
            reference, clientName, req.Situation, req.FiscalQuestion,
            req.Documents ?? new(), attachedTexts.Where(t => !string.IsNullOrEmpty(t)).ToList());

        var result = await mediator.Send(command, ct);
        return File(result.DocBytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            result.Filename);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string client, CancellationToken ct) =>
        Ok(await mediator.Send(new GetConsultationHistoryQuery(client), ct));

    [HttpPost("session/start")]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest req, CancellationToken ct)
    {
        var sessionId = await mediator.Send(
            new StartConsultationSessionCommand(req.ConsultationId, req.ClientName, req.Reference), ct);
        return Ok(new { sessionId });
    }

    [HttpPost("session/end")]
    public async Task<IActionResult> EndSession([FromBody] EndSessionRequest req, CancellationToken ct)
    {
        await mediator.Send(new EndConsultationSessionCommand(req.SessionId), ct);
        return Ok(new { message = "Session archived." });
    }
}
