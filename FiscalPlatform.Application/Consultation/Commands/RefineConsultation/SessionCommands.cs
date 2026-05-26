using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Domain.Repositories;
using MediatR;

namespace FiscalPlatform.Application.Consultation.Commands.RefineConsultation;

// ── Start session ─────────────────────────────────────────────────────────────
public sealed record StartConsultationSessionCommand(
    Guid ConsultationId, string ClientName, string Reference) : IRequest<string>;

public sealed class StartConsultationSessionCommandHandler(
    ISessionStore sessionStore, IConsultationRepository repository)
    : IRequestHandler<StartConsultationSessionCommand, string>
{
    public async Task<string> Handle(StartConsultationSessionCommand cmd, CancellationToken ct)
    {
        var sessionId = $"sess_{Guid.NewGuid():N}";
        sessionStore.Set(sessionId, new ConversationSession
        {
            SessionId      = sessionId,
            ConsultationId = cmd.ConsultationId,
            ClientName     = cmd.ClientName,
            Reference      = cmd.Reference,
        });

        var consultation = await repository.GetByIdAsync(cmd.ConsultationId, ct);
        consultation?.BeginSession(sessionId);
        if (consultation is not null) _ = repository.SaveAsync(consultation, ct);

        return sessionId;
    }
}

// ── End session (archives to ES via Choreography) ─────────────────────────────
public sealed record EndConsultationSessionCommand(string SessionId) : IRequest;

public sealed class EndConsultationSessionCommandHandler(
    ISessionStore sessionStore, IConsultationRepository repository)
    : IRequestHandler<EndConsultationSessionCommand>
{
    public async Task Handle(EndConsultationSessionCommand cmd, CancellationToken ct)
    {
        var session = sessionStore.Get(cmd.SessionId);
        if (session is null) return;

        var consultation = await repository.GetByIdAsync(session.ConsultationId, ct);
        consultation?.EndSession();
        if (consultation is not null) _ = repository.SaveAsync(consultation, ct); // archives event

        sessionStore.Remove(cmd.SessionId);
    }
}
