namespace FiscalPlatform.Application.Common.Interfaces.Agents;

/// <summary>What the acceptance agent reviews.</summary>
public sealed record AcceptanceRequest(
    string FiscalQuestion,
    string Etendue,
    string Analyses,
    string SourcesList);

/// <summary>The acceptance agent's verdict on a generated draft.</summary>
public sealed record AcceptanceVerdict(
    bool         Accept,
    double       Score,            // 0..1 quality estimate
    List<string> Issues,           // human-readable problems
    bool         NeedsMoreSources, // a rate/article is asked but missing from the sources
    List<string> MissingTopics);   // e.g. ["taux retenue à la source", "Art. 52 CIRPPIS"]

/// <summary>
/// Acceptance agent — an LLM-as-judge that validates a generated consultation against the
/// question and the retrieved sources (faithfulness, completeness, groundedness). It decides
/// ACCEPT or REVISE and, when revising, names the missing topics so the orchestrator can run
/// a targeted re-retrieval. Deterministic guardrails run first; this is the reasoning critic.
/// </summary>
public interface IAcceptanceAgent
{
    Task<AcceptanceVerdict> ReviewAsync(AcceptanceRequest req, CancellationToken ct = default);
}
