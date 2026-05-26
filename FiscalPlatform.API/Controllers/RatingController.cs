using FiscalPlatform.Application.Consultation.Commands.RateConsultation;
using FiscalPlatform.API.Requests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FiscalPlatform.API.Controllers;

[ApiController]
[Route("api/rating")]
public sealed class RatingController(IMediator mediator) : ControllerBase
{
    /// <summary>Submit a 1-5 star rating for a consultation.</summary>
    [HttpPost]
    public async Task<IActionResult> Rate([FromBody] RateConsultationApiRequest req, CancellationToken ct)
    {
        var ok = await mediator.Send(
            new RateConsultationCommand(req.ConsultationId, req.Reference, req.Stars, req.Comment), ct);
        return ok ? Ok(new { message = $"Note {req.Stars}★ enregistrée." })
                  : StatusCode(500, new { error = "Erreur lors de l'enregistrement." });
    }
}
