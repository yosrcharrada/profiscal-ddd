using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Consultation.Commands.RefineConsultation;

public sealed record RefineConsultationCommand(
    string             SessionId,
    string             UserMessage,
    ConsultationOutput CurrentOutput,
    List<LegalSourceDto> Sources
) : IRequest<RefineConsultationResult>;

public sealed record RefineConsultationResult(
    string             AssistantReply,
    string             SectionName,
    ConsultationOutput UpdatedOutput,
    string             SessionId);

public sealed class RefineConsultationCommandValidator : AbstractValidator<RefineConsultationCommand>
{
    public RefineConsultationCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().WithMessage("SessionId requis.");
        RuleFor(x => x.UserMessage).NotEmpty().WithMessage("Message requis.");
    }
}

/// <summary>
/// Handles consultation refinement via conversation.
/// State Machine pattern: session states are Generated → UnderReview → Refined.
/// Memory: volatile (in-process ConcurrentDictionary) for active session,
///         archived to ES when session ends.
/// </summary>
public sealed class RefineConsultationCommandHandler(
    ILlmAgent     llmAgent,
    ISessionStore sessionStore,
    ILogger<RefineConsultationCommandHandler> logger)
    : IRequestHandler<RefineConsultationCommand, RefineConsultationResult>
{
    private const string RefineSystemPrompt =
        "Tu es Faiez Choyakh, fiscaliste EY Tunisia. " +
        "Tu ameliores une consultation fiscale selon les instructions de l utilisateur. " +
        "Conserve le meme style professionnel et les citations [Sn]. " +
        "Reponds directement avec le texte corrige de la section demandee.";

    public async Task<RefineConsultationResult> Handle(
        RefineConsultationCommand cmd, CancellationToken ct)
    {
        // ── State Machine: Load or verify session ────────────────────────────
        var session = sessionStore.Get(cmd.SessionId)
            ?? throw new SessionNotFoundException(cmd.SessionId);

        logger.LogInformation("Refine [{Session}]: '{Msg}'",
            cmd.SessionId, cmd.UserMessage[..Math.Min(cmd.UserMessage.Length, 60)]);

        // ── Build conversation context for LLM ───────────────────────────────
        var contextMsg =
            $"CONSULTATION ACTUELLE:\nSommaire: {cmd.CurrentOutput.SommairExecutif[..Math.Min(cmd.CurrentOutput.SommairExecutif.Length, 300)]}\n" +
            $"SOURCES DISPONIBLES:\n" +
            string.Join("\n", cmd.Sources.Take(10)
                .Select(s => $"[S{s.Index}] {s.DocType} {s.DocName} ({s.Year}) — {s.ArticleRef}"));

        // First turn: add context
        if (session.History.Count == 0)
        {
            session.History.Add(("user", contextMsg));
            session.History.Add(("assistant", "J'ai bien pris connaissance de la consultation. Comment puis-je l'améliorer ?"));
        }

        // Add new user message to history
        session.History.Add(("user", cmd.UserMessage));
        session.LastActivity = DateTime.UtcNow;

        // ── LLM Agent call ────────────────────────────────────────────────────
        var reply = await llmAgent.ChatAsync(session.History, RefineSystemPrompt, ct);

        if (string.IsNullOrWhiteSpace(reply))
        {
            logger.LogWarning("LLM returned empty reply for session [{S}]", cmd.SessionId);
            reply = "Je n'ai pas pu traiter cette demande. Pouvez-vous reformuler ?";
        }

        // Add assistant reply to history
        session.History.Add(("assistant", reply));
        sessionStore.Set(cmd.SessionId, session);

        // ── Detect and apply section change ──────────────────────────────────
        var sectionName = DetectSection(cmd.UserMessage);
        var updated     = ApplyToSection(cmd.CurrentOutput, sectionName, reply);

        return new RefineConsultationResult(reply, sectionName, updated, cmd.SessionId);
    }

    private static string DetectSection(string msg)
    {
        var lower = msg.ToLower();
        if (lower.Contains("analyse") || lower.Contains("section 4")) return "analyses";
        if (lower.Contains("sommaire") || lower.Contains("résumé"))   return "sommaire";
        if (lower.Contains("étendue") || lower.Contains("etendue"))   return "etendue";
        if (lower.Contains("contexte") || lower.Contains("faits"))    return "contextefaits";
        if (lower.Contains("document") || lower.Contains("référence")) return "documents";
        return "analyses";
    }

    private static ConsultationOutput ApplyToSection(ConsultationOutput current, string section, string content)
    {
        switch (section)
        {
            case "analyses":       current.Analyses        = content; break;
            case "sommaire":       current.SommairExecutif = content; break;
            case "etendue":        current.Etendue         = content; break;
            case "contextefaits":  current.ContexteFaits   = content; break;
            case "documents":      current.Documents       = content; break;
        }
        return current;
    }
}
