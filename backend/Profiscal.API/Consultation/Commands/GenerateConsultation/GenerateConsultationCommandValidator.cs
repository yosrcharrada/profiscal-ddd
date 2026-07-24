using FluentValidation;

namespace FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;

public sealed class GenerateConsultationCommandValidator
    : AbstractValidator<GenerateConsultationCommand>
{
    public GenerateConsultationCommandValidator()
    {
        RuleFor(x => x.Situation)
            .NotEmpty().WithMessage("La description de la situation est requise.")
            .MinimumLength(20).WithMessage("La situation doit comporter au moins 20 caractères.");

        RuleFor(x => x.FiscalQuestion)
            .NotEmpty().WithMessage("La question fiscale est requise.")
            .MinimumLength(10).WithMessage("La question fiscale doit comporter au moins 10 caractères.");

        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("Le nom du client est requis.");
    }
}
