using FiscalPlatform.Application.Common.DTOs;
using MediatR;

namespace FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;

public sealed record GenerateConsultationCommand(
    string        Reference,
    string        ClientName,
    string        Situation,
    string        FiscalQuestion,
    List<string>  Documents,
    List<string>? AttachedDocumentTexts = null
) : IRequest<ConsultationGeneratedDto>;
