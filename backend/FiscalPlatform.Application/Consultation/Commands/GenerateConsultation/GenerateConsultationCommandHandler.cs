using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Application.Consultation.Agents;
using FiscalPlatform.Application.Consultation.Playbooks;
using FiscalPlatform.Domain.Exceptions;
using FiscalPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;

/// <summary>
/// Orchestrates the full consultation generation pipeline.
///
/// Pipeline:
///   Step 0 — RetrievalPlannerAgent (TRUE SK ReAct agent, max 2 iterations)
///             → Identifies income type, ES risk, fetches targeted sources
///   Step 1 — Non-LLM detection (BranchDetector, CountryDetector, KeywordExtractor)
///   Step 2 — Embed search (broad semantic, Python microservice)
///   Step 3 — Neo4j retrieval (keyword + graph expansion)
///   Step 4 — Merge all sources (planner + embed + Neo4j)
///   Step 5 — LLM Phase 1 (contexte, étendue, sommaire, pays_non_resident)
///   Step 5b — Post-Phase-1 country detection (convention fetch if needed)
///   Step 6 — LLM Phase 2 + 3 PARALLEL (analyses ‖ table)
///   Step 7 — Document generation (OpenXML)
///   Step 8 — Persist (fire-and-forget)
///
/// Anti-hallucination measures:
///   - Temperature = 0
///   - Citation-only system [S1],[S2]...
///   - Convention exclusion when no country detected
///   - pays_non_resident detection in Phase 1
///   - Art.92 CIRPPIS LF-reference correction
///   - Rate citation rule (every % must cite [Sn])
///   - ES mandatory sequence for foreign providers
///   - Note Commune N°2/2015 for all convention countries except Allemagne
///   - Output guardrail post-generation
///   - Reflective Loop: retry Phase 1 on bad JSON
///   - try/finally: timing table always prints
/// </summary>
public sealed class GenerateConsultationCommandHandler(
    IBranchDetector          branchDetector,
    ICountryDetector         countryDetector,
    IKeywordExtractor        keywordExtractor,
    IRetrievalPlannerAgent   plannerAgent,
    IEmbedSearchAgent        embedAgent,
    IRetrievalAgent          retrievalAgent,
    IRuleBasedRetrieval      ruleRetrieval,
    ILlmAgent                llmAgent,
    IDocumentGenerationAgent docAgent,
    Orchestration.ConsultationWorkflow consultationWorkflow,
    IFiscalGuardrails        guardrails,
    IConsultationRepository  repository,
    ILogger<GenerateConsultationCommandHandler> logger)
    : IRequestHandler<GenerateConsultationCommand, ConsultationGeneratedDto>
{
    // Countries where Note Commune N°2/2015 is superseded by newer convention
    private static readonly HashSet<string> NoteCommune2Exceptions =
        new(StringComparer.OrdinalIgnoreCase) { "allemagne" };

    internal const string SystemPrompt =
        "Tu es Faiez Choyakh — fiscaliste tunisien senior, EY Tunisia.\n" +
        "CITATIONS: [S1],[S2]... uniquement. Jamais de document en clair. Jamais inventer un article.\n" +
        "TAUX: LIS chaque taux DEPUIS le texte de l'article cité [Sn] et recopie le chiffre EXACT qui y figure. " +
        "Jamais de taux de mémoire, jamais inventé, jamais supposé.\n" +
        "ART.92 CIRPPIS: si source contient 'Art 92-X LF' = référence LF, pas article autonome.\n" +
        "PRINCIPE DIRECTEUR — TAUX LE PLUS FAVORABLE: parmi TOUS les fondements applicables, retenir le\n" +
        "  traitement le plus favorable au contribuable (taux le plus bas, voire exonération) qui respecte\n" +
        "  TOUTES les conditions légales. La convention prime toujours le droit commun et peut réduire ou\n" +
        "  supprimer l'imposition tunisienne. Ne jamais retenir un taux plus élevé s'il existe un fondement\n" +
        "  plus favorable dont les conditions sont remplies.\n" +
        "TAUX SPÉCIFIQUE (lex specialis): dans un article de taux, LIS TOUT l'article et applique la LIGNE\n" +
        "  correspondant PRÉCISÉMENT à la NATURE du revenu ET à la QUALITÉ du bénéficiaire — jamais la\n" +
        "  première ligne venue. IMPÉRATIF: pour un bénéficiaire NON-RÉSIDENT NON ÉTABLI, applique la ligne\n" +
        "  qui vise EXPRESSÉMENT les revenus « servis aux non domiciliés ni établis » / « aux non-résidents »,\n" +
        "  et NON la ligne des paiements aux résidents (régime réel), même si le taux diffère. Le taux\n" +
        "  spécifique prime le taux général. Le 'taux le plus favorable' ne joue qu'entre fondements RÉELLEMENT\n" +
        "  CONCURRENTS pour la MÊME situation (convention vs droit commun ; régime standard vs régime réduit\n" +
        "  dont les conditions sont remplies), jamais entre sous-taux de catégories différentes.\n" +
        "PRESTATAIRE ÉTRANGER — séquence obligatoire:\n" +
        "  0. RÉGIME FISCAL PRIVILÉGIÉ (À VÉRIFIER EN PREMIER): le pays du bénéficiaire figure-t-il dans la\n" +
        "     liste des États/territoires à régime fiscal privilégié [Sn] ? SI OUI → NE PAS analyser NI\n" +
        "     mentionner l'établissement stable (notion écartée pour un bénéficiaire à régime privilégié) :\n" +
        "     passer directement à la RS (pt.2), sans verdict d'ES. SI NON → dérouler l'ES au pt.1.\n" +
        "  1. ES (établissement stable) — SEULEMENT si le bénéficiaire n'est PAS à régime privilégié:\n" +
        "     trancher OUI/NON, d'abord SELON LE DROIT COMMUN (Art.45/47 CIRPPIS\n" +
        "     + doctrine: interprétation extensive, règle des 6 mois même pour une seule prestation), PUIS\n" +
        "     SELON l'ART.5 de la convention. Si ES en Tunisie → imposition (IS/RS) selon le régime de l'ES.\n" +
        "  2. EN L'ABSENCE D'ES — qualifier le revenu et appliquer le régime correspondant:\n" +
        "     • S'IL EXISTE UNE CONVENTION: qualifier le revenu au regard de la convention (bénéfice\n" +
        "       d'entreprise, redevance, dividende, intérêt, profession indépendante…) et appliquer le\n" +
        "       TRAITEMENT CONVENTIONNEL de cette catégorie:\n" +
        "         - bénéfice d'entreprise (Art.7) → imposable UNIQUEMENT dans l'État de résidence → AUCUNE\n" +
        "           imposition tunisienne (ni RS) en l'absence d'ES ;\n" +
        "         - redevance / dividende / intérêt (Art.12/10/11) → imposable dans l'État de la source au\n" +
        "           TAUX RÉDUIT de la convention [Sn], sous réserve des conditions de la convention.\n" +
        "       LA CONVENTION PRIME TOUT RÉGIME INTERNE DE RS — l'Art.52 CIRPPIS comme tout régime interne\n" +
        "       SECTORIEL (travaux, montage, installation, surveillance, construction) ou toute note commune\n" +
        "       fixant un taux interne : ces régimes ne valent qu'à DÉFAUT de convention.\n" +
        "     • SANS CONVENTION: droit commun — Art.52 CIRPPIS, au taux correspondant à la nature du revenu\n" +
        "       et à la qualité du bénéficiaire [Sn]. ES SUPERFÉTATOIRE si ce taux s'applique que l'ES existe\n" +
        "       ou non (le préciser ; ne pas mettre 'NON DOCUMENTÉ' pour l'ES). Puis vérifier le régime privilégié (pt.3).\n" +
        "  3. RÉGIME FISCAL PRIVILÉGIÉ — VÉRIFIER le pays du bénéficiaire DANS la liste retrouvée [Sn]. NE\n" +
        "     JAMAIS affirmer qu'un pays n'y figure pas sans avoir lu la liste. S'il Y FIGURE, la majoration de\n" +
        "     RS ne s'applique QUE si l'activité relève du taux d'IS le plus élevé ; sinon elle ne s'applique\n" +
        "     pas ; si l'arrêté n'est pas actualisé, son application est incertaine → conclure prudemment au\n" +
        "     taux de droit commun [Sn].\n" +
        "  4. TVA: toujours — champ Art.1, TERRITORIALITÉ Art.3, Art.5 ; taux Art.7 [Sn] (ou taux réduit des\n" +
        "     tableaux annexes A/B si l'opération y figure). Prestataire NON établi = RETENUE À LA SOURCE DE\n" +
        "     100% DE LA TVA par le preneur (TVA déductible).\n" +
        "  5. SECTIONS OBLIGATOIRES (cas RS/international): (a) ASSIETTE DE LA RS = montant BRUT, TVA COMPRISE\n" +
        "     [Sn] ; (b) FORMALISME TRANSFERT DE FONDS = certificat de retenue à la source (attestation de\n" +
        "     régularisation, Art.112 CDPF [Sn], non exigée si la RS a été opérée) ; si l'Art.21 de la\n" +
        "     circulaire BCT N°2016-9 figure parmi les sources [Sn], vise-le pour les justificatifs exigés.\n" +
        "     Cite UNIQUEMENT les textes réellement fournis [Sn] — n'invente AUCUN numéro d'article ni de\n" +
        "     circulaire absent des sources.\n" +
        "  6. NE JAMAIS introduire de condition non étayée par les faits.\n" +
        "CONVENTION: Art.5=ES, Art.7=bénéfices, Art.10=dividendes, Art.11=intérêts,\n" +
        "  Art.12=redevances, Art.14=prof.indép., Art.15=salaires.\n" +
        "NOTE COMMUNE N°2/2015: l'utiliser pour interpréter les conventions (taux/qualification par pays, sauf Allemagne).\n" +
        "HIÉRARCHIE: International: Convention→Codes→LdF→Doctrine. Local: Codes→LdF→Doctrine.\n" +
        "ÉTENDUE: UNIQUEMENT ce que le client demande. ZÉRO ajout.\n" +
        "VERDICTS: OUI / NON / le taux chiffré réel (lu dans [Sn]) / EXONÉRÉ / SOUMIS. " +
        "N'écris JAMAIS le littéral « X% » : recopie le vrai pourcentage. NON DOCUMENTÉ si aucune source.\n" +
        "JSON PUR UNIQUEMENT.";

    // ── Timing table ──────────────────────────────────────────────────────────
    private sealed record TimingEntry(string Step, double Ms, string Notes);

    private void LogTimingTable(List<TimingEntry> timings, string reference)
    {
        // Built as ONE multi-line string and logged with a SINGLE call, so the console shows a
        // contiguous box instead of wrapping every row in the logger's category/timestamp prefix.
        const int wStep = 34, wDur = 10, wNote = 34;
        string Bar(char l, char m, char r) => l + new string('─', wStep + 2) + m +
            new string('─', wDur + 2) + m + new string('─', wNote + 2) + r;
        string Row(string s, string d, string n) =>
            "│ " + Clip(s, wStep).PadRight(wStep) + " │ " + d.PadLeft(wDur) + " │ " +
            Clip(n, wNote).PadRight(wNote) + " │";
        static string Clip(string s, int w) => (s ?? "").Length > w ? s![..(w - 1)] + "…" : (s ?? "");
        static string Dur(double ms) => ms >= 1000 ? $"{ms / 1000.0:F1}s" : $"{ms:F0}ms";

        // inner width so a merged title row equals the data-row total width (88):
        //   data row = "│ "+34+" │ "+10+" │ "+34+" │"  → title pad = 34+10+34 + 6 = 84
        const int inner = wStep + wDur + wNote + 6;
        string TitleRow(string t) => "│ " + Clip(t, inner).PadRight(inner) + " │";

        var sb = new StringBuilder("\n");
        sb.AppendLine(Bar('┌', '┬', '┐').Replace('┬', '─'));
        sb.AppendLine(TitleRow("CHRONOMÉTRAGE — consultation " + reference));
        sb.AppendLine(Bar('├', '┬', '┤'));
        sb.AppendLine(Row("Étape", "Durée", "Détail"));
        sb.AppendLine(Bar('├', '┼', '┤'));
        foreach (var t in timings.Where(x => !x.Step.StartsWith("TOTAL", StringComparison.Ordinal)))
            sb.AppendLine(Row(t.Step, Dur(t.Ms), t.Notes));
        var total = timings.FirstOrDefault(x => x.Step.StartsWith("TOTAL", StringComparison.Ordinal));
        if (total is not null)
        {
            sb.AppendLine(Bar('├', '┼', '┤'));
            sb.AppendLine(Row(total.Step, Dur(total.Ms), total.Notes));
        }
        sb.Append(Bar('└', '┴', '┘'));
        logger.LogInformation("{Table}", sb.ToString());
    }

    public async Task<ConsultationGeneratedDto> Handle(
        GenerateConsultationCommand cmd, CancellationToken ct)
    {
        var total   = Stopwatch.StartNew();
        var timings = new List<TimingEntry>();

        logger.LogInformation("╔══════════════════════════════════════════════════════╗");
        logger.LogInformation("║  CONSULTATION {Ref} — START", cmd.Reference);
        logger.LogInformation("║  Client : {C}", cmd.ClientName);
        logger.LogInformation("╚══════════════════════════════════════════════════════╝");

        try
        {
            return await RunPipelineAsync(cmd, ct, total, timings);
        }
        catch (Exception ex)
        {
            total.Stop();
            timings.Add(new("TOTAL (FAILED)", total.Elapsed.TotalMilliseconds, ex.GetType().Name));
            LogTimingTable(timings, cmd.Reference);
            logger.LogError(ex, "✗ [{Ref}] Generation failed after {Ms:F0}ms",
                cmd.Reference, total.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private async Task<ConsultationGeneratedDto> RunPipelineAsync(
        GenerateConsultationCommand cmd, CancellationToken ct,
        Stopwatch total, List<TimingEntry> timings)
    {
        // ── Step 0: INPUT GUARDRAIL — block off-topic requests before any LLM spend ──
        var (inputOk, inputReason) = guardrails.ValidateInput(cmd.Situation, cmd.FiscalQuestion);
        if (!inputOk)
            throw new ConsultationGenerationException("Guardrail d'entrée : " + inputReason);

        // ── Step 1: Non-LLM detection ─────────────────────────────────────────
        var sw1 = Stopwatch.StartNew();
        var branches            = branchDetector.Detect(cmd.Situation, cmd.FiscalQuestion);
        var (countries, isIntl) = countryDetector.Detect(cmd.Situation + " " + cmd.FiscalQuestion);
        var (keywords, entities)= keywordExtractor.Extract(cmd.Situation, cmd.FiscalQuestion);
        sw1.Stop();
        var s1Notes = $"kw={keywords.Count} br=[{string.Join(",", branches)}]";
        timings.Add(new("1. Detection", sw1.Elapsed.TotalMilliseconds, s1Notes));
        logger.LogInformation("► [STEP 1] ({Ms:F0}ms) | {N}", sw1.Elapsed.TotalMilliseconds, s1Notes);

        // ── Step 2: RetrievalPlannerAgent (TRUE SK ReAct agent) ───────────────
        // Runs BEFORE embed + Neo4j to get targeted sources
        logger.LogInformation("┌─ [STEP 2] RetrievalPlannerAgent (ReAct, max 2 iter)…");
        var sw2   = Stopwatch.StartNew();
        var plan  = await plannerAgent.PlanAndRetrieveAsync(
            cmd.Situation, cmd.FiscalQuestion, branches, countries, isIntl, ct);
        sw2.Stop();

        // Update country/intl from planner's detection
        if (!string.IsNullOrEmpty(plan.DetectedCountry) &&
            !countries.Contains(plan.DetectedCountry))
        {
            countries.Add(plan.DetectedCountry);
            isIntl = true;
        }

        timings.Add(new("2. RetrievalPlanner (SK)", sw2.Elapsed.TotalMilliseconds,
            $"{plan.Sources.Count}src iter={plan.IterationsUsed}"));
        logger.LogInformation(
            "└─ [STEP 2] ✓ ({Ms:F0}ms) | {N} sources | income={I} | es={E} | nc2={NC}",
            sw2.Elapsed.TotalMilliseconds, plan.Sources.Count,
            plan.IncomeType, plan.EsRiskPossible, plan.NoteCommune2Used);

        // ── Step 3: Embed search (broad semantic) ──────────────────────────────
        logger.LogInformation("┌─ [STEP 3] Embed search…");
        var sw3      = Stopwatch.StartNew();
        var query    = cmd.FiscalQuestion + " " + cmd.Situation[..Math.Min(cmd.Situation.Length, 200)];
        var embedSrc = await embedAgent.SearchAsync(query, topK: 20);
        var convHints = new List<LegalSourceDto>();
        if (isIntl && countries.Any())
        {
            foreach (var country in countries)
            {
                var hits = await embedAgent.SearchScopedAsync(query, country, topK: 8);
                convHints.AddRange(hits.Where(s =>
                    s.DocType == "Convention" ||
                    s.DocName.Contains(country, StringComparison.OrdinalIgnoreCase)));
            }
        }
        // Exclude ALL conventions when no country detected
        var filteredEmbed = embedSrc.Where(s =>
        {
            if (s.DocType != "Convention") return true;
            if (!countries.Any())          return false;
            return countries.Any(c => s.DocName.Contains(c, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        sw3.Stop();
        timings.Add(new("3. Embed search", sw3.Elapsed.TotalMilliseconds,
            $"{filteredEmbed.Count} hits conv={convHints.Count}"));
        logger.LogInformation("└─ [STEP 3] ✓ ({Ms:F0}ms) | {N} results",
            sw3.Elapsed.TotalMilliseconds, filteredEmbed.Count);

        // ── Step 4: Neo4j retrieval (keyword + graph) ─────────────────────────
        logger.LogInformation("┌─ [STEP 4] Neo4j retrieval…");
        var sw4 = Stopwatch.StartNew();
        List<LegalSourceDto> neo4jSources;
        string method;
        if (filteredEmbed.Count >= 5)
        {
            neo4jSources = await retrievalAgent.RetrieveSourcesAsync(
                keywords, entities, countries, isIntl, branches, convHints, 15, ct);
            method = "vector+keyword";
        }
        else
        {
            neo4jSources = await retrievalAgent.RetrieveSourcesAsync(
                keywords, entities, countries, isIntl, branches, convHints, 30, ct);
            method = "keyword+graph";
        }
        sw4.Stop();
        timings.Add(new("4. Neo4j retrieval", sw4.Elapsed.TotalMilliseconds,
            $"{neo4jSources.Count} src"));
        logger.LogInformation("└─ [STEP 4] ✓ ({Ms:F0}ms) | {N} sources via {M}",
            sw4.Elapsed.TotalMilliseconds, neo4jSources.Count, method);

        // ── Step 4b: Rule-based retrieval policy (precise, config-driven routing) ──
        var ruleSw = Stopwatch.StartNew();
        var ruleSources = await ruleRetrieval.RetrieveAsync(
            new RuleContext(branches, isIntl, countries, cmd.FiscalQuestion, cmd.Situation), ct);
        ruleSw.Stop();
        timings.Add(new("4b. Rule-based retrieval", ruleSw.Elapsed.TotalMilliseconds,
            $"{ruleSources.Count} src"));
        logger.LogInformation("► [STEP 4b] rule-based policy → {N} targeted sources", ruleSources.Count);

        // ── Step 4c: Merge all sources ────────────────────────────────────────
        // Priority: Rule-based (precise) > Planner > Embed > Neo4j
        var sources = MergeAllSources(
            ruleSources.Concat(plan.Sources).ToList(), filteredEmbed, neo4jSources, 30);
        CorrectArticleRefs(sources);
        PinRateArticles(sources);

        if (sources.Count == 0)
            throw new NoSourcesFoundException(cmd.Situation);

        var srcTypes = string.Join(" ", sources.GroupBy(s => s.DocType)
            .Select(g => $"{g.Key}:{g.Count()}"));
        logger.LogInformation("  ► {N} total sources [{T}]", sources.Count, srcTypes);
        timings.Add(new("4b. Merge sources", 0,
            $"{sources.Count} [{srcTypes}]"));

        // ── Step 5: LLM Phase 1 ───────────────────────────────────────────────
        logger.LogInformation("┌─ [PHASE 1] contexte / étendue / sommaire / pays…");
        var sw5   = Stopwatch.StartNew();
        var p1Raw = await llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase1Prompt(cmd, sources, isIntl, branches, plan), "Phase1", 2800, ct);

        if (p1Raw is null)
        {
            sw5.Stop();
            timings.Add(new("5. LLM Phase 1", sw5.Elapsed.TotalMilliseconds, "FAILED"));
            throw new ConsultationGenerationException("LLM Phase 1 returned null");
        }

        // Reflective Loop: retry if JSON malformed
        var p1 = ParseJsonDict(p1Raw);
        if (p1 is null)
        {
            logger.LogWarning("│  [PHASE 1] JSON malformed — retry with correction");
            var retryRaw = await llmAgent.CompleteAsync(SystemPrompt,
                BuildPhase1Prompt(cmd, sources, isIntl, branches, plan) +
                "\n\nATTENTION: Votre réponse précédente n'était pas du JSON valide. " +
                "Répondez UNIQUEMENT avec le JSON demandé, sans texte avant ou après.",
                "Phase1-Retry", 2800, ct);
            p1 = retryRaw is not null ? ParseJsonDict(retryRaw) : null;
            if (p1 is null)
            {
                sw5.Stop();
                throw new ConsultationGenerationException("Phase 1 invalid JSON after retry");
            }
        }

        var etendueItems  = GetList(p1, "etendue_items");
        var sommaire      = GetStr(p1, "sommaire_executif");
        var contexteFaits = GetStr(p1, "contexte_faits");
        sw5.Stop();
        timings.Add(new("5. LLM Phase 1", sw5.Elapsed.TotalMilliseconds,
            $"{etendueItems.Count} items"));
        logger.LogInformation("└─ [PHASE 1] ✓ ({Ms:F0}ms / {Min:F2}min) | {N} items",
            sw5.Elapsed.TotalMilliseconds, sw5.Elapsed.TotalMinutes, etendueItems.Count);

        // ── Step 5b: Post-Phase-1 country detection ────────────────────────────
        var detectedCountry = GetStr(p1, "pays_non_resident")
            ?.Trim().ToLower()
            .Replace("é","e").Replace("è","e").Replace("ê","e").Replace("â","a");
        if (!string.IsNullOrEmpty(detectedCountry) &&
            !countries.Contains(detectedCountry))
        {
            logger.LogInformation("► [STEP 5b] Phase 1 detected country: '{C}'", detectedCountry);
            countries.Add(detectedCountry);
            isIntl = true;
            var sw5b     = Stopwatch.StartNew();
            var convHits = await embedAgent.SearchScopedAsync(query, detectedCountry, topK: 8);
            var matching = convHits.Where(s =>
                s.DocType == "Convention" ||
                s.DocName.Contains(detectedCountry, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Any())
            {
                var existing = new HashSet<string>(sources.Select(s => s.ChunkId));
                var newConv  = matching.Where(s => !existing.Contains(s.ChunkId)).ToList();
                sources.InsertRange(0, newConv);
                for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;
                sw5b.Stop();
                timings.Add(new("5b. Convention fetch", sw5b.Elapsed.TotalMilliseconds,
                    $"{detectedCountry} +{newConv.Count}"));
                logger.LogInformation("└─ [STEP 5b] ✓ +{N} convention chunks for '{C}'",
                    newConv.Count, detectedCountry);
            }
        }

        // ── Step 6: THE ORCHESTRATOR — Microsoft Agent Framework state graph ──
        // Qualify → CaseBrief → Fulfil ⇄ Completeness (bounded) → Writer ⇄ Judge (bounded,
        // judge can also route back to retrieval) → ExpertVoice (hard guards) → Finalize (table).
        // Phase-3 (documents/references) runs in PARALLEL with the graph, exactly as before.
        var state = new Orchestration.ConsultationState
        {
            Command         = cmd,
            EtendueItems    = etendueItems,
            ContexteFaits   = contexteFaits,
            Sommaire        = sommaire,
            Countries       = countries.ToList(),
            IsInternational = isIntl,
            Branches        = branches,
            Plan            = plan,
            Sources         = sources,
        };

        logger.LogInformation("┌─ [GRAPH] MAF workflow…");
        var sw6 = Stopwatch.StartNew();
        state   = await consultationWorkflow.RunAsync(state, ct);

        sources = state.Sources;                      // graph may have augmented + re-indexed
        var analysesRaw = state.Analyses;
        var table       = state.Table;
        if (string.IsNullOrWhiteSpace(analysesRaw))
            throw new ConsultationGenerationException("Workflow returned empty analyses");

        sw6.Stop();
        foreach (var (step, ms, note) in state.Timings)
            timings.Add(new(step, ms, note));
        timings.Add(new("6. MAF workflow (‖ P3)", sw6.Elapsed.TotalMilliseconds,
            $"case={state.CaseType} rl={state.RetrievalLoops} wl={state.WriterLoops} expert={(state.ExpertApplied ? "y" : "n")}"));
        logger.LogInformation(
            "└─ [GRAPH] ✓ ({Ms:F0}ms) | case={C} | retrievalLoops={R} writerLoops={W} | judge={J} | expert={E} | table={T}",
            sw6.Elapsed.TotalMilliseconds, state.CaseType, state.RetrievalLoops, state.WriterLoops,
            state.JudgeAccepted ? "accepted" : "bounded", state.ExpertApplied, table.Count);

        // ── Step 7: Build output ──────────────────────────────────────────────
        string R(string t) => ResolveCitations(t, sources);
        var output = new ConsultationOutput
        {
            ContexteFaits   = GetStr(p1, "contexte_faits"),
            Etendue         = BuildEtendue(etendueItems, GetStr(p1, "etendue")),
            Abbreviations   = GetStr(p1, "abbreviations").Trim(),
            SommairExecutif = R(sommaire),
            Analyses        = R(analysesRaw),
            // The references section is built DETERMINISTICALLY from the sources actually cited in
            // the final analyses/table — an LLM used to write it in parallel with the workflow and
            // its [Sn] numbering drifted when the graph re-indexed sources (garbled label↔target
            // pairs, alien conventions). Code can't misalign.
            Documents       = BuildReferences(analysesRaw, table, sources),
            AnalysisTable   = table.Select(r =>
                new AnalysisRow(R(r.Sujet), R(r.Analyse), R(r.Conclusion))).ToList(),
            Sources         = sources,
            Method          = method,
            ElapsedMs       = total.Elapsed.TotalMilliseconds,
        };

        // ── Step 8: Document generation ──────────────────────────────────────
        var sw8 = Stopwatch.StartNew();
        byte[] docBytes;
        try
        {
            docBytes = docAgent.Generate(new GenerateDocumentRequest(
                cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion,
                cmd.Documents, output));
        }
        catch (Exception ex)
        {
            sw8.Stop();
            logger.LogError(ex, "│  [DOC] Generation failed — returning empty bytes");
            docBytes = Array.Empty<byte>();
        }
        sw8.Stop();
        timings.Add(new("7. Document generation", sw8.Elapsed.TotalMilliseconds, "Word .docx"));

        // ── Step 9: Persist (fire-and-forget) ─────────────────────────────────
        var aggregate = FiscalPlatform.Domain.Aggregates.Consultation.Consultation.Create(
            cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion, cmd.Documents,
            branches.Select(b => FiscalPlatform.Domain.ValueObjects.LegalBranch.TryFrom(b))
                    .Where(b => b is not null).Select(b => b!),
            countries, isIntl,
            output.ContexteFaits, output.Etendue, output.Abbreviations,
            output.SommairExecutif, output.Analyses, output.Documents,
            method, sources.Count, total.Elapsed.TotalMilliseconds);
        _ = repository.SaveAsync(aggregate, ct);

        var safeClient = Regex.Replace(cmd.ClientName.Trim(), @"[^\w\s-]", "")
                              .Trim().Replace(" ", "_");
        var filename = $"Consultation_{safeClient}_{DateTime.Now:dd-MM-yyyy}.docx";

        total.Stop();
        timings.Add(new("TOTAL", total.Elapsed.TotalMilliseconds, $"method={method}"));
        LogTimingTable(timings, cmd.Reference);

        return new ConsultationGeneratedDto(
            docBytes, filename, output, method, total.Elapsed.TotalMilliseconds, aggregate.Id);
    }

    // ── Prompt builders ───────────────────────────────────────────────────────

    internal static string SourcesBlock(List<LegalSourceDto> sources)
    {
        // Enough sources that EVERY branch (RS, TVA, régime, formalisme) reaches the prompt — cutting
        // this too low dropped the CTVA articles and produced "TVA NON DOCUMENTÉ". SMART truncation:
        // rate-bearing articles (rate tables) get their FULL text so the correct LINE is visible, other
        // sources get a short preview. CIRPPIS Art.52 is a multi-rate menu ~8 200 chars: the honoraires
        // line is ~char 1000, the non-résident b) 15% ~char 2 830, the DIVIDENDES c bis) ~char 4 450,
        // the cession f) 2,5% ~char 7 660. A 3 300 cap hid the dividend line and the model guessed the
        // wrong rate — the cap must cover the whole menu so the model reads the RIGHT paragraph.
        // Sources are now coalesced to ONE entry per article (parts merged upstream), so 18 slots =
        // ~18 distinct articles — enough for even the widest case (RS foreign: ES + RS + TVA 3/7/19 +
        // NC + CDPF 112 + BCT). PlainChars raised because a coalesced non-rate article (e.g. CTVA
        // territorialité) is several parts long and 1100 chars used to clip the operative rule.
        const int MaxSources = 18, RateChars = 8600, PlainChars = 2600;
        var sb = new StringBuilder("== SOURCES JURIDIQUES ==\n\n");
        foreach (var s in sources.Take(MaxSources))
        {
            var label   = s.IsExpert ? "COMMENTAIRE — Faiez Choyakh" : s.DocType;
            var rateBearing = s.Text.Contains('%') || s.Text.Contains("taux", StringComparison.OrdinalIgnoreCase);
            var cap     = rateBearing ? RateChars : PlainChars;
            var preview = s.Text.Length > cap ? s.Text[..cap] + "…" : s.Text;
            sb.AppendLine($"[S{s.Index}] {label} | {s.DocName} | {s.Year} | {s.ArticleRef}");
            sb.AppendLine($"       {preview}\n");
        }
        sb.AppendLine($"!! Cite UNIQUEMENT [S1]..[S{Math.Min(sources.Count, MaxSources)}].");
        return sb.ToString();
    }

    private static string BuildPhase1Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, bool isIntl, HashSet<string> branches,
        RetrievalPlan plan)
    {
        var attachedNote = cmd.AttachedDocumentTexts?.Any() == true
            ? $"\n\nDOCUMENTS CLIENT ATTACHÉS ({cmd.AttachedDocumentTexts.Count}):\n" +
              string.Join("\n---\n", cmd.AttachedDocumentTexts
                  .Select((t, i) => $"[DOC-{i+1}]: {t[..Math.Min(t.Length, 600)]}")) +
              "\n(Si contrat fourni: extraire nature des services, montants, durée, lieu d'exécution.)"
            : "";

        var hasConv = sources.Any(s => string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase));
        var plannerContext =
            $"[ANALYSE PRÉLIMINAIRE DU PLANNER]\n" +
            $"Type de revenu identifié: {plan.IncomeType}\n" +
            $"Risque ES: {(plan.EsRiskPossible ? "OUI — analyser obligatoirement" : "faible")}\n" +
            $"Convention fiscale disponible: {(hasConv ? "OUI" : "NON — appliquer le DROIT COMMUN, n'invoque AUCUNE convention")}\n" +
            $"Note Commune N°2/2015: {(plan.NoteCommune2Used ? "fetchée — utiliser ses tables" : "non applicable")}\n";

        return
            $"PHASE 1 — JSON avec 6 clés exactes.\n\n" +
            $"Client    : {cmd.ClientName}\n" +
            $"Situation : {cmd.Situation}\n" +
            $"Question  : {cmd.FiscalQuestion}\n" +
            $"Ordre     : {(isIntl ? "Convention → Codes → LdF → Doctrine" : "Codes → LdF → Doctrine")}\n" +
            $"Branches  : {string.Join(", ", branches)}\n\n" +
            plannerContext + "\n" +
            attachedNote + "\n\n" +
            SourcesBlock(sources) +
            "\nRÈGLES:\n" +
            "- etendue_items: reprends EXACTEMENT et UNIQUEMENT les questions explicitement posées " +
            "par le client — une entrée courte par question posée. N'AJOUTE AUCUN point dérivé ou " +
            "connexe : établissement stable, TVA, obligations déclaratives, prix de transfert NE " +
            "figurent PAS dans l'étendue s'ils ne sont pas explicitement demandés (ils seront " +
            "traités dans l'analyse, jamais dans l'étendue).\n" +
            "- contexte_faits: faits purs, ZÉRO citation. Début: \"Nous comprenons que :\"\n" +
            "- etendue: laisse \"\" — la section 1.2 est construite automatiquement (liste à puces) " +
            "à partir de etendue_items.\n" +
            "- abbreviations: SIGLE : Définition\n" +
            "- sommaire_executif: verdicts concis, max 1 [Sn] par point, tout taux LU depuis [Sn]. " +
            "N'INVOQUE JAMAIS une convention fiscale si 'Convention fiscale disponible: NON' — dans ce cas " +
            "applique le droit commun. N'affirme pas de conclusion contraire à celle qui découlera de l'analyse.\n" +
            "- pays_non_resident: pays de résidence de la partie étrangère (ex: france, maroc). " +
            "Identifier même si non mentionné explicitement (nom de société, groupe, devise). " +
            "Vide si transaction purement tunisienne.\n\n" +
            "{\"etendue_items\":[],\"contexte_faits\":\"\",\"etendue\":\"\"," +
            "\"abbreviations\":\"\",\"sommaire_executif\":\"\",\"pays_non_resident\":\"\"}";
    }

    // Section 1.2 — built in code as a bullet list of ONLY the asked questions.
    private static string BuildEtendue(List<string> items, string fallback)
    {
        var clean = items.Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        if (clean.Count == 0) return fallback;
        return "Notre analyse portera sur les points suivants :\n" +
               string.Join("\n", clean.Select(i => $"- {i}"));
    }

    internal static string BuildPhase2Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, List<string> etendueItems, string sommaire,
        string contexteFaits, bool isIntl, HashSet<string> branches, RetrievalPlan plan)
    {
        var n  = etendueItems.Count;
        var et = string.Join("\n", etendueItems.Select((x, i) => $"  {i+1}. {x}"));
        var concise = string.Equals(cmd.Mode, "concise", StringComparison.OrdinalIgnoreCase);

        // Data-driven flags (no hardcoded country lists):
        //  • hasConvention — did retrieval actually surface a convention for this case?
        //  • groupLink     — do the facts mention a same-group capital link (société mère/filiale)?
        var hay = ((cmd.Situation ?? "") + " " + (cmd.FiscalQuestion ?? "") + " " + (contexteFaits ?? "")).ToLowerInvariant();
        bool hasConvention = sources.Any(s => string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase));
        bool groupLink = new[]
        {
            "société mère", "societe mere", "maison mère", "maison mere", "filiale", "même groupe",
            "meme groupe", "intra-groupe", "intragroupe", "lien capitalist", "capitalistique",
            "actionnariat", "participation", "détention", "groupe"
        }.Any(hay.Contains);

        // DETERMINISTIC régime-privilégié detection (no hardcoded country list — reads the retrieved
        // list itself): a source that IS the privileged-regime list AND names the detected country.
        // Métier rule: for a beneficiary in a privileged regime, ES is NOT analysed at all — so we
        // don't leave that to the model reading the list; we detect it here and hard-forbid ES below.
        var country = (plan.DetectedCountry ?? "").Trim().ToLowerInvariant();
        var countryKey = country.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        bool privilegedRegime = country.Length > 0 && sources.Any(s =>
        {
            var t = (s.Text ?? "").ToLowerInvariant();
            return t.Contains("privilég") && countryKey.Length >= 3 && t.Contains(countryKey);
        });

        var bg = new StringBuilder();
        if (privilegedRegime)
            bg.AppendLine($"  ⚠️ RÉGIME FISCAL PRIVILÉGIÉ DÉTECTÉ — le pays du bénéficiaire ({country}) figure " +
                          "dans la liste des États/territoires à régime fiscal privilégié retrouvée [Sn]. " +
                          "EN CONSÉQUENCE : INTERDICTION ABSOLUE d'analyser OU DE MENTIONNER l'établissement " +
                          "stable (ni en droit interne, ni au sens de la convention) — aucune sous-section, " +
                          "aucun verdict, aucune phrase à son sujet. Applique DIRECTEMENT la retenue à la " +
                          "source (majorée si le taux de la majoration figure dans les sources [Sn] ; sinon " +
                          "droit commun de l'Art.52 [Sn]), puis la TVA et le formalisme du transfert.");
        if (isIntl || plan.EsRiskPossible)
        {
            bg.AppendLine("  CAS INTERNATIONAL — séquence d'analyse:");
            if (!privilegedRegime)
            {
                // ES step is EMITTED only when the beneficiary is NOT in a privileged regime.
                // When privileged (detected deterministically above), the ES sub-section is not even
                // shown to the writer — the blunt directive at the top already routed straight to RS.
                bg.AppendLine("  1. ÉTABLISSEMENT STABLE — trancher OUI/NON, d'abord selon le DROIT COMMUN " +
                              "(Art.45/47 CIRPPIS + doctrine: interprétation extensive, règle des 6 mois même pour " +
                              "une seule prestation), puis selon l'ART.5 de la convention si elle existe.");
                bg.AppendLine("     a) Présence directe du prestataire étranger (lieu fixe, personnel propre, durée) → ES OUI/NON.");
                if (groupLink)
                    bg.AppendLine("     b) Lien capitalistique (sociétés du MÊME GROUPE) : le simple contrôle NE crée PAS " +
                                  "un ES (la filiale n'est pas un ES de sa mère), sauf locaux mis à disposition ou agent " +
                                  "dépendant concluant des contrats au nom de l'étranger → verdict OUI/NON.");
                // (Pas de lien capitalistique → ne PAS évoquer la société mère / le groupe.)
            }

            if (hasConvention)
            {
                bg.AppendLine($"  2. EN L'ABSENCE D'ES — qualifier le revenu au regard de la convention (type indicatif: {plan.IncomeType}) et appliquer le traitement conventionnel de la catégorie:");
                bg.AppendLine("     • BÉNÉFICE D'ENTREPRISE (Art.7) → imposable UNIQUEMENT dans l'État de résidence → AUCUNE " +
                              "imposition tunisienne (ni RS) en l'absence d'ES.");
                bg.AppendLine("     • REDEVANCE / DIVIDENDE / INTÉRÊT (Art.12/10/11) → imposable dans l'État de la source au " +
                              "TAUX RÉDUIT de la convention [Sn], sous réserve des conditions de la convention.");
                bg.AppendLine("     • La convention PRIME TOUT RÉGIME INTERNE DE RS — l'Art.52 CIRPPIS comme tout régime interne " +
                              "SECTORIEL (travaux/montage/installation/surveillance/construction) ou note commune fixant un taux " +
                              "interne : ces régimes ne valent qu'à DÉFAUT de convention.");
            }
            else
            {
                bg.AppendLine("  2. AUCUNE CONVENTION applicable → DROIT COMMUN:");
                bg.AppendLine("     • Art.52 CIRPPIS → RS au taux de la LIGNE visant les revenus « servis aux non domiciliés ni " +
                              "établis » / « aux non-résidents » (PAS la ligne des paiements aux résidents), lis-le dans le texte cité [Sn].");
                bg.AppendLine("     • ES SUPERFÉTATOIRE si ce taux s'applique que l'ES existe ou non (le préciser).");
                bg.AppendLine("     • RÉGIME FISCAL PRIVILÉGIÉ : LIRE la liste retrouvée [Sn] et vérifier si le pays y figure. " +
                              "NE PAS affirmer qu'il n'y figure pas sans l'avoir vérifiée. S'il Y FIGURE, la majoration de RS ne " +
                              "s'applique QUE si l'activité relève du taux d'IS le plus élevé ; sinon elle ne s'applique pas ; si " +
                              "l'arrêté n'est pas actualisé, son application est incertaine → conclure prudemment au taux de droit commun [Sn].");
            }
            bg.AppendLine("  3. TAUX LE PLUS FAVORABLE : entre fondements concurrents, retenir le taux le plus bas (ou l'exonération) dont toutes les conditions sont remplies.");
            bg.AppendLine("  4. TVA : champ Art.1, TERRITORIALITÉ Art.3, Art.5 → taux Art.7 [Sn] (ou taux réduit des tableaux " +
                          "annexes A/B si l'opération y figure). Prestataire non établi = RETENUE À LA SOURCE DE 100% DE LA TVA par le preneur (TVA déductible).");
            bg.AppendLine("  5. SECTIONS OBLIGATOIRES — ne jamais omettre : (a) ASSIETTE DE LA RS = montant brut TVA comprise " +
                          "[Sn] ; (b) FORMALISME DU TRANSFERT DES FONDS = certificat de retenue à la source (attestation de " +
                          "régularisation, Art.112 CDPF [Sn], non exigée si la RS a été opérée) ; si l'Art.21 de la circulaire " +
                          "BCT N°2016-9 figure parmi les sources [Sn], vise-le pour les justificatifs exigés. Cite UNIQUEMENT " +
                          "les textes réellement fournis [Sn] — n'invente AUCUN numéro d'article ni de circulaire absent des sources.");
            bg.AppendLine("  6. Ne PAS introduire de condition non étayée par les faits.");
        }
        if (plan.NoteCommune2Used)
            bg.AppendLine("  Note Commune N°2/2015 disponible → l'utiliser pour la doctrine de l'établissement " +
                          "stable (prestataire étranger) et, s'il existe une convention, pour interpréter celle-ci " +
                          "(qualification du revenu et taux par pays).");
        if (branches.Contains("IS"))     bg.AppendLine("  IS → CIRPPIS Art.45/47 (personnes morales, bénéfices).");
        if (branches.Contains("TVA"))    bg.AppendLine("  TVA → CTVA (opérations soumises en Tunisie).");
        if (branches.Contains("IRPP"))   bg.AppendLine("  IRPP → CIRPPIS (revenu, personne physique).");
        if (branches.Contains("Retenue"))bg.AppendLine("  Retenue → CIRPPIS Art.52 + convention si international.");
        if (branches.Contains("PrixTransfert")) bg.AppendLine("  Prix de transfert → Art.48 septies CIRPPIS + CDPF.");

        var antiDraft =
            "═══ STYLE RÉDACTIONNEL — CONSULTATION FINALE REMISE AU CLIENT ═══\n" +
            "Rédige une consultation PROFESSIONNELLE et ABOUTIE, en PROSE juridique continue, comme un mémo\n" +
            "EY effectivement remis au client — PAS un brouillon ni un exercice scolaire. INTERDIT: les\n" +
            "étiquettes de raisonnement « Principe applicable : », « Application au cas : », « Détermination »,\n" +
            "« le scénario applicable », « sur la base du fait établi », et toute formulation « Si X alors Y ».\n" +
            "Rédige des PHRASES FLUIDES et liées (« Conformément à l'article … , … », « Il en résulte que … »,\n" +
            "« En conséquence, … », « Dès lors, … »). Affirme directement la position, UN SEUL verdict par point.\n" +
            "═══ INTERDICTION ABSOLUE — HÉDGING PROCÉDURAL ═══\n" +
            "Tu DISPOSES du texte COMPLET des articles dans les SOURCES ci-dessous : LIS-LES et DONNE le\n" +
            "RÉSULTAT. INTERDIT d'écrire des formules dilatoires comme « le taux doit être vérifié dans le\n" +
            "texte », « il convient de consulter la liste », « le taux reste à déterminer », « sous réserve de\n" +
            "vérification ». Donne le TAUX CHIFFRÉ EXACT lu dans la source (ex: le taux de l'Art.52 pour la\n" +
            "catégorie « non domiciliés ni établis ») et le CONSTAT direct (le pays figure OU NON sur la liste).\n" +
            "CITATIONS: utilise le NUMÉRO RÉEL de la source, p.ex. [S1], [S7] — JAMAIS le littéral « [Sn] » ni\n" +
            "« [S…] ». NON DOCUMENTÉ est réservé au cas où l'information est réellement absente des sources —\n" +
            "PAS quand tu n'as pas pris la peine de lire le texte fourni.\n";

        // The démarche is enforced as TITLED sub-sections (like the EY gold memos), with flowing prose
        // INSIDE each. This prevents the model from collapsing everything into one paragraph and
        // skipping a step (e.g. concluding 'no ES → no tax' and forgetting the Art.52 RS entirely).
        // MÉTIER: the privileged-regime step (A.3) exists ONLY when NO convention applies — with a
        // treaty in force it is never examined nor mentioned.
        var demarche =
            "DÉMARCHE OBLIGATOIRE — développe CHAQUE sous-section titrée ci-dessous, sans en sauter AUCUNE:\n" +
            "  A. IMPÔT DIRECT\n" +
            "     A.1 Établissement stable — d'abord selon le droit commun (Art.45/47), puis selon l'Art.5\n" +
            "         de la convention s'il en existe une → verdict OUI/NON. Bref et conclusif: 2 courts\n" +
            "         paragraphes maximum, sans généralités doctrinales.\n" +
            "     A.2 Imposition EN L'ABSENCE d'ES — c'est ICI qu'on tranche le TAUX: qualifier le revenu\n" +
            "         et DONNER le taux chiffré: soit le taux de RS de l'Art.52 pour la catégorie non-\n" +
            "         résident (pays SANS convention — l'absence d'ES N'exonère PAS, elle rend la RS\n" +
            "         libératoire ; lis le chiffre dans le texte de l'Art.52 fourni et écris-le), soit le\n" +
            "         traitement conventionnel (bénéfice d'entreprise = aucune RS ; redevance/dividende/\n" +
            "         intérêt = taux réduit chiffré). NE JAMAIS conclure 'pas de RS' du seul fait de\n" +
            "         l'absence d'ES pour un pays sans convention. NE cite QUE l'alinéa applicable de\n" +
            "         l'Art.52 — pas les autres lignes.\n" +
            (hasConvention
                ? ""   // convention en vigueur → le régime privilégié n'est NI examiné NI mentionné
                : "     A.3 Régime fiscal privilégié — DIS si le pays figure ou non sur la liste fournie, et\n" +
                  "         conclus (majoration applicable uniquement pour les activités au taux d'IS le plus élevé).\n") +
            "  B. TVA — territorialité (Art.3) et taux chiffré (Art.7).\n" +
            "  C. AUTRES CONSIDÉRATIONS — C.1 Assiette de la RS (NC 3/2015) ; C.2 Formalisme du transfert\n" +
            "     des fonds (certificat de retenue à la source) — cite UNIQUEMENT les textes fournis [Sn].\n";

        var styleAndFormat = concise
            ? "═══ FORMAT — VERSION CONCISE ═══\n" + demarche +
              "MÊME démarche et MÊMES conclusions que la version détaillée (mêmes verdicts, mêmes taux, mêmes\n" +
              "sous-sections) — simplement PLUS CONDENSÉE: chaque sous-section en 1 à 2 phrases, sans reproduire\n" +
              "les longues citations. N'OMETS AUCUNE sous-section, AUCUN verdict, AUCUN taux. Pas de rappel des faits.\n" +
              $"Organise en blocs « 4.1 » à « 4.{n} » (un par point d'étendue) intégrant les sous-sections ci-dessus.\n"
            : "═══ FORMAT — VERSION DÉTAILLÉE ═══\n" + demarche +
              $"Organise en blocs « 4.1 » à « 4.{n} » (un par point d'étendue), avec des SOUS-SECTIONS TITRÉES " +
              "suivant la démarche ci-dessus, et une PROSE professionnelle continue DANS chaque sous-section " +
              "(paragraphes liés, PAS d'étiquettes « Principe/Application/Conclusion »). Chaque sous-section " +
              "énonce la règle avec sa source (numéro réel, p.ex. [S1]), l'applique aux faits, et se termine " +
              "par une position claire (taux chiffré cité depuis sa source).\n";

        return
            $"PHASE 2 — JSON avec 1 clé: analyses.\n\n" +
            $"Client : {cmd.ClientName} | Question : {cmd.FiscalQuestion}\n\n" +
            $"FAITS ÉTABLIS (section 1.1) — utilise-les pour trancher:\n{contexteFaits}\n\n" +
            $"ÉTENDUE ({n} points demandés):\n{et}\n\n" +
            SourcesBlock(sources) +
            $"\nORDRE: {(isIntl ? "Convention → Codes → LdF → Doctrine" : "Codes → LdF → Doctrine")}\n" +
            bg + "\n" +
            antiDraft + "\n" +
            EyStyle.Card + "\n" +
            styleAndFormat + "\n" +
            "[Sn] OBLIGATOIRE par bloc. Tout taux doit citer sa source [Sn].\n\n" +
            "{\"analyses\":\"4. ANALYSES\\n\\n[blocs]\"}";
    }

    // References built DETERMINISTICALLY from the sources actually cited in the final analyses and
    // table — code cannot misalign the label with the target, and never lists an uncited document.
    internal static string BuildReferences(
        string analyses, List<AnalysisRow> table, List<LegalSourceDto> sources)
    {
        var citedText = analyses + " " + string.Join(" ",
            table.Select(r => r.Sujet + " " + r.Analyse + " " + r.Conclusion));
        var indexes = Regex.Matches(citedText, @"\[S(\d+)\]")
            .Select(m => int.TryParse(m.Groups[1].Value, out var i) ? i : -1)
            .Where(i => i > 0).Distinct().OrderBy(i => i).ToList();

        var lines = indexes
            .Select(i => sources.FirstOrDefault(s => s.Index == i))
            .Where(s => s is not null)
            .Select(s => $"{s!.Citation} — {s.DocType} | {s.DocName}" +
                         (string.IsNullOrWhiteSpace(s.Year) ? "" : $" ({s.Year})") +
                         (string.IsNullOrWhiteSpace(s.ArticleRef) ? "" : $" — {s.ArticleRef}"))
            .Distinct().ToList();

        return lines.Count == 0 ? "5. RÉFÉRENCES\n\n(aucune source citée)"
                                : "5. RÉFÉRENCES\n\n" + string.Join("\n", lines);
    }

    // Synthesis table derived STRICTLY from the finalized analyses (never from raw sources),
    // so it always matches the body — no "NON DOCUMENTÉ" while the analysis states a rate.
    internal static string BuildTablePrompt(List<string> etendueItems, string finalAnalyses)
    {
        var n  = etendueItems.Count;
        var et = string.Join("\n", etendueItems.Select((x, i) => $"  {i + 1}. {x}"));
        return
            $"TABLEAU DE SYNTHÈSE — JSON: analysis_table ({n} objets, un par point d'étendue, même ordre).\n\n" +
            $"POINTS D'ÉTENDUE:\n{et}\n\n" +
            $"ANALYSES FINALES (SEULE source de vérité — n'invente rien hors de ce texte):\n{finalAnalyses}\n\n" +
            "RÈGLES STRICTES (le tableau doit être LISIBLE et SYNTHÉTIQUE) :\n" +
            "- sujet : le point d'étendue, en 1 ligne courte (pas de recopie intégrale).\n" +
            "- analyse : 2 à 3 phrases MAXIMUM résumant la position retenue, avec les mêmes [Sn].\n" +
            "- conclusion : LE verdict chiffré essentiel, TRÈS COURT (ex. « RS 15% ; TVA 19% » ou " +
            "« EXONÉRÉ »). Si le point porte plusieurs sous-verdicts, mets-en UN par ligne (séparés par " +
            "un retour à la ligne « \\n »), format « Établissement stable : NON », « Retenue à la source : " +
            "15% », etc. — JAMAIS un paragraphe. Recopie fidèlement les taux/verdicts des analyses ; " +
            "INTERDIT d'écrire « NON DOCUMENTÉ » si les analyses tranchent le point. Le tableau NE DOIT " +
            "JAMAIS contredire les analyses.\n\n" +
            "{\"analysis_table\":[{\"sujet\":\"\",\"analyse\":\"Selon [Sn]: \",\"conclusion\":\"\"}]}";
    }

    // ── Source merging ────────────────────────────────────────────────────────
    // (Qualification, senior review and the judge/revision loop moved into the MAF workflow —
    //  see Orchestration/ConsultationWorkflow.cs.)

    // The rate-driving domestic articles (CIRPPIS Art.52/53 for the withholding rate, CTVA Art.7 for
    // the VAT rate) MUST be inside the model's visible source window — otherwise the model truthfully
    // reports "taux NON DOCUMENTÉ". Version-bloat (2026/2023/2022 copies of every article) plus the
    // embed server returning a slightly different neighbour set on another machine can push these past
    // the cutoff even when they were fetched. This pins the rate-bearing copy to the front (after any
    // Convention chunks, which keep priority for international cases). Deterministic — no scores, no env.
    internal static void PinRateArticles(List<LegalSourceDto> sources)
    {
        // Require an actual numeric rate ('%') — not merely the word "taux" — so a rate-less stub
        // copy of the article never jumps ahead of the version that carries the figure to read.
        static bool HasRate(LegalSourceDto s) => (s.Text ?? "").Contains('%');

        static bool IsRs(LegalSourceDto s) =>
            (s.DocName ?? "").Contains("code_irpp_is", StringComparison.OrdinalIgnoreCase) &&
            (Digits(s.ArticleRef) == "52" || Digits(s.ArticleRef) == "53");

        static bool IsTva(LegalSourceDto s) =>
            (s.DocName ?? "").Contains("code_tva", StringComparison.OrdinalIgnoreCase) &&
            Digits(s.ArticleRef) == "7";

        bool Pin(LegalSourceDto s) => HasRate(s) && (IsRs(s) || IsTva(s));

        // Newest-year copy of a pinned article wins (2026 before 2020) — the LF revises rates yearly,
        // so a stale year gives the wrong figure even when the article number is right.
        static int Year(LegalSourceDto s)
        {
            var m = Regex.Match((s.DocName ?? "") + " " + (s.Year ?? ""), @"(19|20)\d{2}");
            return m.Success ? int.Parse(m.Value) : 0;
        }

        // Stable partition: Conventions first (int'l priority), then pinned rate articles (newest year
        // first), then the rest.
        var convs  = sources.Where(s => s.DocType == "Convention").ToList();
        var pinned = sources.Where(s => s.DocType != "Convention" && Pin(s))
                            .OrderByDescending(Year).ToList();
        var rest   = sources.Where(s => s.DocType != "Convention" && !Pin(s)).ToList();

        sources.Clear();
        sources.AddRange(convs);
        sources.AddRange(pinned);
        sources.AddRange(rest);
        for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;
    }

    // The FIRST contiguous digit run, not every digit in the string concatenated — see the matching
    // fix + rationale in CaseBrief.cs's RequiredSource.Digits (same bug, same fix, kept in sync).
    // This one is load-bearing for DropOlderEditions (edition dedup) and PinCaseSources (Art.52/53,
    // Art.7 pinning) — both silently no-opped for every multi-part article on taxmindvf, since
    // "ARTICLE 52 (Part 1/37)" concatenated to "52137", never equal to "52".
    internal static string Digits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var start = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsDigit(s[i])) { start = i; break; }
        }
        if (start < 0) return "";
        var end = start;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return s[start..end];
    }

    private static List<LegalSourceDto> MergeAllSources(
        List<LegalSourceDto> plannerSources,
        List<LegalSourceDto> embedSources,
        List<LegalSourceDto> neo4jSources,
        int maxTotal)
    {
        static string Key(LegalSourceDto s) =>
            !string.IsNullOrEmpty(s.ChunkId)
                ? s.ChunkId
                : s.DocName + "|" + s.Text[..Math.Min(s.Text.Length, 60)].Trim();

        var seen   = new HashSet<string>();
        var result = new List<LegalSourceDto>();

        // Planner sources first (highest priority — targeted fetches)
        foreach (var s in plannerSources)
            if (seen.Add(Key(s))) result.Add(s);

        // Then embed (semantic breadth)
        foreach (var s in embedSources)
            if (seen.Add(Key(s))) result.Add(s);

        // Then Neo4j (keyword + graph)
        foreach (var s in neo4jSources)
            if (seen.Add(Key(s))) result.Add(s);

        // Diversity: limit Commentaire to 4, Doctrine to 3
        var com   = result.Where(r => r.DocType == "Commentaire").OrderByDescending(r => r.Score).Take(4).ToList();
        var doc   = result.Where(r => r.DocType == "Doctrine").OrderByDescending(r => r.Score).Take(3).ToList();
        var other = result.Where(r => r.DocType != "Commentaire" && r.DocType != "Doctrine")
                          .OrderBy(r => r.DocType == "Convention" ? 0 : r.DocType == "Code" ? 1 : 2)
                          .ThenByDescending(r => r.Score).ToList();

        var merged = other.Take(maxTotal - com.Count - doc.Count)
                          .Concat(com).Concat(doc).Take(maxTotal).ToList();
        for (int i = 0; i < merged.Count; i++) merged[i].Index = i + 1;
        return merged;
    }

    // ── Article ref correction ────────────────────────────────────────────────

    private static void CorrectArticleRefs(List<LegalSourceDto> sources)
    {
        foreach (var s in sources)
        {
            // Fix 0: Art. 92 in CIRPPIS/CTVA/CDPF is NEVER a real standalone article —
            // confirmed via direct Neo4j inspection that every "Art. 92" tag in these
            // docs is actually a mislabeled Loi de Finances amendment reference (the
            // real Art.92 lives in loi-de-finances-2016, not in these Codes). This is
            // unconditional — it does NOT depend on matching a text pattern, because
            // different mislabeled chunks have different embedded text shapes and a
            // text-pattern check alone misses some of them.
            var refTrimmed = s.ArticleRef?.Trim() ?? "";
            var isArt92 = refTrimmed.Equals("Art. 92", StringComparison.OrdinalIgnoreCase) ||
                          refTrimmed.Equals("Art.92",  StringComparison.OrdinalIgnoreCase) ||
                          refTrimmed.Equals("Article 92", StringComparison.OrdinalIgnoreCase);
            if (isArt92 &&
                (s.DocName.ToLower().Contains("irpp") ||
                 s.DocName.ToLower().Contains("ctva") ||
                 s.DocName.ToLower().Contains("cdpf")))
            {
                if (!s.ArticleRef.Contains("[réf. LF]"))
                    s.ArticleRef = s.ArticleRef.Trim() + " [réf. LF]";
                continue;
            }

            // Fix 1: detect real article number from chunk text start
            var m = Regex.Match(s.Text,
                @"^ARTICLE\s+(\d+[\w\s]*?)\s*[:\n]", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                s.ArticleRef = "Art. " + m.Groups[1].Value.Trim();
                continue;
            }

            // Fix 2: detect "Art XX LF YYYY" pattern — this is a Loi de Finances
            // amendment reference embedded in a Code doc, NOT a standalone code article
            var lfPattern = Regex.Match(
                s.Text[..Math.Min(s.Text.Length, 150)],
                @"Art\.?\s*\d+[\-\d]*\s+LF\s+\d{4}", RegexOptions.IgnoreCase);
            if (lfPattern.Success &&
                (s.DocName.ToLower().Contains("irpp") ||
                 s.DocName.ToLower().Contains("ctva") ||
                 s.DocName.ToLower().Contains("cdpf")))
            {
                // Mark clearly so LLM doesn't cite as a primary code article
                if (!s.ArticleRef.Contains("[réf. LF]"))
                    s.ArticleRef = s.ArticleRef + " [réf. LF]";
                continue;
            }

            // Fix 3: detect article number mentioned in text body
            var bodyMatch = Regex.Match(s.Text,
                @"^Art(?:icle)?\.?\s+(\d{1,3}(?:\s*bis|ter|quater)?)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (bodyMatch.Success && string.IsNullOrEmpty(s.ArticleRef))
                s.ArticleRef = "Art. " + bodyMatch.Groups[1].Value.Trim();
        }
    }

    // ── Citation resolver ─────────────────────────────────────────────────────

    private static string ResolveCitations(string text, List<LegalSourceDto> sources)
    {
        // Safety net: the model must never emit the literal placeholder tokens from the prompt
        // ([Sn], [S…], [S...], [S ]). Strip them (and any adjacent orphan separators) before
        // resolving real [S1]..[Sn] citations to their source labels.
        text = Regex.Replace(text, @"\s*\[S\s*(?:n|…|\.\.\.| )\s*\]", "", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"\[S(\d+)\]", m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx)) return m.Value;
            return sources.FirstOrDefault(s => s.Index == idx)?.Citation ?? m.Value;
        });
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    internal static Dictionary<string, JsonElement>? ParseJsonDict(string raw)
    {
        raw = Regex.Replace(raw.Trim(), @"^```(json)?\s*", "", RegexOptions.Multiline);
        raw = Regex.Replace(raw.Trim(), @"\s*```$",          "", RegexOptions.Multiline);
        var s = raw.IndexOf('{'); var e = raw.LastIndexOf('}');
        if (s >= 0 && e > s) raw = raw[s..(e+1)];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    internal static string GetStr(Dictionary<string, JsonElement>? d, string key) =>
        d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() ?? "" : "";

    private static List<string> GetList(Dictionary<string, JsonElement>? d, string key) =>
        d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray()
           .Where(e => e.ValueKind == JsonValueKind.String)
           .Select(e => e.GetString() ?? "")
           .Where(s => !string.IsNullOrEmpty(s)).ToList()
        : new();

}
