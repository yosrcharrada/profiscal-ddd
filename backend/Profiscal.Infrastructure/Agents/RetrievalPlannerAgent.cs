using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Profiscal.Domain.Dtos;
using Profiscal.Domain.Abstractions.Agents;
using Microsoft.Extensions.Logging;

namespace Profiscal.Infrastructure.Agents;

/// <summary>
/// TRUE ReAct Retrieval Agent — bounded 2-round loop with parallel tool dispatch.
///
/// This is a genuine agent (Brain + Memory + Actions + Observe-Adapt loop):
///   ROUND 1 — REASON + ACT:
///     Brain decides which legal sources are needed for the case.
///     Dispatches ALL chosen tool calls in PARALLEL (Task.WhenAll).
///   ROUND 2 — OBSERVE + REASON + ACT:
///     Brain observes what each tool returned (including EMPTY results),
///     reasons about gaps, dispatches gap-filling calls in parallel,
///     then signals finish.
///
/// Why parallel dispatch + 2-round cap:
///   The EY Azure endpoint is slow per LLM call (~6s). Naive SK tool-calling
///   makes one round trip PER tool (sequential) = 140s. By having the LLM emit
///   ALL its tool calls in one response and executing them concurrently, we get
///   true agentic behaviour in just 2 LLM calls (~13-16s total).
///
/// Business rules the agent is instructed to follow:
///   - Foreign provider → ALWAYS fetch ES article (Convention Art.5) first
///   - Service/redevance → fetch redevance article (Convention Art.12)
///   - Note Commune N°2/2015 → for all countries EXCEPT Allemagne
///   - Always fetch domestic retenue fallback (CIRPPIS Art.52)
///   - Art.92 CIRPPIS = LF reference, not an autonomous code article
/// </summary>
public sealed class RetrievalPlannerAgent : IRetrievalPlannerAgent
{
    private readonly IRetrievalAgent   _retrieval;
    private readonly IEmbedSearchAgent _embed;
    private readonly ILlmAgent         _llm;
    private readonly ILogger<RetrievalPlannerAgent> _logger;

    private static readonly HashSet<string> NoteCommune2Exceptions =
        new(StringComparer.OrdinalIgnoreCase) { "allemagne" };

    private const int MaxRounds = 2;

    private const string AgentSystem =
        "Tu es un agent de recherche documentaire fiscale tunisienne.\n" +
        "Tu décides quels outils appeler pour trouver les sources juridiques EXACTES.\n" +
        "Tu peux appeler PLUSIEURS outils en même temps dans un seul tour.\n\n" +
        "OUTILS DISPONIBLES:\n" +
        "- fetch_convention_article(country, article_type): article_type ∈ " +
        "{etablissement_stable, redevances, interets, dividendes, benefices}\n" +
        "- fetch_note_commune_2(country): tableaux taux par pays (SAUF Allemagne)\n" +
        "- fetch_domestic_retenue(keywords): retenue source CIRPPIS Art.52\n" +
        "- fetch_domestic_tax(tax_type, keywords): tax_type ∈ {IS, TVA, IRPP}\n" +
        "- semantic_search(query): recherche sémantique large\n\n" +
        "SÉQUENCE OBLIGATOIRE pour prestataire étranger:\n" +
        "1. fetch_convention_article(pays, etablissement_stable)\n" +
        "2. fetch_convention_article(pays, redevances) si service/assistance\n" +
        "3. fetch_note_commune_2(pays) sauf Allemagne\n" +
        "4. fetch_domestic_retenue(...) en fallback\n\n" +
        "Art.92 CIRPPIS = référence Loi de Finances, pas article autonome.\n" +
        "RÉPONDS UNIQUEMENT EN JSON.";

    public RetrievalPlannerAgent(
        IRetrievalAgent   retrieval,
        IEmbedSearchAgent embed,
        ILlmAgent         llm,
        ILogger<RetrievalPlannerAgent> logger)
    {
        _retrieval = retrieval;
        _embed     = embed;
        _llm       = llm;
        _logger    = logger;
    }

    public async Task<RetrievalPlan> PlanAndRetrieveAsync(
        string          situation,
        string          fiscalQuestion,
        HashSet<string> branches,
        List<string>    countries,
        bool            isIntl,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("┌─ [AGENT] True ReAct retrieval (max {R} rounds)…", MaxRounds);

        var sources    = new List<LegalSourceDto>();
        var seen       = new HashSet<string>();
        var country    = countries.FirstOrDefault() ?? "";
        var incomeType = "unknown";
        var esRisk     = false;
        var nc2Used    = false;
        var detected   = country;
        int round      = 0;

        // Memory: the observation log the agent reasons over across rounds
        var observations = new StringBuilder();

        while (round < MaxRounds)
        {
            round++;
            _logger.LogInformation("│  [AGENT] Round {R}/{M} — reasoning…", round, MaxRounds);

            // ── REASON: build the prompt (round 1 = initial, round 2 = observe) ──
            var prompt = round == 1
                ? BuildRound1Prompt(situation, fiscalQuestion, branches, country, isIntl)
                : BuildRound2Prompt(observations.ToString());

            var raw = await _llm.CompleteAsync(AgentSystem, prompt, $"Agent-R{round}", 700, ct);
            if (raw is null)
            {
                _logger.LogWarning("│  [AGENT] Round {R} LLM null — stopping", round);
                break;
            }

            var decision = ParseDecision(raw);
            if (decision is null)
            {
                _logger.LogWarning("│  [AGENT] Round {R} unparseable — stopping", round);
                break;
            }

            // Capture metadata the agent inferred
            if (!string.IsNullOrEmpty(decision.IncomeType) && decision.IncomeType != "unknown")
                incomeType = decision.IncomeType;
            if (decision.EsRisk) esRisk = true;
            if (!string.IsNullOrEmpty(decision.Country)) detected = decision.Country.ToLower();

            // ── ACT: execute all tool calls IN PARALLEL ──────────────────────
            if (decision.ToolCalls?.Any() == true)
            {
                _logger.LogInformation("│  [AGENT] Round {R}: dispatching {N} parallel tool calls",
                    round, decision.ToolCalls.Count);

                var tasks = decision.ToolCalls
                    .Select(tc => ExecuteToolAsync(tc, detected, ct))
                    .ToList();
                var batches = await Task.WhenAll(tasks);

                // ── OBSERVE: record results (with EMPTY flags so agent sees gaps) ──
                for (int i = 0; i < decision.ToolCalls.Count; i++)
                {
                    var tc      = decision.ToolCalls[i];
                    var batch   = batches[i];
                    var added   = AddUnique(sources, seen, batch);
                    if (tc.Tool == "fetch_note_commune_2" && batch.Any()) nc2Used = true;

                    var status = batch.Any()
                        ? $"{batch.Count} chunks trouvés (+{added} nouveaux)"
                        : "VIDE — aucune source";
                    observations.AppendLine(
                        $"- {tc.Tool}({tc.Country ?? tc.ArticleType ?? tc.TaxType ?? ""}): {status}");
                }
            }

            // ── Termination check ────────────────────────────────────────────
            if (decision.Finish)
            {
                _logger.LogInformation("│  [AGENT] Round {R}: agent signalled finish", round);
                break;
            }
            if (round >= MaxRounds)
            {
                _logger.LogInformation("│  [AGENT] Max rounds reached — finishing", round);
                break;
            }
        }

        // Apply Germany exception defensively
        if (NoteCommune2Exceptions.Contains(detected)) nc2Used = false;

        sw.Stop();
        for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;

        _logger.LogInformation(
            "└─ [AGENT] ✓ {Ms:F0}ms | {N} sources | income={I} | es={E} | nc2={NC} | rounds={R}",
            sw.Elapsed.TotalMilliseconds, sources.Count, incomeType, esRisk, nc2Used, round);

        return new RetrievalPlan(
            Sources:          sources,
            IncomeType:       incomeType,
            EsRiskPossible:   esRisk,
            DetectedCountry:  string.IsNullOrEmpty(detected) ? null : detected,
            NoteCommune2Used: nc2Used,
            IterationsUsed:   round,
            PlannerReasoning: $"income={incomeType} es={esRisk} rounds={round}");
    }

    // ── Tool execution dispatcher ──────────────────────────────────────────────

    private async Task<List<LegalSourceDto>> ExecuteToolAsync(
        ToolCall tc, string detectedCountry, CancellationToken ct)
    {
        try
        {
            var country = string.IsNullOrEmpty(tc.Country) ? detectedCountry : tc.Country.ToLower();

            switch (tc.Tool)
            {
                case "fetch_convention_article":
                {
                    var kws = (tc.ArticleType ?? "redevances") switch
                    {
                        "etablissement_stable" => new[] { "établissement stable", "chantier", "durée", "installation fixe", "services" },
                        "redevances"           => new[] { "redevances", "rémunérations", "usage", "droit", "information", "assistance" },
                        "interets"             => new[] { "intérêts", "créance", "prêt" },
                        "dividendes"           => new[] { "dividendes", "distribution", "participation" },
                        "benefices"            => new[] { "bénéfices", "entreprise", "résultat" },
                        _                      => new[] { tc.ArticleType ?? "redevances" }
                    };
                    return await _retrieval.FetchConventionArticleAsync(country, kws, ct);
                }

                case "fetch_note_commune_2":
                    if (NoteCommune2Exceptions.Contains(country)) return new();
                    return await _retrieval.FetchNoteCommune2Async(country, ct);

                case "fetch_domestic_retenue":
                {
                    var kws = new List<string> { "retenue", "source", "non-résident", "taux", "versés" };
                    if (!string.IsNullOrEmpty(tc.Keywords))
                        kws.AddRange(tc.Keywords.Split(',').Select(k => k.Trim()));
                    return await _retrieval.FetchDomesticRetenueAsync(kws, ct);
                }

                case "fetch_domestic_tax":
                {
                    var kws = (tc.TaxType?.ToUpper()) switch
                    {
                        "IS"   => new[] { "personnes morales", "bénéfices passibles", "taux", "résultat" },
                        "TVA"  => new[] { "soumises", "affaires", "activités", "prestation", "assujetti" },
                        "IRPP" => new[] { "revenu", "personne physique", "catégorie" },
                        _      => new[] { "impôt" }
                    };
                    return await _retrieval.FetchDomesticTaxRulesAsync(tc.TaxType ?? "IS", kws, ct);
                }

                case "semantic_search":
                    return await _embed.SearchAsync(tc.Query ?? tc.Keywords ?? "", topK: 8);

                default:
                    _logger.LogDebug("│  [AGENT] Unknown tool: {T}", tc.Tool);
                    return new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "│  [AGENT] Tool {T} failed", tc.Tool);
            return new();
        }
    }

    // ── Prompt builders ─────────────────────────────────────────────────────────

    private static string BuildRound1Prompt(
        string situation, string fiscalQuestion,
        HashSet<string> branches, string country, bool isIntl)
    {
        var nc2 = isIntl && !string.IsNullOrEmpty(country) && !NoteCommune2Exceptions.Contains(country)
            ? "APPLICABLE"
            : NoteCommune2Exceptions.Contains(country) ? "NON (Allemagne)" : "non (domestique)";

        return
            $"SITUATION: {situation[..Math.Min(situation.Length, 280)]}\n" +
            $"QUESTION: {fiscalQuestion[..Math.Min(fiscalQuestion.Length, 160)]}\n" +
            $"BRANCHES: {string.Join(", ", branches)}\n" +
            $"PAYS: {(string.IsNullOrEmpty(country) ? "à déterminer depuis le contexte" : country)}\n" +
            $"INTERNATIONAL: {(isIntl ? "OUI" : "NON")}\n" +
            $"NOTE COMMUNE N°2/2015: {nc2}\n\n" +
            "TOUR 1 — Décide quels outils appeler pour ce cas (plusieurs possibles).\n" +
            "Si pays non détecté mais société étrangère évoquée, identifie le pays.\n\n" +
            "Réponds en JSON:\n" +
            "{\"reasoning\":\"...\",\"country\":\"france\",\"income_type\":\"redevance\"," +
            "\"es_risk\":true,\"tool_calls\":[" +
            "{\"tool\":\"fetch_convention_article\",\"country\":\"france\",\"article_type\":\"etablissement_stable\"}," +
            "{\"tool\":\"fetch_convention_article\",\"country\":\"france\",\"article_type\":\"redevances\"}," +
            "{\"tool\":\"fetch_note_commune_2\",\"country\":\"france\"}," +
            "{\"tool\":\"fetch_domestic_retenue\",\"keywords\":\"assistance,services\"}" +
            "],\"finish\":false}";
    }

    private static string BuildRound2Prompt(string observations)
    {
        return
            "TOUR 2 — Voici ce que chaque outil a retourné:\n\n" +
            observations + "\n" +
            "Analyse les résultats. Si une source CRITIQUE est VIDE (ex: article ES, " +
            "redevance, ou Note Commune), appelle un outil alternatif pour combler le manque " +
            "(ex: semantic_search, ou fetch avec d'autres mots-clés).\n" +
            "Si tu as suffisamment de sources, mets finish=true.\n\n" +
            "Réponds en JSON:\n" +
            "{\"reasoning\":\"...\",\"tool_calls\":[...],\"finish\":true}";
    }

    // ── JSON parsing ─────────────────────────────────────────────────────────────

    private static AgentDecision? ParseDecision(string raw)
    {
        raw = Regex.Replace(raw.Trim(), @"^```(json)?\s*", "", RegexOptions.Multiline);
        raw = Regex.Replace(raw.Trim(), @"\s*```$",          "", RegexOptions.Multiline);
        var s = raw.IndexOf('{'); var e = raw.LastIndexOf('}');
        if (s >= 0 && e > s) raw = raw[s..(e + 1)];
        try
        {
            return JsonSerializer.Deserialize<AgentDecision>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static int AddUnique(
        List<LegalSourceDto> target, HashSet<string> seen, List<LegalSourceDto> toAdd)
    {
        int added = 0;
        foreach (var src in toAdd)
        {
            var key = !string.IsNullOrEmpty(src.ChunkId)
                ? src.ChunkId
                : src.DocName + src.Text[..Math.Min(src.Text.Length, 50)];
            if (seen.Add(key)) { target.Add(src); added++; }
        }
        return added;
    }

    // ── DTOs for the tool-call protocol ──────────────────────────────────────────

    private sealed class AgentDecision
    {
        [JsonPropertyName("reasoning")]   public string?         Reasoning  { get; set; }
        [JsonPropertyName("country")]     public string?         Country    { get; set; }
        [JsonPropertyName("income_type")] public string?         IncomeType { get; set; }
        [JsonPropertyName("es_risk")]     public bool            EsRisk     { get; set; }
        [JsonPropertyName("finish")]      public bool            Finish     { get; set; }
        [JsonPropertyName("tool_calls")]  public List<ToolCall>? ToolCalls  { get; set; }
    }

    private sealed class ToolCall
    {
        [JsonPropertyName("tool")]         public string  Tool        { get; set; } = "";
        [JsonPropertyName("country")]      public string? Country     { get; set; }
        [JsonPropertyName("article_type")] public string? ArticleType { get; set; }
        [JsonPropertyName("tax_type")]     public string? TaxType     { get; set; }
        [JsonPropertyName("keywords")]     public string? Keywords    { get; set; }
        [JsonPropertyName("query")]        public string? Query       { get; set; }
    }
}
