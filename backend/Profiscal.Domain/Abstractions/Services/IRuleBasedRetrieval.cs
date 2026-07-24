using Profiscal.Domain.Dtos;

namespace Profiscal.Domain.Abstractions.Services;

/// <summary>Inputs the routing policy reasons over (case classification).</summary>
public sealed record RuleContext(
    HashSet<string> Branches,
    bool            IsInternational,
    List<string>    Countries,
    string          Question,
    string          Situation,
    IReadOnlyCollection<string>? ExtraTopics = null);

/// <summary>
/// Deterministic, config-driven retrieval policy. It decides WHICH legal sources must be on
/// the table for a given case class (e.g. a domestic service-fee question must include
/// Art. 52 CIRPPIS + Note Commune N°3/2015) — it never decides the answer. The rate is still
/// read from the retrieved text and cited [Sn]. This raises retrieval precision so the LLM
/// can actually find the rate instead of replying "NON DOCUMENTÉ".
/// </summary>
public interface IRuleBasedRetrieval
{
    Task<List<LegalSourceDto>> RetrieveAsync(RuleContext ctx, CancellationToken ct = default);
}
