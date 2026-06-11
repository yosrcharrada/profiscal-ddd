using System.ComponentModel;
using System.Text;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure.Kernel.Plugins;

/// <summary>
/// SK Plugin with tools available ONLY to the RetrievalPlannerAgent.
/// These are targeted legal source fetch functions — not broad semantic search.
/// The planner agent calls these based on its reasoning about the case.
///
/// Each function calls into RetrievalAgent's targeted methods.
/// Results are accumulated in a shared list that the planner reads.
/// </summary>
public sealed class RetrievalPlannerPlugin
{
    private readonly IRetrievalAgent              _retrieval;
    private readonly IEmbedSearchAgent            _embed;
    private readonly List<LegalSourceDto>         _accumulated;
    private readonly HashSet<string>              _seen;
    private readonly ILogger<RetrievalPlannerPlugin> _logger;
    private          bool                         _finished;

    // Country detected by the planner during reasoning
    public string?  DetectedCountry    { get; private set; }
    public string   IncomeType         { get; private set; } = "unknown";
    public bool     EsRiskPossible     { get; private set; }
    public bool     NoteCommune2Used   { get; private set; }
    public bool     IsFinished         => _finished;
    public string   PlannerReasoning   { get; private set; } = "";

    public RetrievalPlannerPlugin(
        IRetrievalAgent   retrieval,
        IEmbedSearchAgent embed,
        ILogger<RetrievalPlannerPlugin> logger)
    {
        _retrieval   = retrieval;
        _embed       = embed;
        _accumulated = new List<LegalSourceDto>();
        _seen        = new HashSet<string>();
        _logger      = logger;
    }

    public List<LegalSourceDto> GetAccumulatedSources() => _accumulated;

    // ── Tool 1: Fetch convention article by type ──────────────────────────────

    [KernelFunction("fetch_convention_article")]
    [Description(
        "Fetch a specific article from the double-taxation convention with the given country. " +
        "article_type must be one of: 'etablissement_stable' (Art.5), 'redevances' (Art.12), " +
        "'interets' (Art.11), 'dividendes' (Art.10), 'benefices_entreprises' (Art.7), " +
        "'professions_independantes' (Art.14), 'professions_dependantes' (Art.15).")]
    public async Task<string> FetchConventionArticle(
        [Description("Country name in French lowercase (ex: france, maroc, allemagne)")] string country,
        [Description("Type of article to fetch")] string articleType)
    {
        DetectedCountry = country.ToLower().Trim();
        _logger.LogInformation("[Planner] Fetching convention article: {C}/{T}", country, articleType);

        var keywords = articleType switch
        {
            "etablissement_stable"    => new[] { "établissement stable", "chantier", "durée", "installation fixe" },
            "redevances"              => new[] { "redevances", "rémunérations", "usage", "droit", "information", "brevet" },
            "interets"                => new[] { "intérêts", "créance", "prêt", "emprunt" },
            "dividendes"              => new[] { "dividendes", "distribution", "participation" },
            "benefices_entreprises"   => new[] { "bénéfices", "entreprise", "résultat" },
            "professions_independantes" => new[] { "professions indépendantes", "activités indépendantes" },
            "professions_dependantes" => new[] { "professions dépendantes", "salaires", "rémunérations" },
            _                         => new[] { articleType }
        };

        if (articleType == "etablissement_stable") EsRiskPossible = true;

        var sources = await _retrieval.FetchConventionArticleAsync(
            country, keywords, ct: default);

        AddSources(sources, $"Convention {country}/{articleType}");
        return FormatResult(sources, $"Convention {country} — {articleType}");
    }

    // ── Tool 2: Fetch Note Commune N°2/2015 ───────────────────────────────────

    [KernelFunction("fetch_note_commune_2")]
    [Description(
        "Fetch Note Commune N°2/2015 which explains how to apply double-taxation conventions " +
        "for Tunisian-source income. Contains Annexe 1 with per-country tax rate tables. " +
        "MANDATORY for all international cases EXCEPT Allemagne (Germany convention supersedes it). " +
        "Always call this when the case involves a non-resident party from any country except Allemagne.")]
    public async Task<string> FetchNoteCommune2(
        [Description("Country name to find in Annexe 1 tables (ex: france, maroc). Empty string for general principles only.")] string country)
    {
        _logger.LogInformation("[Planner] Fetching Note Commune N°2/2015 for country: {C}", country);
        NoteCommune2Used = true;

        var sources = await _retrieval.FetchNoteCommune2Async(
            string.IsNullOrEmpty(country) ? null : country.ToLower().Trim(),
            ct: default);

        AddSources(sources, "NoteCommune2/2015");
        return FormatResult(sources, "Note Commune N°2/2015");
    }

    // ── Tool 3: Fetch domestic retenue à la source rules ──────────────────────

    [KernelFunction("fetch_domestic_retenue")]
    [Description(
        "Fetch CIRPPIS articles about retenue à la source (withholding tax). " +
        "Includes Art.52 (general retenue rates), domestic rates for non-residents. " +
        "Always call this as fallback when convention rate cannot be determined, " +
        "or when ES risk is confirmed (because IS rates apply instead of retenue).")]
    public async Task<string> FetchDomesticRetenue(
        [Description("Additional keywords to refine search (ex: 'non-résident', 'taux', 'services')")] string additionalKeywords = "")
    {
        _logger.LogInformation("[Planner] Fetching domestic retenue rules");

        var kws = new List<string> { "retenue", "source", "non-résident", "taux", "art. 52", "versés" };
        if (!string.IsNullOrEmpty(additionalKeywords))
            kws.AddRange(additionalKeywords.Split(',').Select(k => k.Trim()));

        var sources = await _retrieval.FetchDomesticRetenueAsync(kws, ct: default);
        AddSources(sources, "CIRPPIS/Retenue");
        return FormatResult(sources, "CIRPPIS Retenue à la source");
    }

    // ── Tool 4: Fetch IS/TVA rules ────────────────────────────────────────────

    [KernelFunction("fetch_domestic_tax_rules")]
    [Description(
        "Fetch domestic tax rules for IS (Impôt sur les Sociétés) or TVA. " +
        "Use tax_type='IS' for corporate tax, 'TVA' for value-added tax, 'IRPP' for income tax. " +
        "For IS: fetches Art.45/47 (base imposable) and applicable rates. " +
        "For TVA: fetches CTVA articles about taxable services.")]
    public async Task<string> FetchDomesticTaxRules(
        [Description("Tax type: IS, TVA, or IRPP")] string taxType,
        [Description("Specific keywords for this case")] string keywords = "")
    {
        _logger.LogInformation("[Planner] Fetching domestic {T} rules", taxType);

        var kws = taxType.ToUpper() switch
        {
            "IS"   => new[] { "personnes morales", "bénéfices passibles", "s'applique", "taux", "résultat" },
            "TVA"  => new[] { "soumises", "affaires", "activités", "assujetti", "prestation" },
            "IRPP" => new[] { "revenu", "personne physique", "catégorie", "traitements" },
            _      => new[] { keywords }
        };

        if (!string.IsNullOrEmpty(keywords))
            kws = kws.Concat(keywords.Split(',').Select(k => k.Trim())).ToArray();

        var sources = await _retrieval.FetchDomesticTaxRulesAsync(taxType, kws, ct: default);
        AddSources(sources, $"Domestic/{taxType}");
        return FormatResult(sources, $"Règles {taxType} domestiques");
    }

    // ── Tool 5: Semantic search ────────────────────────────────────────────────

    [KernelFunction("semantic_search_legal")]
    [Description(
        "Semantic vector search in the legal knowledge base. " +
        "Use when you need sources that cannot be fetched by targeted methods. " +
        "Best for finding jurisprudence, doctrine, or specific concepts.")]
    public async Task<string> SemanticSearchLegal(
        [Description("Search query describing what you need")] string query,
        [Description("Optional doc type filter: Convention, Code, LoiFinances, Doctrine, Commentaire")] string docTypeFilter = "")
    {
        _logger.LogInformation("[Planner] Semantic search: {Q}", query[..Math.Min(query.Length, 60)]);

        var results = await _embed.SearchAsync(query, topK: 10);

        if (!string.IsNullOrEmpty(docTypeFilter))
            results = results.Where(s =>
                s.DocType.Equals(docTypeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        AddSources(results, $"Semantic/{query[..Math.Min(query.Length,30)]}");
        return FormatResult(results, $"Semantic search: {query[..Math.Min(query.Length,50)]}");
    }

    // ── Tool 6: Finish retrieval ───────────────────────────────────────────────

    [KernelFunction("finish_retrieval")]
    [Description(
        "Call this when you have enough sources to proceed with consultation generation. " +
        "MUST be called after at most 2 rounds of fetching. " +
        "Always call this to signal completion, even if sources are insufficient.")]
    public Task<string> FinishRetrieval(
        [Description("Brief explanation of what was found and why retrieval is complete")] string reason,
        [Description("Detected income type: redevance|interets|dividendes|benefices|salaire|unknown")] string incomeType = "unknown",
        [Description("Is ES (établissement stable) risk possible? true/false")] bool esRisk = false)
    {
        _finished      = true;
        IncomeType     = incomeType.ToLower().Trim();
        EsRiskPossible = esRisk;
        PlannerReasoning = reason;

        _logger.LogInformation("[Planner] ✓ Finished: {N} sources | income={I} | es={E} | {R}",
            _accumulated.Count, IncomeType, EsRiskPossible,
            reason[..Math.Min(reason.Length, 100)]);

        return Task.FromResult(
            $"Retrieval complete. {_accumulated.Count} sources collected. " +
            $"Income type: {incomeType}. ES risk: {esRisk}. {reason}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddSources(List<LegalSourceDto> sources, string label)
    {
        int added = 0;
        foreach (var s in sources)
        {
            var key = !string.IsNullOrEmpty(s.ChunkId)
                ? s.ChunkId
                : s.DocName + s.ArticleRef + s.Text[..Math.Min(s.Text.Length, 50)];
            if (_seen.Add(key))
            {
                _accumulated.Add(s);
                added++;
            }
        }
        _logger.LogInformation("[Planner] {L}: +{A} new sources (total={T})",
            label, added, _accumulated.Count);
    }

    private static string FormatResult(List<LegalSourceDto> sources, string label)
    {
        if (!sources.Any())
            return $"[{label}] No sources found.";

        var sb = new StringBuilder($"[{label}] Found {sources.Count} sources:\n");
        foreach (var s in sources.Take(5))
            sb.AppendLine($"  - {s.DocType} | {s.DocName} | {s.ArticleRef} | {s.Text[..Math.Min(s.Text.Length, 100)]}...");
        return sb.ToString();
    }
}
