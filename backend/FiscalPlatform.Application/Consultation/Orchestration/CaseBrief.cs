using FiscalPlatform.Application.Common.DTOs;

namespace FiscalPlatform.Application.Consultation.Orchestration;

/// <summary>
/// The CASE BRIEF — what a case agent hands the rest of the pipeline. It is the contract between
/// the case agent (domain controller: WHAT this case needs) and the downstream workers (retrieval
/// fulfilment, writer, judge). It carries the required-sources CHECKLIST, the démarche, the
/// forbidden steps, the qualification guidance, the structure skeleton and the judge criteria.
/// Golden métier rule: a brief NEVER contains a numeric rate or a verdict — every figure is read
/// by the model from the retrieved sources.
/// </summary>
public sealed record CaseBrief(
    string   Label,
    string   SystemPrompt,
    bool     UsesOwnPrompt,          // false → legacy proven service prompt (Generic / RS-service)
    string   Demarche,
    string   ForbiddenSteps,
    string   QualificationGuidance,
    string   RedactedSkeleton,
    string   JudgeCriteria,
    string[] Topics,
    IReadOnlyList<RequiredSource> RequiredSources);

/// <summary>
/// One item of the case's required-sources checklist. Deterministically MATCHABLE (predicates) and
/// deterministically FULFILLABLE (fetch hints) — no LLM involved in verifying completeness, which
/// keeps the retrieval loop reproducible and free.
/// All non-null predicates must hold for a source to satisfy the item.
/// </summary>
public sealed record RequiredSource(
    string  Key,
    string  Description,
    bool    Critical,
    // ── match predicates ──
    string?   DocFragment       = null,  // source DocName contains (case-insensitive)
    string?   ArticleNumber     = null,  // digits of ArticleRef equal
    string?   TextContains      = null,  // source text contains (case-insensitive)
    bool      RequirePercent    = false, // source text must contain '%' (a real rate, not a stub)
    string[]? ConventionSubject = null,  // DocType=Convention + head contains ANY variant + country match
    // ── fulfilment hints (how to fetch it when missing) ──
    string?   FetchDocFragment  = null,
    string[]? FetchKeywords     = null)
{
    /// <summary>Deterministic: does this source satisfy the item?</summary>
    public bool IsSatisfiedBy(LegalSourceDto s, ICollection<string> countries)
    {
        var text = s.Text ?? "";
        var name = s.DocName ?? "";

        if (ConventionSubject is { Length: > 0 })
        {
            if (!string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase)) return false;
            var head = text[..Math.Min(text.Length, 80)];
            if (!ConventionSubject.Any(v => head.Contains(v, StringComparison.OrdinalIgnoreCase))) return false;
            if (countries.Count > 0 &&
                !countries.Any(c => name.Contains(c, StringComparison.OrdinalIgnoreCase))) return false;
        }

        if (DocFragment is not null &&
            !name.Contains(DocFragment, StringComparison.OrdinalIgnoreCase)) return false;

        if (ArticleNumber is not null &&
            Digits(s.ArticleRef) != ArticleNumber) return false;

        if (TextContains is not null &&
            !text.Contains(TextContains, StringComparison.OrdinalIgnoreCase)) return false;

        if (RequirePercent && !text.Contains('%')) return false;

        return true;
    }

    private static string Digits(string? s) =>
        string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());
}

/// <summary>Result of the deterministic completeness check.</summary>
public sealed record CompletenessReport(
    IReadOnlyList<RequiredSource> Missing,
    IReadOnlyList<RequiredSource> MissingCritical,
    int TotalRequired)
{
    public bool CriticallyComplete => MissingCritical.Count == 0;
}
