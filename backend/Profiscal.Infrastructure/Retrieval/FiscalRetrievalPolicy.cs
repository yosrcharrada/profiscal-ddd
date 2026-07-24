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

    // Number-free routing: a rule points at a document family and identifies the provision by
    // distinctive ANCHOR PHRASES (text that only the right article contains), taxmind TOPIC nodes,
    // and broad KEYWORDS — never by article number. The rate/verdict still comes from the LLM
    // reading the retrieved, cited text.
    private sealed record Rule(
        string Name,
        Func<RuleContext, bool> When,
        string DocFragment,
        string[] AnchorPhrases,
        string[] Topics,
        string[] Keywords,
        // Optional precision POINTERS (not answers) used only where BM25/phrase matching is
        // ambiguous in this graph — e.g. the IS-rate article, which the consolidated code's
        // intro chunks outrank. Left null for rules that resolve cleanly number-free.
        string[]? ArticleHints = null);

    // ── The routing map — mirrors the EY tax team's official source list for RS consultations ──────
    //   Droit commun (RS):  CIRPPIS Art. 45/46/49/52 + NC 3/2015 · TVA Art. 1/3/5/7 + tableaux A&B
    //   Fournisseur étranger: ES → CIRPPIS Art. 45/47 + NC 2/2015 + convention · RS → Art. 52 + convention
    //                          TVA → Art. 1/3/5/19/7 + tableaux · Transfert → CDPF Art. 112 + circ. BCT 9/2016
    private static readonly Rule[] Rules =
    {
        // RS — droit commun: rate table + base articles (CIRPPIS Art. 45/46/49/52).
        new Rule(
            "RAS — droit commun (CIRPPIS Art. 45/46/49/52)",
            ctx => ctx.Branches.Contains("Retenue") || Mentions(ctx, "retenue", "ras", "taux"),
            "code_irpp_is",
            AnchorPhrases: new[] { "retenue à la source aux taux suivants", "font l'objet d'une retenue à la source" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "retenue à la source", "honoraires", "régime fiscal privilégié", "25%", "15%" },
            ArticleHints: new[] { "45", "46", "49", "52", "53" }),

        // Honoraires / assiette de la RS — Note Commune N°3/2015 (base brute TVA comprise).
        new Rule(
            "Honoraires & assiette RS — Note Commune N°3/2015",
            ctx => Mentions(ctx, "honoraires", "assistance", "conseil", "prestation", "retenue") || ctx.Branches.Contains("Retenue"),
            "NC_2015_03",
            AnchorPhrases: Array.Empty<string>(),
            Topics: Array.Empty<string>(),
            Keywords: new[] { "honoraires", "assiette", "montant brut", "retenue à la source", "assistance" }),

        // IS — base & rate.
        new Rule(
            "IS — base imposable & taux (CIRPPIS)",
            ctx => ctx.Branches.Contains("IS"),
            "code_irpp_is",
            AnchorPhrases: new[] { "taux de l'impôt sur les sociétés", "bénéfice imposable" },
            Topics: new[] { "Bénéfices des entreprises" },
            Keywords: new[] { "personnes morales", "taux", "bénéfices" },
            ArticleHints: new[] { "45", "47", "49" }),

        // TVA — champ (Art.1), territorialité (Art.3), Art.5, Art.19 (services importés), taux (Art.7 + tableaux A&B).
        new Rule(
            "TVA — champ/territorialité/taux (CTVA Art. 1/3/5/19/7 + tableaux A&B)",
            ctx => ctx.Branches.Contains("TVA") || Mentions(ctx, "tva"),
            "code_tva",
            AnchorPhrases: new[] { "soumis à la taxe sur la valeur ajoutée au taux de", "tableaux annexes", "affaire est réputée faite en Tunisie" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "taux", "13%", "7%", "tableau", "assujetti", "soumises" },
            ArticleHints: new[] { "1", "3", "5", "7", "19" }),

        // Établissement stable — foreign provider (CIRPPIS Art. 45/47 + NC 2/2015 + convention if any).
        new Rule(
            "Établissement stable — prestataire étranger (CIRPPIS Art. 45/47)",
            ctx => ctx.IsInternational || Mentions(ctx, "établissement stable", "non résident", "non-résident", "prestataire étranger"),
            "code_irpp_is",
            AnchorPhrases: new[] { "établissement stable", "établissements situés en Tunisie" },
            Topics: new[] { "Établissement stable" },
            Keywords: new[] { "établissement stable", "non résident", "bénéfices" },
            ArticleHints: new[] { "45", "47" }),

        // Transfert de fonds à l'étranger — CDPF Art. 112 (attestation de situation fiscale).
        new Rule(
            "Transfert de fonds — CDPF Art. 112",
            ctx => ctx.IsInternational || Mentions(ctx, "transfert", "attestation", "régularisation"),
            "code_droits_procedures",
            AnchorPhrases: new[] { "attestation de situation fiscale", "transfert desdits revenus" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "transfert", "attestation", "non-résidents" },
            ArticleHints: new[] { "112" }),

        // Circulaire BCT n°9/2016 — change control on cross-border transfers.
        new Rule(
            "Circulaire BCT n°9/2016 — transfert à l'étranger",
            ctx => ctx.IsInternational || Mentions(ctx, "transfert", "banque centrale", "change"),
            "",
            AnchorPhrases: new[] { "circulaire", "banque centrale de tunisie" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "circulaire", "9/2016", "banque centrale", "transfert" }),

        // Prix de transfert.
        new Rule(
            "Prix de transfert (CIRPPIS + CDPF)",
            ctx => ctx.Branches.Contains("PrixTransfert") || Mentions(ctx, "prix de transfert", "pleine concurrence", "marge"),
            "code_irpp_is",
            AnchorPhrases: new[] { "pleine concurrence", "entreprises associées" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "prix de transfert", "bénéfices indirectement transférés" }),

        // Régime fiscal privilégié — list of States/territories (NC 16/2019 + the consolidated code's
        // arrêté table) to decide whether the 25% RS majoration applies.
        new Rule(
            "Régime fiscal privilégié — liste des États (majoration RS 25%)",
            ctx => ctx.IsInternational,
            "",
            AnchorPhrases: new[] { "états et territoires dont le taux", "régime fiscal privilégié" },
            Topics: Array.Empty<string>(),
            Keywords: new[] { "26 septembre 2022", "régime fiscal privilégié" }),
    };

    // Treaty article NUMBERING differs per convention (in conv_france: ES=Art.4, bénéfices=Art.11,
    // redevances=Art.19). So we fetch by SUBJECT (the article title), which is convention-agnostic.
    private static readonly string[] TreatySubjects =
        { "tablissement stable", "bénéfices des entreprises", "redevances", "professions indépendantes", "dividendes", "intérêts" };

    public async Task<List<LegalSourceDto>> RetrieveAsync(RuleContext ctx, CancellationToken ct = default)
    {
        var matched = Rules.Where(r => SafeWhen(r, ctx)).ToList();
        var tasks   = matched.Select(r =>
            _retrieval.FetchBySubjectAsync(r.DocFragment, r.AnchorPhrases, r.Topics, r.Keywords, ct)).ToList();

        // Add a precise pointer fetch only for rules that carry article hints (BM25-ambiguous cases).
        foreach (var r in matched.Where(r => r.ArticleHints is { Length: > 0 }))
            tasks.Add(_retrieval.FetchTargetedAsync(r.DocFragment, r.ArticleHints!, r.Keywords, ct));

        // ── International: deterministically fetch the convention's rate-driving articles
        //    (Art. 5/7/12/14) for each detected country, plus Note Commune N°2/2015 which the
        //    métier uses to read the conventions. This is what makes the rate references show up. ──
        var convNames = new List<string>();
        if (ctx.IsInternational && ctx.Countries is { Count: > 0 })
        {
            foreach (var country in ctx.Countries.Take(3))
            {
                tasks.Add(_retrieval.FetchConventionArticleAsync(country, TreatySubjects, ct));
                convNames.Add("Convention " + country);
            }
            tasks.Add(_retrieval.FetchNoteCommune2Async(ctx.Countries.FirstOrDefault(), ct));
            convNames.Add("Note Commune N°2/2015");
        }

        if (tasks.Count == 0)
        {
            _logger.LogInformation("[RULES] no rule matched");
            return new();
        }

        var fetched = await Task.WhenAll(tasks);

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

        var allNames = matched.Select(r => r.Name).Concat(convNames);
        _logger.LogInformation("[RULES] {M} source-set(s) [{Names}] → {N} sources",
            tasks.Count, string.Join(", ", allNames), merged.Count);
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
