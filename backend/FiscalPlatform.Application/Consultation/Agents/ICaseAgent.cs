using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;
using FiscalPlatform.Application.Consultation.Playbooks;

namespace FiscalPlatform.Application.Consultation.Agents;

/// <summary>
/// A case agent handles ONE income qualification end-to-end (an RS-service agent, a dividende
/// agent, an intérêt agent, a redevance agent, a generic fallback). Each agent owns its own
/// source acquisition, its legal démarche/analysis, and its self-check criteria. The handler
/// qualifies the consultation, then dispatches to the matching agent — no giant if/else, and each
/// case is independently testable and independently upgradeable (a structured agent today can
/// become a tool-using ReAct agent tomorrow without touching the others).
/// </summary>
public interface ICaseAgent
{
    CaseType Type { get; }

    /// <summary>Acquire any case-specific sources, then produce the analyses (Phase-2).</summary>
    Task<CaseDraft> DraftAsync(CaseContext ctx, CancellationToken ct = default);

    /// <summary>One bounded revision pass driven by the judge's corrective guidance.</summary>
    Task<string> ReviseAsync(CaseContext ctx, CaseDraft draft, string guidance, CancellationToken ct = default);
}

/// <summary>Everything an agent needs about the consultation after Phase 1.</summary>
public sealed record CaseContext(
    GenerateConsultationCommand Command,
    List<string>          EtendueItems,
    string                ContexteFaits,
    string                Sommaire,
    List<LegalSourceDto>  Sources,
    List<string>          Countries,
    bool                  IsInternational,
    HashSet<string>       Branches,
    RetrievalPlan         Plan);

/// <summary>An agent's draft: the analyses text plus the (possibly augmented) source list and the
/// system prompt + judge criteria the shared acceptance/senior-review steps must reuse.</summary>
public sealed record CaseDraft(
    string               Analyses,
    List<LegalSourceDto> Sources,
    string               SystemPrompt,
    string               JudgeCriteria);
