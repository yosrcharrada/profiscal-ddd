using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Retrieval;

/// <summary>
/// Rule-based retrieval policy — the "fiscal routing map".
///
/// This is NOT hardcoding answers. It is a maintained, declarative table that maps a case
/// classification to the legal sources that MUST be retrieved for that class, then fetches
/// them precisely (by article ref / doc family) via the retrieval tool layer. The actual
/// rate/verdict is still produced by the LLM from the retrieved, cited text.
///
/// The rule list is the single place the métier team maintains as the law evolves.
/// </summary>
public sealed class FiscalRetrievalPolicy : IRuleBasedRetrieval
{
    private readonly IRetrievalAgent _retrieval;
    private readonly ILogger<FiscalRetrievalPolicy> _logger;

    public FiscalRetrievalPolicy(IRetrievalAgent retrieval, ILogger<FiscalRetrievalPolicy> logger)
    {
        _retrieval = retrieval;
        _logger    = logger;
    }

    private sealed record Rule(
        string Name,
        Func<RuleContext, bool> When,
        string DocFragment,
        string[] ArticleRefs,
        string[] Keywords);

    // ── The routing map (maintained policy — edit here as the law changes) ──────────
    private static readonly Rule[] Rules =
    {
        // Domestic withholding tax — the rate table lives in Art. 52/53 CIRPPIS.
        new Rule(
            "RAS domestique — taux (Art. 52/53 CIRPPIS)",
            ctx => ctx.Branches.Contains("Retenue") || Mentions(ctx, "retenue", "ras", "taux"),
            "code_irpp_is", new[] { "Art. 52", "Art. 53" },
            new[] { "retenue à la source", "honoraires", "1%", "1,5%", "3%", "10%", "15%" }),

        // Honoraires vs commercial services — defined in Note Commune N°3/2015 (Annexe 2).
        new Rule(
            "Honoraires — Note Commune N°3/2015",
            ctx => Mentions(ctx, "honoraires", "assistance", "conseil", "prestation", "retenue"),
            "NC_2015_03", Array.Empty<string>(),
            new[] { "honoraires", "professions", "retenue à la source", "assistance" }),

        // IS — base & rate.
        new Rule(
            "IS — base imposable & taux (CIRPPIS)",
            ctx => ctx.Branches.Contains("IS"),
            "code_irpp_is", new[] { "Art. 45", "Art. 47", "Art. 49" },
            new[] { "taux de l'impôt", "personnes morales", "bénéfices" }),

        // TVA — scope & rate.
        new Rule(
            "TVA — champ & taux (CTVA)",
            ctx => ctx.Branches.Contains("TVA") || Mentions(ctx, "tva"),
            "code_tva", new[] { "Art. 6", "Art. 7" },
            new[] { "taux", "assujetti", "soumises", "affaires" }),

        // Transfer pricing.
        new Rule(
            "Prix de transfert (Art. 48 septies CIRPPIS + CDPF)",
            ctx => ctx.Branches.Contains("PrixTransfert") || Mentions(ctx, "prix de transfert", "pleine concurrence", "marge"),
            "code_irpp_is", new[] { "Art. 48 septies" },
            new[] { "pleine concurrence", "entreprises associées", "prix de transfert" }),
    };

    public async Task<List<LegalSourceDto>> RetrieveAsync(RuleContext ctx, CancellationToken ct = default)
    {
        var matched = Rules.Where(r => SafeWhen(r, ctx)).ToList();
        if (matched.Count == 0)
        {
            _logger.LogInformation("[RULES] no rule matched");
            return new();
        }

        // Fetch every matched rule's targeted sources in parallel (cheap Neo4j calls).
        var fetched = await Task.WhenAll(matched.Select(r =>
            _retrieval.FetchTargetedAsync(r.DocFragment, r.ArticleRefs, r.Keywords, ct)));

        // Merge + dedupe; tag with a high score so the merge keeps them.
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<LegalSourceDto>();
        foreach (var list in fetched)
            foreach (var s in list)
            {
                var key = !string.IsNullOrEmpty(s.ChunkId)
                    ? s.ChunkId
                    : s.DocName + "|" + (s.Text ?? "")[..Math.Min((s.Text ?? "").Length, 60)];
                if (seen.Add(key)) merged.Add(s);
            }

        _logger.LogInformation("[RULES] {M} rule(s) matched [{Names}] → {N} sources",
            matched.Count, string.Join(", ", matched.Select(r => r.Name)), merged.Count);
        return merged;
    }

    private bool SafeWhen(Rule r, RuleContext ctx)
    {
        try { return r.When(ctx); }
        catch (Exception ex) { _logger.LogDebug(ex, "[RULES] predicate failed: {R}", r.Name); return false; }
    }

    private static bool Mentions(RuleContext ctx, params string[] terms)
    {
        var hay = ((ctx.Question ?? "") + " " + (ctx.Situation ?? "") + " " +
                   string.Join(" ", ctx.ExtraTopics ?? Array.Empty<string>())).ToLowerInvariant();
        return terms.Any(t => hay.Contains(t));
    }
}
