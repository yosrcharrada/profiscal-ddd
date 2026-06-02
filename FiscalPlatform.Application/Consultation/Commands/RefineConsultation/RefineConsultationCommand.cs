using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace FiscalPlatform.Application.Consultation.Commands.RefineConsultation;

// ── COMMAND ───────────────────────────────────────────────────────────────────
public sealed record RefineConsultationCommand(
    string               SessionId,
    string               UserMessage,
    ConsultationOutput   CurrentOutput,
    List<LegalSourceDto> Sources
) : IRequest<RefineConsultationResult>;

public sealed record RefineConsultationResult(
    string             AssistantReply,
    string             SectionName,
    ConsultationOutput UpdatedOutput,
    string             SessionId);

// ── VALIDATOR ────────────────────────────────────────────────────────────────
public sealed class RefineConsultationCommandValidator
    : AbstractValidator<RefineConsultationCommand>
{
    public RefineConsultationCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().WithMessage("SessionId requis.");
        RuleFor(x => x.UserMessage).NotEmpty().WithMessage("Message requis.");
    }
}

// ── HANDLER — TRUE SK ChatCompletionAgent ────────────────────────────────────
/// <summary>
/// True Semantic Kernel agent for consultation refinement.
///
/// Architecture:
///   - Uses SK ChatCompletionAgent with access to RetrievalPlugin + AnalysisPlugin
///   - Agent has BRAIN: reasons about the user's instruction
///   - Agent has MEMORY: reads full conversation history from session store
///   - Agent has ACTIONS: can call RetrievalPlugin to fetch more sources,
///     or LegalAnalysisPlugin to rewrite a section
///   - Agent DECIDES autonomously which tools to invoke
///
/// State Machine:
///   Generated → UnderReview → Refined → Approved
/// </summary>
public sealed class RefineConsultationCommandHandler
    : IRequestHandler<RefineConsultationCommand, RefineConsultationResult>
{
    private readonly Microsoft.SemanticKernel.Kernel _kernel;
    private readonly ISessionStore                   _sessionStore;
    private readonly ILogger<RefineConsultationCommandHandler> _logger;

    // The agent's system prompt — defines its identity, knowledge, and constraints
    private const string AgentInstructions =
        "Tu es Faiez Choyakh, fiscaliste senior EY Tunisia et auteur des commentaires annuels des lois de finances.\n" +
        "Tu aides l'équipe fiscale à affiner les consultations générées.\n\n" +
        "TES CAPACITÉS (outils disponibles):\n" +
        "  - Retrieval.semantic_search     → chercher des sources juridiques supplémentaires\n" +
        "  - Retrieval.search_convention   → chercher dans une convention de non-double imposition\n" +
        "  - Retrieval.keyword_search      → chercher des articles spécifiques par mots-clés\n" +
        "  - Analysis.refine_section       → réécrire une section selon les instructions\n" +
        "  - Analysis.analyze_fiscal_point → analyser un point fiscal précis\n" +
        "  - Analysis.generate_sommaire    → régénérer le sommaire exécutif\n\n" +
        "RÈGLES:\n" +
        "  1. Si l'utilisateur demande une correction → appelle refine_section\n" +
        "  2. Si l'utilisateur dit qu'une source manque → appelle semantic_search ou keyword_search d'abord\n" +
        "  3. Si l'utilisateur demande plus d'analyse sur un point → appelle analyze_fiscal_point\n" +
        "  4. Citations uniquement via [S1],[S2]... — jamais de nom de document en clair\n" +
        "  5. Verdicts clairs: OUI/NON/X%/EXONÉRÉ/SOUMIS\n" +
        "  6. Style professionnel EY — formel, précis, fondé sur les sources";

    public RefineConsultationCommandHandler(
        Microsoft.SemanticKernel.Kernel kernel,
        ISessionStore                   sessionStore,
        ILogger<RefineConsultationCommandHandler> logger)
    {
        _kernel       = kernel;
        _sessionStore = sessionStore;
        _logger       = logger;
    }

    public async Task<RefineConsultationResult> Handle(
        RefineConsultationCommand cmd, CancellationToken ct)
    {
        // ── State Machine: Load session ───────────────────────────────────────
        var session = _sessionStore.Get(cmd.SessionId);
        if (session is null)
        {
            _logger.LogWarning("Session [{S}] not found — creating new", cmd.SessionId);
            session = new ConversationSession
            {
                SessionId      = cmd.SessionId,
                ConsultationId = Guid.Empty,
                ClientName     = "",
                Reference      = "",
            };
            _sessionStore.Set(cmd.SessionId, session);
        }

        _logger.LogInformation("SK Agent refinement [{S}]: '{M}'",
            cmd.SessionId, cmd.UserMessage[..Math.Min(cmd.UserMessage.Length, 60)]);

        // ── Build SK ChatHistory from session memory ───────────────────────────
        var chatHistory = new ChatHistory();

        // Inject current consultation context as first message
        if (session.History.Count == 0)
        {
            var context =
                $"Consultation actuelle à affiner:\n\n" +
                $"SOMMAIRE:\n{cmd.CurrentOutput.SommairExecutif[..Math.Min(cmd.CurrentOutput.SommairExecutif.Length, 500)]}\n\n" +
                $"ANALYSES (extrait):\n{cmd.CurrentOutput.Analyses[..Math.Min(cmd.CurrentOutput.Analyses.Length, 1000)]}\n\n" +
                $"SOURCES DISPONIBLES:\n" +
                string.Join("\n", cmd.Sources.Take(10)
                    .Select(s => $"[S{s.Index}] {s.DocType} {s.DocName} ({s.Year}) — {s.ArticleRef}"));

            chatHistory.AddUserMessage(context);
            chatHistory.AddAssistantMessage(
                "J'ai bien pris connaissance de la consultation. " +
                "Je suis prêt à l'améliorer selon vos instructions. " +
                "Je peux rechercher des sources supplémentaires, réécrire des sections, " +
                "ou analyser des points spécifiques. Que souhaitez-vous modifier?");
        }

        // Replay conversation history into SK ChatHistory
        foreach (var (role, content) in session.History)
        {
            if (role == "user")      chatHistory.AddUserMessage(content);
            else if (role == "assistant") chatHistory.AddAssistantMessage(content);
        }

        // Add the new user message
        chatHistory.AddUserMessage(cmd.UserMessage);

        // ── Create the true SK ChatCompletionAgent ────────────────────────────
        // This agent has BRAIN (reasons about what to do),
        // MEMORY (chatHistory with full context),
        // ACTIONS (RetrievalPlugin + LegalAnalysisPlugin tools)
        var agent = new ChatCompletionAgent
        {
            Name         = "FiscalAdvisor",
            Instructions = AgentInstructions,
            Kernel       = _kernel,
        };

        // ── Agent invoke via AgentThread (SK 1.45+ API) ───────────────────────
        string reply = "";
        try
        {
            var thread = new ChatHistoryAgentThread(chatHistory);
            await foreach (var response in agent.InvokeAsync(thread, cancellationToken: ct))
            {
                reply += response.Message?.Content ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SK Agent failed for session [{S}]", cmd.SessionId);
            reply = "Je n'ai pas pu traiter cette demande. Pouvez-vous reformuler?";
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "Je n'ai pas pu traiter cette demande. Pouvez-vous reformuler?";

        // ── Update session memory ─────────────────────────────────────────────
        session.History.Add(("user",      cmd.UserMessage));
        session.History.Add(("assistant", reply));
        session.LastActivity = DateTime.UtcNow;
        _sessionStore.Set(cmd.SessionId, session);

        // ── Detect which section was modified and apply update ────────────────
        var sectionName = DetectSection(cmd.UserMessage, reply);
        var updated     = ApplyToSection(cmd.CurrentOutput, sectionName, reply);

        _logger.LogInformation("SK Agent: section '{S}' updated for [{Id}]",
            sectionName, cmd.SessionId);

        return new RefineConsultationResult(reply, sectionName, updated, cmd.SessionId);
    }

    private static string DetectSection(string userMsg, string agentReply)
    {
        var lower = (userMsg + " " + agentReply).ToLower();
        if (lower.Contains("analyse") || lower.Contains("section 4") || lower.Contains("développement")) return "analyses";
        if (lower.Contains("sommaire") || lower.Contains("résumé") || lower.Contains("exécutif"))        return "sommaire";
        if (lower.Contains("étendue") || lower.Contains("etendue") || lower.Contains("travaux"))         return "etendue";
        if (lower.Contains("contexte") || lower.Contains("faits") || lower.Contains("comprenons"))       return "contextefaits";
        if (lower.Contains("document") || lower.Contains("référence") || lower.Contains("source"))       return "documents";
        return "analyses";
    }

    private static ConsultationOutput ApplyToSection(
        ConsultationOutput current, string section, string content)
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

// ── SESSION COMMANDS (unchanged) ──────────────────────────────────────────────
public sealed record StartConsultationSessionCommand(
    Guid ConsultationId, string ClientName, string Reference) : IRequest<string>;

public sealed class StartConsultationSessionCommandHandler(
    ISessionStore sessionStore)
    : IRequestHandler<StartConsultationSessionCommand, string>
{
    public Task<string> Handle(StartConsultationSessionCommand cmd, CancellationToken ct)
    {
        var sessionId = $"sess_{Guid.NewGuid():N}";
        sessionStore.Set(sessionId, new ConversationSession
        {
            SessionId      = sessionId,
            ConsultationId = cmd.ConsultationId,
            ClientName     = cmd.ClientName,
            Reference      = cmd.Reference,
        });
        return Task.FromResult(sessionId);
    }
}

public sealed record EndConsultationSessionCommand(string SessionId) : IRequest;

public sealed class EndConsultationSessionCommandHandler(ISessionStore sessionStore)
    : IRequestHandler<EndConsultationSessionCommand>
{
    public Task Handle(EndConsultationSessionCommand cmd, CancellationToken ct)
    {
        sessionStore.Remove(cmd.SessionId);
        return Task.CompletedTask;
    }
}
