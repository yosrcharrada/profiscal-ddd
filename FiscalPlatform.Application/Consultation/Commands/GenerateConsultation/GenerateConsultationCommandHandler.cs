using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
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
    ILlmAgent                llmAgent,
    IDocumentGenerationAgent docAgent,
    IConsultationRepository  repository,
    ILogger<GenerateConsultationCommandHandler> logger)
    : IRequestHandler<GenerateConsultationCommand, ConsultationGeneratedDto>
{
    // Countries where Note Commune N°2/2015 is superseded by newer convention
    private static readonly HashSet<string> NoteCommune2Exceptions =
        new(StringComparer.OrdinalIgnoreCase) { "allemagne" };

    private const string SystemPrompt =
        "Tu es Faiez Choyakh — fiscaliste tunisien senior, EY Tunisia.\n" +
        "CITATIONS: [S1],[S2]... uniquement. Jamais de document en clair. Jamais inventer un article.\n" +
        "TAUX: tout taux (15%,5%,2.5%...) DOIT citer [Sn]. Jamais de taux de mémoire.\n" +
        "ART.92 CIRPPIS: si source contient 'Art 92-X LF' = référence LF, pas article autonome.\n" +
        "PRESTATAIRE ÉTRANGER — séquence obligatoire:\n" +
        "  1. ES: analyser risque établissement stable (durée, présence, lieu fixe).\n" +
        "     Si ES → taux IS. Si pas ES → étape 2.\n" +
        "  2. Redevance: service = redevance selon Art.12 convention? (définition propre à chaque conv.)\n" +
        "     Si oui → taux convention. Si non → Art.52 CIRPPIS.\n" +
        "  3. TVA: toujours analyser.\n" +
        "CONVENTION: Art.5=ES, Art.7=bénéfices, Art.10=dividendes, Art.11=intérêts,\n" +
        "  Art.12=redevances, Art.14=prof.indép., Art.15=salaires.\n" +
        "NOTE COMMUNE N°2/2015: utiliser Annexe 1 pour taux par pays (sauf Allemagne).\n" +
        "HIÉRARCHIE: International: Convention→Codes→LdF→Doctrine. Local: Codes→LdF→Doctrine.\n" +
        "ÉTENDUE: UNIQUEMENT ce que le client demande. ZÉRO ajout.\n" +
        "VERDICTS: OUI/NON/X%/EXONÉRÉ/SOUMIS. NON DOCUMENTÉ si aucune source.\n" +
        "JSON PUR UNIQUEMENT.";

    // ── Timing table ──────────────────────────────────────────────────────────
    private sealed record TimingEntry(string Step, double Ms, string Notes);

    private void LogTimingTable(List<TimingEntry> timings, string reference)
    {
        const string sep = "╠══════════════════════════════╬═════════════════════════╬═══════════════════════════════╣";
        const string top = "╔══════════════════════════════╦═════════════════════════╦═══════════════════════════════╗";
        const string bot = "╚══════════════════════════════╩═════════════════════════╩═══════════════════════════════╝";
        const string hdr = "║  Step                        ║  Duration               ║  Notes                        ║";
        logger.LogInformation(top);
        logger.LogInformation("║  TIMING — {Ref}", reference);
        logger.LogInformation(sep); logger.LogInformation(hdr); logger.LogInformation(sep);
        foreach (var t in timings.Where(x => x.Step != "TOTAL"))
        {
            var step  = t.Step.Length  > 28 ? t.Step[..28]  : t.Step.PadRight(28);
            var notes = t.Notes.Length > 29 ? t.Notes[..29] : t.Notes.PadRight(29);
            var dur   = $"{t.Ms:F0}ms / {t.Ms / 60000.0:F2}min".PadRight(23);
            logger.LogInformation("║  {S}  ║  {D}  ║  {N}  ║", step, dur, notes);
        }
        var total = timings.FirstOrDefault(x => x.Step == "TOTAL");
        if (total is not null)
        {
            logger.LogInformation(sep);
            var dur = $"{total.Ms:F0}ms / {total.Ms / 60000.0:F2}min".PadRight(23);
            logger.LogInformation("║  {S}  ║  {D}  ║  {N}  ║",
                "TOTAL".PadRight(28), dur, total.Notes.PadRight(29));
        }
        logger.LogInformation(bot);
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

        // ── Step 4b: Merge all sources ────────────────────────────────────────
        // Priority: Planner sources (targeted) > Embed > Neo4j
        var sources = MergeAllSources(plan.Sources, filteredEmbed, neo4jSources, 30);
        CorrectArticleRefs(sources);

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

        // ── Step 6: LLM Phase 2 + 3 — PARALLEL ───────────────────────────────
        logger.LogInformation("┌─ [PHASE 2+3] analyses ‖ table — parallel…");
        var sw6   = Stopwatch.StartNew();
        var task2 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase2Prompt(cmd, sources, etendueItems, sommaire, contexteFaits, isIntl, branches, plan),
            "Phase2", 3800, ct);
        var task3 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase3Prompt(cmd, sources, etendueItems),
            "Phase3", 3500, ct);
        await Task.WhenAll(task2, task3);

        if (task2.Result is null)
        {
            sw6.Stop();
            timings.Add(new("6. LLM Phase 2+3", sw6.Elapsed.TotalMilliseconds, "PHASE 2 FAILED"));
            throw new ConsultationGenerationException("LLM Phase 2 returned null");
        }

        var p2    = ParseJsonDict(task2.Result) ?? new Dictionary<string, JsonElement>();
        var p3    = task3.Result is not null ? ParseJsonDict(task3.Result) : null;
        var table = ParseTable(p3);

        if (task3.Result is null)
            logger.LogWarning("│  [PHASE 3] returned null — table will be empty");

        sw6.Stop();
        timings.Add(new("6. LLM Phase 2+3 (‖)", sw6.Elapsed.TotalMilliseconds,
            $"table={table.Count}/{etendueItems.Count}"));
        logger.LogInformation("└─ [PHASE 2+3] ✓ ({Ms:F0}ms / {Min:F2}min) | table={T}/{N}",
            sw6.Elapsed.TotalMilliseconds, sw6.Elapsed.TotalMinutes,
            table.Count, etendueItems.Count);

        // ── Step 7: Build output ──────────────────────────────────────────────
        string R(string t) => ResolveCitations(t, sources);
        var output = new ConsultationOutput
        {
            ContexteFaits   = GetStr(p1, "contexte_faits"),
            Etendue         = GetStr(p1, "etendue"),
            Abbreviations   = GetStr(p1, "abbreviations").Trim(),
            SommairExecutif = R(sommaire),
            Analyses        = R(GetStr(p2, "analyses")),
            Documents       = R(p3 is not null ? GetStr(p3, "documents") : ""),
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

    private static string SourcesBlock(List<LegalSourceDto> sources)
    {
        var sb = new StringBuilder("== SOURCES JURIDIQUES ==\n\n");
        foreach (var s in sources.Take(18))
        {
            var label   = s.IsExpert ? "COMMENTAIRE — Faiez Choyakh" : s.DocType;
            var preview = s.Text.Length > 300 ? s.Text[..300] + "…" : s.Text;
            sb.AppendLine($"[S{s.Index}] {label} | {s.DocName} | {s.Year} | {s.ArticleRef}");
            sb.AppendLine($"       {preview}\n");
        }
        sb.AppendLine($"!! Cite UNIQUEMENT [S1]..[S{Math.Min(sources.Count, 18)}].");
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

        var plannerContext =
            $"[ANALYSE PRÉLIMINAIRE DU PLANNER]\n" +
            $"Type de revenu identifié: {plan.IncomeType}\n" +
            $"Risque ES: {(plan.EsRiskPossible ? "OUI — analyser obligatoirement" : "faible")}\n" +
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
            "- etendue_items: UNIQUEMENT les points demandés explicitement par le client. ZÉRO ajout.\n" +
            "- contexte_faits: faits purs, ZÉRO citation. Début: \"Nous comprenons que :\"\n" +
            "- etendue: section 1.2. DOIT commencer par \"Notre analyse portera sur\" suivi des " +
            "points listés. INTERDICTION ABSOLUE de \"Analyse limitée à\" ou de toute phrase " +
            "disant que d'autres aspects ne sont pas couverts (pas de \"Aucun autre aspect... " +
            "n'est couvert\").\n" +
            "- abbreviations: SIGLE : Définition\n" +
            "- sommaire_executif: verdicts concis, max 1 [Sn] par point, tout taux justifié.\n" +
            "- pays_non_resident: pays de résidence de la partie étrangère (ex: france, maroc). " +
            "Identifier même si non mentionné explicitement (nom de société, groupe, devise). " +
            "Vide si transaction purement tunisienne.\n\n" +
            "{\"etendue_items\":[],\"contexte_faits\":\"\",\"etendue\":\"\"," +
            "\"abbreviations\":\"\",\"sommaire_executif\":\"\",\"pays_non_resident\":\"\"}";
    }

    private static string BuildPhase2Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, List<string> etendueItems, string sommaire,
        string contexteFaits, bool isIntl, HashSet<string> branches, RetrievalPlan plan)
    {
        var n  = etendueItems.Count;
        var et = string.Join("\n", etendueItems.Select((x, i) => $"  {i+1}. {x}"));

        var bg = new StringBuilder();
        if (isIntl || plan.EsRiskPossible)
        {
            bg.AppendLine("  CAS INTERNATIONAL — SÉQUENCE OBLIGATOIRE PAR POINT:");
            bg.AppendLine("  1. ES — DEUX tests distincts à trancher séparément, chacun avec un verdict explicite:");
            bg.AppendLine("     a) Présence directe du prestataire étranger en Tunisie (lieu fixe d'affaires, " +
                          "personnel propre stationné, durée de présence) → conclure ES propre OUI/NON.");
            bg.AppendLine("     b) Si le prestataire étranger est actionnaire/associé/société mère du client " +
                          "tunisien : RAPPELER EXPLICITEMENT que le simple lien de contrôle ou d'actionnariat " +
                          "NE constitue PAS en soi un ES (le client tunisien n'est pas automatiquement un ES de " +
                          "sa société mère étrangère), SAUF si les locaux du client tunisien sont mis à la " +
                          "disposition du prestataire étranger pour l'exercice de SA PROPRE activité, ou si le " +
                          "client tunisien agit comme agent dépendant concluant habituellement des contrats au " +
                          "nom du prestataire étranger. Conclure explicitement OUI/NON sur ce second test aussi.");
            bg.AppendLine("  2. Si AUCUN ES (les deux tests négatifs) : service = redevance ? " +
                         $"(Convention Art.12 — type identifié: {plan.IncomeType}) → trancher OUI/NON.");
            bg.AppendLine("  3. TVA : analyser applicabilité et trancher.");
        }
        if (plan.NoteCommune2Used)
            bg.AppendLine("  Note Commune N°2/2015 disponible → utiliser ses tableaux Annexe 1 pour taux par pays");
        if (branches.Contains("IS"))
            bg.AppendLine("  IS → citer Art.45/47 CIRPPIS (personnes morales, bénéfices passibles)");
        if (branches.Contains("TVA"))
            bg.AppendLine("  TVA → citer CTVA (soumises, affaires, activités en Tunisie)");
        if (branches.Contains("IRPP"))
            bg.AppendLine("  IRPP → citer CIRPPIS (revenu, personne physique)");
        if (branches.Contains("Retenue"))
            bg.AppendLine("  Retenue → CIRPPIS Art.52 + convention si intl");
        if (branches.Contains("PrixTransfert"))
            bg.AppendLine("  PrixTransfert → Art.48 septies CIRPPIS + CDPF 17 bis/ter");

        return
            $"PHASE 2 — JSON avec 1 clé: analyses.\n\n" +
            $"Client : {cmd.ClientName} | Question : {cmd.FiscalQuestion}\n\n" +
            $"FAITS ÉTABLIS EN SECTION 1.1 (à utiliser pour trancher, ne pas re-discuter en \"Si...\"):\n" +
            $"{contexteFaits}\n\n" +
            $"ÉTENDUE ({n} points):\n{et}\n\n" +
            SourcesBlock(sources) +
            $"\nORDRE: {(isIntl ? "Convention → Codes → LdF → Doctrine" : "Codes → LdF → Doctrine")}\n" +
            bg +
            $"\n═══ RÈGLE ABSOLUE — PRISE DE POSITION OBLIGATOIRE ═══\n" +
            "INTERDIT d'écrire \"Si X alors Y, Si A alors B\" comme contenu de \"Application au cas\" " +
            "ou \"Conclusion\". C'est un générateur de scénarios génériques, PAS une analyse de CE cas. " +
            "Tu DOIS: (1) lire les FAITS ÉTABLIS ci-dessus, (2) déclarer explicitement quel scénario " +
            "s'applique à CE client en citant le fait précis qui le détermine " +
            "(ex: \"Le fait établi que [citer] permet de déterminer que...\"), (3) dérouler l'analyse " +
            "UNIQUEMENT pour le scénario retenu, (4) donner UN SEUL verdict en conclusion — jamais " +
            "plusieurs verdicts conditionnels. NON DOCUMENTÉ s'applique UNIQUEMENT à un sous-point " +
            "réellement indéterminé (ex: taux exact si le pays n'est pas connu) — cela ne doit JAMAIS " +
            "transformer l'ensemble de la conclusion en liste de scénarios.\n\n" +
            $"FORMAT {n} blocs 4.1 à 4.{n}:\n" +
            "  4.X [Titre]\n" +
            "  Principe applicable : [Sn] : \"citation exacte du texte\".\n" +
            "  Détermination : sur la base du fait établi [citer], le scénario applicable à ce cas est... " +
            "(une seule phrase déclarative, pas de \"si\").\n" +
            "  Application au cas : dérouler l'analyse pour CE scénario uniquement.\n" +
            "  Conclusion : UN SEUL VERDICT — justification avec taux cité depuis [Sn]. " +
            "Si un sous-point précis (ex: taux exact) est indéterminé, le signaler avec NON DOCUMENTÉ " +
            "sans remettre en cause le reste du verdict.\n\n" +
            "[Sn] OBLIGATOIRE par bloc. Tout taux doit citer sa source [Sn].\n\n" +
            "{\"analyses\":\"4. ANALYSES\\n\\n[blocs]\"}";
    }

    private static string BuildPhase3Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, List<string> etendueItems)
    {
        var lst = string.Join("\n", sources.Take(18)
            .Select(s => $"  [S{s.Index}] {s.DocType} | {s.DocName} ({s.Year}) — {s.ArticleRef}"));
        return
            $"PHASE 3 — JSON: documents + analysis_table ({etendueItems.Count} objets).\n\n" +
            $"Client: {cmd.ClientName}\nSOURCES:\n{lst}\n\n" +
            "{\"documents\":\"5. RÉFÉRENCES\\n\\n[sources citées]\"," +
            "\"analysis_table\":[{\"sujet\":\"\",\"analyse\":\"Selon [Sn]: \",\"conclusion\":\"OUI/NON\"}]}";
    }

    // ── Source merging ────────────────────────────────────────────────────────

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

    private static string ResolveCitations(string text, List<LegalSourceDto> sources) =>
        Regex.Replace(text, @"\[S(\d+)\]", m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx)) return m.Value;
            return sources.FirstOrDefault(s => s.Index == idx)?.Citation ?? m.Value;
        });

    // ── JSON helpers ──────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement>? ParseJsonDict(string raw)
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

    private static string GetStr(Dictionary<string, JsonElement>? d, string key) =>
        d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() ?? "" : "";

    private static List<string> GetList(Dictionary<string, JsonElement>? d, string key) =>
        d is not null && d.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray()
           .Where(e => e.ValueKind == JsonValueKind.String)
           .Select(e => e.GetString() ?? "")
           .Where(s => !string.IsNullOrEmpty(s)).ToList()
        : new();

    private static List<(string Sujet, string Analyse, string Conclusion)> ParseTable(
        Dictionary<string, JsonElement>? p3)
    {
        if (p3 is null || !p3.TryGetValue("analysis_table", out var tbl)
            || tbl.ValueKind != JsonValueKind.Array) return new();
        return tbl.EnumerateArray()
            .Select(r => (GetElStr(r,"sujet"), GetElStr(r,"analyse"), GetElStr(r,"conclusion")))
            .ToList();
    }

    private static string GetElStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() ?? "" : "";
}
