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
        // Art. 52/53 CIRPPIS — retenue à la source sur paiements à des non-résidents.
        // Fetches the non-resident withholding regime (droit commun, sans CNDI).
        new Rule(
            "RAS non-résidents — Art. 52/53 CIRPPIS (droit commun)",
            ctx => ctx.Branches.Contains("Retenue") || ctx.IsInternational ||
                   Mentions(ctx, "retenue", "ras", "non résident", "non-résident", "étranger"),
            "irpp", new[] { "Art. 52", "Art. 53" },
            new[] { "non domicilié", "non établi", "retenue libératoire", "personnes non résidentes" }),

        // NC 3/2015 — définit la distinction honoraires / non-honoraires et l'assiette RS (TTC).
        // Used for: (a) assiette calcul RS (TTC rule), (b) qualification honoraires vs services.
        // NOT for applying rates to non-residents — that belongs to Art. 52 CIRPPIS.
        new Rule(
            "NC 3/2015 — qualification honoraires & assiette RS",
            ctx => Mentions(ctx, "honoraires", "assistance", "conseil", "prestation", "retenue", "assiette"),
            "note-commune-numero-3", Array.Empty<string>(),
            new[] { "honoraires", "professions", "retenue à la source", "assiette", "toutes taxes" }),

        // IS — base imposable & taux (personnes morales résidentes ou ES).
        new Rule(
            "IS — base imposable & taux (CIRPPIS)",
            ctx => ctx.Branches.Contains("IS"),
            "irpp", new[] { "Art. 45", "Art. 47", "Art. 49" },
            new[] { "taux de l'impôt", "personnes morales", "bénéfices" }),

        // TVA — champ d'application (Art. 1 + Art. 3 CTVA: affaires faites en Tunisie).
        new Rule(
            "TVA — territorialité & champ (CTVA Art. 1/3/6/7)",
            ctx => ctx.Branches.Contains("TVA") || Mentions(ctx, "tva"),
            "taxe-sur-la-valeur", new[] { "Art. 1", "Art. 3", "Art. 6", "Art. 7" },
            new[] { "affaires faites en Tunisie", "utilisés", "exploités", "soumises", "assujetti" }),

        // Transfer pricing.
        new Rule(
            "Prix de transfert (Art. 48 septies CIRPPIS + CDPF)",
            ctx => ctx.Branches.Contains("PrixTransfert") || Mentions(ctx, "prix de transfert", "pleine concurrence", "marge"),
            "irpp", new[] { "Art. 48 septies" },
            new[] { "pleine concurrence", "entreprises associées", "prix de transfert" }),

        // CDPF Art. 112 + BCT circulaire 9/2016 — formalisme transfert de fonds à l'étranger.
        // Mandatory whenever a cross-border payment with RS libératoire is identified.
        new Rule(
            "Formalisme transfert fonds à l'étranger (CDPF Art. 112)",
            ctx => ctx.IsInternational || Mentions(ctx, "transfert", "virement", "non résident", "non-résident"),
            "cdpf", new[] { "Art. 112" },
            new[] { "transfert", "attestation", "situation fiscale", "banque centrale", "retenue libératoire" }),
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
