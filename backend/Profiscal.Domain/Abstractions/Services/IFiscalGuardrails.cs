using Profiscal.Domain.Dtos;

namespace Profiscal.Domain.Abstractions.Services;

/// <summary>
/// Guardrails contract, exposed to the Application layer so ALL entry points (consultation
/// generation, the chatbot, the refinement agent) run behind the same fences.
/// Input side blocks off-topic requests before any LLM spend; output side flags citation and
/// grounding violations (invalid [Sn], uncited rates, hallucinated citation formats).
/// </summary>
public interface IFiscalGuardrails
{
    (bool IsValid, string? Reason) ValidateInput(string situation, string fiscalQuestion);

    /// <summary>Scan a produced text for grounding violations: [Sn] beyond the source list,
    /// percentages with no citation nearby, hallucinated citation formats. Returns warnings.</summary>
    List<string> ValidateTextWarnings(string text, int maxSourceIndex);
}
