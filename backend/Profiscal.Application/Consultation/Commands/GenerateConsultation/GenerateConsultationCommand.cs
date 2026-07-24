using Profiscal.Domain.Dtos;
using MediatR;

namespace Profiscal.Application.Consultation.Commands.GenerateConsultation;

public sealed record GenerateConsultationCommand(
    string        Reference,
    string        ClientName,
    string        Situation,
    string        FiscalQuestion,
    List<string>  Documents,
    List<string>? AttachedDocumentTexts = null,
    // "concise" = verdict-first, straight to the point; "detaillee" = full reasoning (default).
    string        Mode = "detaillee"
) : IRequest<ConsultationGeneratedDto>;
