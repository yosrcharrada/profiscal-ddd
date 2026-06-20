using FiscalPlatform.Application.Common.DTOs;

namespace FiscalPlatform.Application.Common.Interfaces.Agents;

/// <summary>
/// True ReAct agent that plans and executes legal source retrieval.
/// Unlike the deterministic RetrievalAgent, this agent:
///   - Reasons about which sources are needed for the specific case
///   - Calls targeted fetch tools based on its reasoning
///   - Observes results and decides whether to fetch more
///   - Enforces mandatory analysis sequence for international cases
/// Max 2 ReAct iterations to keep total time under 90 seconds with GPT-4o.
/// </summary>
public interface IRetrievalPlannerAgent
{
    Task<RetrievalPlan> PlanAndRetrieveAsync(
        string             situation,
        string             fiscalQuestion,
        HashSet<string>    branches,
        List<string>       countries,
        bool               isIntl,
        CancellationToken  ct = default);
}

/// <summary>
/// Result of the retrieval planning agent.
/// Contains the targeted sources + metadata used to guide generation.
/// </summary>
public sealed record RetrievalPlan(
    List<LegalSourceDto> Sources,
    string               IncomeType,           // redevance|interets|dividendes|benefices|salaire|unknown
    bool                 EsRiskPossible,        // ES risk detected for this foreign provider
    string?              DetectedCountry,       // country identified by planner (may differ from CountryDetector)
    bool                 NoteCommune2Used,      // Note Commune N°2/2015 was fetched
    int                  IterationsUsed,        // 1 or 2
    string               PlannerReasoning       // for logging and debugging
);
