using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Domain.Exceptions;
using FiscalPlatform.Domain.Repositories;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Consultation.Commands.RateConsultation;

public sealed record RateConsultationCommand(
    Guid ConsultationId, string Reference, int Stars, string? Comment)
    : IRequest<bool>;

public sealed class RateConsultationCommandValidator : AbstractValidator<RateConsultationCommand>
{
    public RateConsultationCommandValidator()
    {
        RuleFor(x => x.Stars).InclusiveBetween(1, 5).WithMessage("La note doit être entre 1 et 5.");
        RuleFor(x => x.Reference).NotEmpty().WithMessage("La référence est requise.");
    }
}

public sealed class RateConsultationCommandHandler(
    IConsultationRepository repository,
    IFeedbackAgent feedbackAgent,
    ILogger<RateConsultationCommandHandler> logger)
    : IRequestHandler<RateConsultationCommand, bool>
{
    public async Task<bool> Handle(RateConsultationCommand cmd, CancellationToken ct)
    {
        logger.LogInformation("Rating consultation [{Ref}]: {Stars}★", cmd.Reference, cmd.Stars);

        // Save rating to feedback store (Event-Driven — async, non-blocking)
        _ = feedbackAgent.SaveRatingAsync(cmd.ConsultationId, cmd.Reference,
            "", cmd.Stars, cmd.Comment, ct);

        // Update the domain aggregate if found
        var consultation = await repository.GetByIdAsync(cmd.ConsultationId, ct);
        if (consultation is not null)
        {
            consultation.Rate(cmd.Stars, cmd.Comment);
            await repository.SaveAsync(consultation, ct);
        }

        return true;
    }
}
