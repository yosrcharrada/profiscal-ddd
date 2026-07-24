using Profiscal.Domain.Dtos;
using Profiscal.Application.Consultation.Orchestration;
using Profiscal.Application.Consultation.Playbooks;

namespace Profiscal.Application.Consultation.Agents;

/// <summary>
/// A case agent is the DOMAIN CONTROLLER for one income qualification (RS-service, dividende,
/// intérêt, redevance, generic). In the workflow it owns node [2]: it BUILDS THE CASE BRIEF — the
/// required-sources checklist, the démarche, the forbidden steps, the judge criteria — and it
/// VERIFIES COMPLETENESS of what retrieval brought back (deterministically, no LLM). It commands
/// the retrieval fulfilment through its checklist; the ConsultationWriter drafts from its brief;
/// the judge scores against its criteria. One class per big case, each owning its content as
/// readable code.
/// </summary>
public interface ICaseAgent
{
    CaseType Type { get; }

    /// <summary>Build this case's brief from the consultation's understood state. Context-aware —
    /// e.g. the dividende agent only demands the régularisation sources when the facts say the
    /// distribution was paid without withholding.</summary>
    CaseBrief BuildBrief(ConsultationState state);

    /// <summary>Deterministic completeness check: which checklist items are still unmatched by the
    /// retrieved sources?</summary>
    CompletenessReport VerifyCompleteness(
        CaseBrief brief, List<LegalSourceDto> sources, ICollection<string> countries);
}
