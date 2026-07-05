using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Agents;
using FiscalPlatform.Application.Consultation.Playbooks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using H = FiscalPlatform.Application.Consultation.Commands.GenerateConsultation.GenerateConsultationCommandHandler;

namespace FiscalPlatform.Application.Consultation.Orchestration;

/// <summary>
/// The consultation ORCHESTRATOR — a Microsoft Agent Framework state graph that owns the whole
/// agentic flow. The handler prepares the understood state (detection + Phase 1), then this graph
/// runs:
///
///   [1] Qualify ─► [2] CaseBrief ─► [3] Fulfil ─► [4] Completeness ──(missing, ≤N)──► [3]
///                                                        │ok
///                                                        ▼
///                                   [5] Writer ─► [6] Judge ──(quality)──► [5]
///                                                        │      └─(missing source)──► [3]
///                                                        ▼ accept / bounds exhausted
///                                   [7] ExpertVoice (hard guards) ─► [8] Finalize (table) ─► output
///
/// Design rules: the graph SHAPE is fixed (auditable, reproducible); autonomy lives inside nodes;
/// every loop is bounded by counters in <see cref="ConsultationState"/>; no node ever injects a
/// rate or verdict — figures are read by the model from the retrieved sources.
/// </summary>
public sealed class ConsultationWorkflow(
    ILlmAgent llm,
    IRetrievalAgent retrieval,
    IRetrievalPlannerAgent planner,
    IAcceptanceAgent acceptance,
    IEnumerable<ICaseAgent> caseAgents,
    ILogger<ConsultationWorkflow> logger)
{
    // ── Public entry: build the graph, run it, return the final state ─────────
    public async Task<ConsultationState> RunAsync(ConsultationState state, CancellationToken ct = default)
    {
        var qualify      = Bind<ConsultationState, ConsultationState>(QualifyAsync,      "Qualify");
        var brief        = Bind<ConsultationState, ConsultationState>(BriefAsync,        "CaseBrief");
        var fulfil       = Bind<ConsultationState, ConsultationState>(FulfilAsync,       "FulfilRetrieval");
        var completeness = Bind<ConsultationState, ConsultationState>(CompletenessAsync, "Completeness");
        var writer       = Bind<ConsultationState, ConsultationState>(WriteAsync,        "Writer");
        var judge        = Bind<ConsultationState, ConsultationState>(JudgeAsync,        "Judge");
        var expert       = Bind<ConsultationState, ConsultationState>(ExpertAsync,       "ExpertVoice");
        var finalize     = ExecutorBindingExtensions.BindAsExecutor<ConsultationState>(
            FinalizeAsync, "Finalize", options: null, threadsafe: false);

        var workflow = new WorkflowBuilder(qualify)
            .WithName("ConsultationGraph")
            .AddEdge(qualify, brief)
            .AddEdge(brief, fulfil)
            .AddEdge(fulfil, completeness)
            .AddEdge(completeness, fulfil, (ConsultationState? s) => s?.NeedsRetrievalRetry == true)
            .AddEdge(completeness, writer, (ConsultationState? s) => s?.NeedsRetrievalRetry != true)
            .AddEdge(writer, judge)
            .AddEdge(judge, fulfil, (ConsultationState? s) => s?.NeedsJudgeRetrieval == true)
            .AddEdge(judge, writer, (ConsultationState? s) => s?.NeedsWriterRevision == true)
            .AddEdge(judge, expert, (ConsultationState? s) => s?.ProceedToExpert == true)
            .AddEdge(expert, finalize)
            .WithOutputFrom(finalize)
            .Build(validateOrphans: false);

        ConsultationState? final = null;
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, state, cancellationToken: ct);
        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case WorkflowOutputEvent output when output.Is<ConsultationState>(out var s) && s is not null:
                    final = s;
                    break;
                case WorkflowErrorEvent err:
                    logger.LogError("[GRAPH] workflow error: {E}", err);
                    break;
                case ExecutorFailedEvent fail:
                    logger.LogError("[GRAPH] executor failed: {E}", fail);
                    break;
            }
        }
        // The state object is mutated in place as it flows the edges, so even if the output event
        // were missed the original reference carries the result.
        return final ?? state;
    }

    private static ExecutorBinding Bind<TIn, TOut>(
        Func<TIn, IWorkflowContext, CancellationToken, ValueTask<TOut>> handler, string id) =>
        ExecutorBindingExtensions.BindAsExecutor(handler, id, options: null, threadsafe: false);

    // ── [1] Qualify: LLM-primary income classification + deterministic contradiction guard ──
    private async ValueTask<ConsultationState> QualifyAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw  = Stopwatch.StartNew();
        var cmd = state.Command;
        var et  = string.Join("\n", state.EtendueItems.Select((x, i) => $"  {i + 1}. {x}"));
        var sys =
            "Tu es un fiscaliste tunisien senior. Tu CLASSES la nature du revenu d'une consultation, " +
            "à partir de l'ÉTENDUE des travaux et des faits. Réponds UNIQUEMENT en JSON:\n" +
            "{\"case_type\":\"rs_service_foreign|rs_service_local|dividende|interet|redevance|autre\",\"rationale\":\"...\"}\n" +
            "- dividende: distribution de bénéfices / dividendes à un actionnaire (société mère, associé…).\n" +
            "- interet: intérêts d'un prêt / d'une créance.\n" +
            "- redevance: usage d'un droit, marque, brevet, logiciel, savoir-faire, licence.\n" +
            "- rs_service_foreign: prestation de services rendue par un fournisseur ÉTRANGER non résident (étude, " +
            "engineering, assistance technique, conseil…).\n" +
            "- rs_service_local: prestation de services entre deux entités TOUTES DEUX résidentes/établies " +
            "en Tunisie (assistance administrative, juridique, comptable, management fees intra-groupe local…).\n" +
            "- autre: tout le reste (cas non couvert).\n" +
            "Choisis la catégorie DOMINANTE de la question posée.";
        var user = $"ÉTENDUE:\n{et}\n\nFAITS:\n{state.ContexteFaits}\n\nQUESTION: {cmd.FiscalQuestion}\n\n" +
                   $"(indice préliminaire du planner: {state.Plan.IncomeType})\nClasse. JSON.";

        var llmType = CaseType.Generic;
        try
        {
            var raw    = await llm.CompleteAsync(sys, user, "Qualifier", 300, ct);
            var parsed = raw is not null ? H.ParseJsonDict(raw) : null;
            var label  = (parsed is not null ? H.GetStr(parsed, "case_type") : "").Trim().ToLowerInvariant();
            llmType = label switch
            {
                "dividende"          => CaseType.Dividende,
                "interet"            => CaseType.Interet,
                "redevance"          => CaseType.Redevance,
                "rs_service_foreign" => CaseType.RsServiceForeign,
                "rs_service_local"   => CaseType.RsServiceLocal,
                _                    => CaseType.Generic,
            };
        }
        catch (Exception ex) { logger.LogWarning(ex, "[QUALIFY] failed — Generic"); }

        // Deterministic contradiction guard: only for qualifications whose démarche diverges hard
        // from the legacy flow — the LLM decides, determinism vetoes the obviously unsupported.
        var hay = ((cmd.Situation ?? "") + " " + (cmd.FiscalQuestion ?? "") + " " + state.ContexteFaits
                   + " " + string.Join(" ", state.EtendueItems)).ToLowerInvariant();
        bool Cue(params string[] terms) => terms.Any(hay.Contains);
        state.CaseType = llmType switch
        {
            CaseType.Dividende when !Cue("dividend", "distribu", "actionnaire", "société mère",
                "societe mere", "filiale", "participation", "associé", "associe", "bénéfices dist",
                "benefices dist") => CaseType.Generic,
            CaseType.Interet when !Cue("intérêt", "interet", "prêt", "pret", "créance", "creance",
                "emprunt", "coupon") => CaseType.Generic,
            CaseType.Redevance when !Cue("redevance", "royalt", "licence", "marque", "brevet",
                "logiciel", "savoir-faire", "savoir faire", "droit d'usage") => CaseType.Generic,
            // Domestic only when NO foreign country was detected anywhere — a detected country means
            // a foreign party is involved and the international flow (with its treaty logic) must run.
            CaseType.RsServiceLocal when state.Countries.Count > 0 || state.IsInternational
                => CaseType.RsServiceForeign,
            _ => llmType,
        };

        sw.Stop();
        state.Timings.Add(("W1. Qualify", sw.Elapsed.TotalMilliseconds, state.CaseType.ToString()));
        logger.LogInformation("► [GRAPH:Qualify] case = {T}", state.CaseType);
        return state;
    }

    // ── [2] Case brief: the selected case agent declares WHAT this case needs ──
    private ValueTask<ConsultationState> BriefAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var agent = Agent(state.CaseType);
        state.Brief = agent.BuildBrief(state);
        state.Timings.Add(("W2. Case brief", 0,
            $"{state.Brief.RequiredSources.Count} required src"));
        logger.LogInformation("► [GRAPH:Brief] {L} — checklist: {N} items",
            state.Brief.Label, state.Brief.RequiredSources.Count);
        return ValueTask.FromResult(state);
    }

    // ── [3] RetrievalPlannerAgent — CASE-AWARE fulfilment of the brief ────────
    // The case agent's brief goes STRAIGHT to the autonomous ReAct planner: it receives the
    // still-missing checklist items as its mission, reasons about where they live, and hunts them
    // with its own tools. The deterministic métier fetchers act only as a SAFETY NET inside the
    // node — (a) if the planner call itself fails (LLM outage), and (b) as a final sweep for any
    // item the planner missed — so the completeness guarantee survives the planner's autonomy.
    private async ValueTask<ConsultationState> FulfilAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw    = Stopwatch.StartNew();
        var brief = state.Brief!;
        var agent = Agent(state.CaseType);
        state.RetrievalLoops++;

        var report  = agent.VerifyCompleteness(brief, state.Sources, state.Countries);
        var before  = state.Sources.Count;
        var hasDraft = !string.IsNullOrEmpty(state.Analyses);
        var plannerOk = false;

        if (report.Missing.Count > 0 || state.JudgeMissingTopics.Count > 0)
        {
            // The MISSION: the brief's missing items (+ anything the judge flagged), described in
            // métier terms. The planner decides which of its tools to call, and how.
            var items = report.Missing.Select(m => $"- {m.Description}")
                .Concat(state.JudgeMissingTopics.Select(t => $"- {t}"))
                .ToList();
            try
            {
                logger.LogInformation("► [GRAPH:Fulfil #{L}] planner mission — {N} item(s): {M}",
                    state.RetrievalLoops, items.Count,
                    string.Join(" | ", report.Missing.Select(m => m.Key)));
                var mission = await planner.PlanAndRetrieveAsync(
                    situation:
                        $"CAS QUALIFIÉ : {brief.Label}.\n" +
                        $"FAITS : {state.ContexteFaits}",
                    fiscalQuestion:
                        "MISSION DE RECHERCHE CIBLÉE — retrouve PRÉCISÉMENT les sources juridiques " +
                        "suivantes, requises pour ce cas (utilise tes outils, reformule si nécessaire ; " +
                        "pour les conventions cherche l'article PAR SUJET, jamais par numéro) :\n" +
                        string.Join("\n", items),
                    state.Branches, state.Countries, state.IsInternational, ct);
                AddDeduped(state.Sources, mission?.Sources ?? new List<LegalSourceDto>());
                plannerOk = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[GRAPH:Fulfil] planner failed — deterministic net takes over");
            }

            // SAFETY NET: deterministic métier fetchers (year preference, Arabic exclusion,
            // treaty-by-subject) for whatever the planner did not satisfy — or everything, if the
            // planner call failed outright. Runs AFTER the planner; never replaces it.
            var still = agent.VerifyCompleteness(brief, state.Sources, state.Countries);
            foreach (var item in (plannerOk ? still.Missing : report.Missing).Take(8))
            {
                try   { AddDeduped(state.Sources, await FetchForAsync(item, brief, state, ct)); }
                catch (Exception ex) { logger.LogWarning(ex, "[GRAPH:Fulfil] net item {K}", item.Key); }
            }
        }
        state.JudgeMissingTopics.Clear();

        // Citation stability rule (hard-won): BEFORE any draft exists we may curate + fully re-index;
        // AFTER a draft exists we only APPEND — existing [Sn] must keep resolving to the same source.
        if (!hasDraft)
        {
            // (1) MÉTIER: a convention for the WRONG country is strictly worse than no convention —
            //     purge any treaty source that doesn't match a detected country.
            if (state.Countries.Count > 0)
                state.Sources.RemoveAll(s =>
                    string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase) &&
                    !state.Countries.Any(c => (s.DocName ?? "").Contains(c, StringComparison.OrdinalIgnoreCase)));
            else
                state.Sources.RemoveAll(s =>
                    string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase));

            // (2) MÉTIER: cite codes in their single MOST RECENT edition — drop older-year duplicates
            //     of the same article (keeps all parts of the newest edition; NC/conventions exempt).
            DropOlderEditions(state.Sources);

            PinCaseSources(state.Sources, state.Countries, brief);
            for (int i = 0; i < state.Sources.Count; i++) state.Sources[i].Index = i + 1;
        }
        else
        {
            for (int i = 0; i < state.Sources.Count; i++)
                if (state.Sources[i].Index <= 0) state.Sources[i].Index = i + 1;
            for (int i = before; i < state.Sources.Count; i++) state.Sources[i].Index = i + 1;
        }

        sw.Stop();
        state.Timings.Add(($"W3. Fulfil #{state.RetrievalLoops}", sw.Elapsed.TotalMilliseconds,
            $"+{state.Sources.Count - before} src, {report.Missing.Count} asked"));
        logger.LogInformation("► [GRAPH:Fulfil #{L}] +{N} sources ({M} items missing before)",
            state.RetrievalLoops, state.Sources.Count - before, report.Missing.Count);
        return state;
    }

    private async Task<List<LegalSourceDto>> FetchForAsync(
        RequiredSource item, CaseBrief brief, ConsultationState state, CancellationToken ct)
    {
        if (item.Key == "nc2_2015")
            return await retrieval.FetchNoteCommune2Async(state.Countries.FirstOrDefault(), ct) ?? new();

        if (item.ConventionSubject is { Length: > 0 })
        {
            var all = new List<LegalSourceDto>();
            foreach (var country in state.Countries.Where(c => !string.IsNullOrWhiteSpace(c)).Take(2))
                all.AddRange(await retrieval.FetchBySubjectAsync(
                    "conv_" + country, item.ConventionSubject, brief.Topics,
                    item.ConventionSubject.Select(s => s.ToLowerInvariant()).ToArray(), ct) ?? new());
            return all;
        }

        // LINE-PRECISE: the item's own predicates (TextContains / RequirePercent) drive the part
        // selection — on the part-split graph the writer receives the article header plus ONLY the
        // alinéas this case needs (with NEXT_PART neighbours for straddles), never the whole menu.
        if (item.ArticleNumber is not null)
        {
            var lines = await retrieval.FetchArticleLinesAsync(
                item.FetchDocFragment ?? item.DocFragment ?? "",
                item.ArticleNumber, item.TextContains, item.RequirePercent, ct) ?? new();
            if (lines.Count > 0) return lines;
            // fallback: the broader by-number fetch (older editions, display-based matches)
            return await retrieval.FetchTargetedAsync(
                item.FetchDocFragment ?? item.DocFragment ?? "",
                new[] { item.ArticleNumber }, item.FetchKeywords ?? Array.Empty<string>(), ct) ?? new();
        }

        var kws = item.FetchKeywords ?? (item.TextContains is not null
            ? new[] { item.TextContains } : Array.Empty<string>());
        if (kws.Length == 0) return new();
        return await retrieval.FetchTargetedAsync(item.FetchDocFragment ?? "", Array.Empty<string>(), kws, ct) ?? new();
    }

    private static void AddDeduped(List<LegalSourceDto> sources, List<LegalSourceDto> hits)
    {
        var seen = new HashSet<string>(sources.Select(s => s.ChunkId).Where(id => !string.IsNullOrEmpty(id)));
        foreach (var h in hits.Where(h => h is not null &&
                     (string.IsNullOrEmpty(h.ChunkId) || seen.Add(h.ChunkId))))
            sources.Add(h);
    }

    // MÉTIER: a code article is cited in its single most recent edition. For each (code family,
    // article) present in several editions, drop the older years — UNLESS the newest edition carries
    // no rate ('%') while an older one does (never trade the figure away for recency). NC, LF and
    // convention docs are exempt (they are unique documents, not yearly editions).
    internal static void DropOlderEditions(List<LegalSourceDto> sources)
    {
        static string Family(string name) => Regex.Replace(name ?? "", @"_(19|20)\d{2}.*$", "");
        static int YearOf(LegalSourceDto s)
        {
            var m = Regex.Match((s.DocName ?? "") + " " + (s.Year ?? ""), @"(19|20)\d{2}");
            return m.Success ? int.Parse(m.Value) : 0;
        }

        var codeGroups = sources
            .Where(s => (s.DocName ?? "").StartsWith("code_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => Family(s.DocName) + "|" + H.Digits(s.ArticleRef))
            .Where(g => g.Select(YearOf).Distinct().Count() > 1);

        var toDrop = new HashSet<LegalSourceDto>();
        foreach (var g in codeGroups)
        {
            var newestYear   = g.Max(YearOf);
            var newestHasPct = g.Where(s => YearOf(s) == newestYear).Any(s => (s.Text ?? "").Contains('%'));
            var anyHasPct    = g.Any(s => (s.Text ?? "").Contains('%'));
            foreach (var s in g)
            {
                if (YearOf(s) == newestYear) continue;
                // keep an older %-bearing copy only when the newest edition lost the figure
                if (!newestHasPct && anyHasPct && (s.Text ?? "").Contains('%')) continue;
                toDrop.Add(s);
            }
        }
        if (toDrop.Count > 0) sources.RemoveAll(toDrop.Contains);
    }

    // Pin the decisive sources to the front of the visible window: treaty income articles matching
    // the brief's subjects, then the %-bearing CIRPPIS 52/53 and CTVA 7 (newest year first).
    private static void PinCaseSources(
        List<LegalSourceDto> sources, ICollection<string> countries, CaseBrief brief)
    {
        var subjects = brief.RequiredSources
            .Where(r => r.ConventionSubject is { Length: > 0 })
            .SelectMany(r => r.ConventionSubject!).Distinct().ToArray();

        bool IsTreaty(LegalSourceDto s)
        {
            if (subjects.Length == 0) return false;
            if (!string.Equals(s.DocType, "Convention", StringComparison.OrdinalIgnoreCase)) return false;
            var head = (s.Text ?? "")[..Math.Min((s.Text ?? "").Length, 80)];
            if (!subjects.Any(v => head.Contains(v, StringComparison.OrdinalIgnoreCase))) return false;
            return countries.Count == 0 ||
                   countries.Any(c => (s.DocName ?? "").Contains(c, StringComparison.OrdinalIgnoreCase));
        }

        bool IsDomesticRate(LegalSourceDto s) =>
            ((s.DocName ?? "").Contains("code_irpp_is", StringComparison.OrdinalIgnoreCase) &&
             (H.Digits(s.ArticleRef) is "52" or "53") && (s.Text ?? "").Contains('%'))
            ||
            ((s.DocName ?? "").Contains("code_tva", StringComparison.OrdinalIgnoreCase) &&
             H.Digits(s.ArticleRef) == "7" && (s.Text ?? "").Contains('%'));

        static int Year(LegalSourceDto s)
        {
            var m = Regex.Match((s.DocName ?? "") + " " + (s.Year ?? ""), @"(19|20)\d{2}");
            return m.Success ? int.Parse(m.Value) : 0;
        }

        var treaty   = sources.Where(IsTreaty).ToList();
        var domestic = sources.Where(s => !IsTreaty(s) && IsDomesticRate(s)).OrderByDescending(Year).ToList();
        var rest     = sources.Where(s => !IsTreaty(s) && !IsDomesticRate(s)).ToList();

        sources.Clear();
        sources.AddRange(treaty);
        sources.AddRange(domestic);
        sources.AddRange(rest);
    }

    // ── [4] Completeness: deterministic checklist verification (no LLM, free, reproducible) ──
    private ValueTask<ConsultationState> CompletenessAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var agent  = Agent(state.CaseType);
        var report = agent.VerifyCompleteness(state.Brief!, state.Sources, state.Countries);

        // Existence-conditional items (treaty articles): after the loop has genuinely tried, their
        // absence becomes a legal FACT (no convention with that country → droit commun applies), not
        // a retrieval failure to chase forever. Stop them from blocking completeness.
        if (state.RetrievalLoops >= 2 && report.MissingCritical.Any(m => m.ExistenceConditional))
        {
            var stillCritical = report.MissingCritical.Where(m => !m.ExistenceConditional).ToList();
            foreach (var m in report.MissingCritical.Where(m => m.ExistenceConditional))
                logger.LogInformation(
                    "► [GRAPH:Completeness] {K} introuvable après {L} tentatives — traité comme " +
                    "INEXISTANT (pas de convention → droit commun)", m.Key, state.RetrievalLoops);
            report = report with { MissingCritical = stillCritical };
        }
        state.Completeness = report;
        var r = state.Completeness;
        state.Timings.Add(("W4. Completeness", 0,
            $"{r.TotalRequired - r.Missing.Count}/{r.TotalRequired} ok, crit missing={r.MissingCritical.Count}"));
        if (!r.CriticallyComplete)
            logger.LogWarning("► [GRAPH:Completeness] MISSING critical: {M} (loop {L}/{Max})",
                string.Join(" | ", r.MissingCritical.Select(m => m.Key)),
                state.RetrievalLoops, ConsultationState.MaxRetrievalLoops);
        else
            logger.LogInformation("► [GRAPH:Completeness] ✓ {N}/{T} satisfied",
                r.TotalRequired - r.Missing.Count, r.TotalRequired);
        return ValueTask.FromResult(state);
    }

    // ── [5] Writer: drafts the analyses from the BRIEF + verified sources + universal style card ──
    private async ValueTask<ConsultationState> WriteAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        if (state.WriterLoops >= 3) return state;   // absolute hard stop, belt over the edge bounds
        var sw = Stopwatch.StartNew();
        state.WriterLoops++;
        var brief = state.Brief!;

        var prompt = brief.UsesOwnPrompt
            ? BuildBriefPrompt(state, brief)
            : H.BuildPhase2Prompt(state.Command, state.Sources, state.EtendueItems, state.Sommaire,
                                  state.ContexteFaits, state.IsInternational, state.Branches, state.Plan);

        if (!string.IsNullOrWhiteSpace(state.JudgeGuidance) && state.WriterLoops > 1)
            prompt += "\n\n═══ CORRECTIONS DEMANDÉES (relecture qualité) ═══\n" + state.JudgeGuidance +
                      "\nCorrige ces points en conservant strictement le format et le niveau de détail demandé.";

        var raw = await llm.CompleteAsync(brief.SystemPrompt, prompt,
            state.WriterLoops == 1 ? "Writer" : "Writer-Revise", 3800, ct);
        var analyses = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");
        if (!string.IsNullOrWhiteSpace(analyses)) state.Analyses = analyses;

        sw.Stop();
        state.Timings.Add(($"W5. Writer #{state.WriterLoops}", sw.Elapsed.TotalMilliseconds,
            $"{state.Analyses.Length} chars"));
        logger.LogInformation("► [GRAPH:Writer #{N}] {C} chars", state.WriterLoops, state.Analyses.Length);
        return state;
    }

    // The specialised Phase-2 prompt for convention-income cases: the brief supplies the démarche,
    // forbidden steps, qualification and structure-only skeleton; EyStyle supplies the voice.
    private static string BuildBriefPrompt(ConsultationState state, CaseBrief brief)
    {
        var cmd     = state.Command;
        var n       = state.EtendueItems.Count;
        var et      = string.Join("\n", state.EtendueItems.Select((x, i) => $"  {i + 1}. {x}"));
        var concise = string.Equals(cmd.Mode, "concise", StringComparison.OrdinalIgnoreCase);
        var format  = concise
            ? "FORMAT — VERSION CONCISE : mêmes qualification, mêmes verdicts et mêmes taux que la version " +
              "détaillée, mais CONDENSÉS (chaque point en quelques phrases). N'omets aucun verdict ni taux.\n"
            : "FORMAT — VERSION DÉTAILLÉE : prose professionnelle continue, chaque point développé selon la " +
              "démarche ci-dessus, chaque règle appliquée aux faits et close par une position claire.\n";

        return
            $"PHASE 2 — JSON avec 1 clé: analyses.\n\n" +
            $"CAS QUALIFIÉ : {brief.Label}\n\n" +
            $"Client : {cmd.ClientName} | Question : {cmd.FiscalQuestion}\n\n" +
            $"FAITS ÉTABLIS (section 1.1) :\n{state.ContexteFaits}\n\n" +
            $"ÉTENDUE ({n} point(s) demandé(s)) :\n{et}\n\n" +
            H.SourcesBlock(state.Sources) + "\n" +
            "═══ QUALIFICATION ═══\n" + brief.QualificationGuidance + "\n\n" +
            brief.Demarche + "\n" +
            (string.IsNullOrWhiteSpace(brief.ForbiddenSteps) ? "" : "═══ À NE PAS FAIRE ═══\n" + brief.ForbiddenSteps + "\n\n") +
            EyStyle.Card + "\n" +
            "═══ MODÈLE DE STRUCTURE (forme uniquement — les crochets sont des ESPACES À REMPLIR depuis les " +
            "faits et les sources ; ne recopie JAMAIS un contenu du modèle) ═══\n" + brief.RedactedSkeleton + "\n\n" +
            format +
            $"Organise en blocs « 4.1 » à « 4.{n} » (un par point d'étendue).\n" +
            "[Sn] OBLIGATOIRE ; tout taux cite sa source [Sn] et est LU dans son texte.\n\n" +
            "{\"analyses\":\"4. ANALYSES\\n\\n[blocs]\"}";
    }

    // ── [6] Judge: LLM-as-judge against the CASE's criteria; verdict routes the graph ──
    private async ValueTask<ConsultationState> JudgeAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var sourcesList = string.Join("\n", state.Sources.Take(18)
            .Select(s => $"[S{s.Index}] {s.DocType} {s.DocName} {s.ArticleRef}"));
        if (!string.IsNullOrWhiteSpace(state.Brief!.JudgeCriteria))
            sourcesList += "\n\n═══ CRITÈRES SPÉCIFIQUES AU CAS ═══\n" + state.Brief.JudgeCriteria;

        var etendue = "Notre analyse portera sur les points suivants :\n" +
                      string.Join("\n", state.EtendueItems.Select(i => $"- {i}"));
        var verdict = await acceptance.ReviewAsync(
            new AcceptanceRequest(state.Command.FiscalQuestion, etendue, state.Analyses, sourcesList), ct);

        state.JudgeRan          = true;
        state.JudgeAccepted     = verdict.Accept;
        state.JudgeNeedsSources = verdict.NeedsMoreSources;
        state.JudgeMissingTopics = verdict.MissingTopics.ToList();
        state.JudgeGuidance = string.IsNullOrWhiteSpace(verdict.RevisionGuidance)
            ? (verdict.Issues.Any() ? "Corrige: " + string.Join(" ; ", verdict.Issues)
                                    : "Corrige les faiblesses de qualité.")
            : verdict.RevisionGuidance;

        sw.Stop();
        state.Timings.Add(("W6. Judge", sw.Elapsed.TotalMilliseconds,
            verdict.Accept ? "accepted" : $"reject ({verdict.Issues.Count} issues)"));
        logger.LogInformation("► [GRAPH:Judge] accept={A} needsSrc={S} issues={I}",
            verdict.Accept, verdict.NeedsMoreSources, verdict.Issues.Count);
        return state;
    }

    // ── [7] Expert voice: EY polish with a HARD extract-and-verify invariant contract ──
    // The rewrite may only touch connective prose. Every rate, citation, article number and verdict
    // is extracted before and after; ANY difference discards the rewrite wholesale.
    private async ValueTask<ConsultationState> ExpertAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var original = state.Analyses;
        if (string.IsNullOrWhiteSpace(original) || original.Length < 200)
        {
            state.Timings.Add(("W7. ExpertVoice", 0, "skipped (short)"));
            return state;
        }

        try
        {
            var prompt =
                EyStyle.Card + "\n\n" +
                "Voici un PROJET d'analyse. Réécris-le pour qu'il se lise comme un mémo EY final et fluide, " +
                "sous CONTRAINTES STRICTES :\n" +
                "- NE CHANGE AUCUN chiffre, AUCUN taux, AUCUNE citation [Sn], AUCUN verdict, AUCUN numéro d'article.\n" +
                "- CONSERVE toutes les sections, sous-sections, titres et leur ordre, ainsi que les « Verdict : ».\n" +
                "- Améliore UNIQUEMENT la fluidité, les liaisons et le ton ; supprime les tournures de brouillon.\n" +
                "- Réponds en JSON : {\"analyses\":\"...\"}.\n\n" +
                "PROJET :\n" + original;
            var raw     = await llm.CompleteAsync(state.Brief!.SystemPrompt, prompt, "ExpertVoice", 3800, ct);
            var revised = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");

            var (ok, why) = InvariantsPreserved(original, revised);
            if (ok)
            {
                state.Analyses     = revised;
                state.ExpertApplied = true;
                state.Timings.Add(("W7. ExpertVoice", sw.Elapsed.TotalMilliseconds, "polished"));
            }
            else
            {
                state.Timings.Add(("W7. ExpertVoice", sw.Elapsed.TotalMilliseconds, $"reverted: {why}"));
                logger.LogWarning("► [GRAPH:Expert] rewrite REVERTED — invariant broken: {W}", why);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GRAPH:Expert] failed — keeping draft");
            state.Timings.Add(("W7. ExpertVoice", sw.Elapsed.TotalMilliseconds, "failed, kept draft"));
        }
        return state;
    }

    /// <summary>The immutability contract: rates (multiset), citations (set), article numbers (set),
    /// verdict count and section count must all survive the rewrite; length may not collapse.</summary>
    public static (bool Ok, string Why) InvariantsPreserved(string before, string after)
    {
        if (string.IsNullOrWhiteSpace(after))              return (false, "empty rewrite");
        if (after.Length < before.Length * 0.6)            return (false, "length collapsed");

        static string Rates(string t) => string.Join("|",
            Regex.Matches(t, @"\d+(?:[.,]\d+)?\s*%").Select(m => m.Value.Replace(" ", ""))
                 .OrderBy(x => x, StringComparer.Ordinal));
        static string Cites(string t) => string.Join("|",
            Regex.Matches(t, @"\[S\d+\]").Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));
        static string Arts(string t) => string.Join("|",
            Regex.Matches(t, @"(?i)art(?:icle)?\.?\s*(\d+(?:\s*(?:bis|ter))?)")
                 .Select(m => m.Groups[1].Value.Replace(" ", "").ToLowerInvariant())
                 .Distinct().OrderBy(x => x, StringComparer.Ordinal));
        static int Verdicts(string t) => Regex.Matches(t, @"(?im)^\s*Verdict").Count;
        static int Sections(string t) => Regex.Matches(t, @"(?m)^\s*(?:4\.\d|[A-C]\.\d?|[A-C]\.)").Count;

        if (Rates(before) != Rates(after))                 return (false, "rates changed");
        if (Cites(before) != Cites(after))                 return (false, "citations changed");
        if (Arts(before)  != Arts(after))                  return (false, "article numbers changed");
        if (Verdicts(after) < Verdicts(before))            return (false, "verdict dropped");
        if (Sections(after) < Sections(before))            return (false, "section dropped");
        return (true, "");
    }

    // ── [8] Finalize: synthesis table derived STRICTLY from the final analyses; yield output ──
    private async ValueTask FinalizeAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var raw    = await llm.CompleteAsync(H.SystemPrompt,
                H.BuildTablePrompt(state.EtendueItems, state.Analyses), "Table", 1800, ct);
            var parsed = raw is not null ? H.ParseJsonDict(raw) : null;
            if (parsed is not null && parsed.TryGetValue("analysis_table", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                var rows = new List<AnalysisRow>();
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    string S(string k) => el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? "" : "";
                    rows.Add(new AnalysisRow(S("sujet"), S("analyse"), S("conclusion")));
                }
                if (rows.Count > 0) state.Table = rows;
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "[GRAPH:Finalize] table derivation failed"); }
        sw.Stop();
        state.Timings.Add(("W8. Finalize (table)", sw.Elapsed.TotalMilliseconds, $"rows={state.Table.Count}"));

        await ctx.YieldOutputAsync(state, ct);
    }

    private ICaseAgent Agent(CaseType type) =>
        caseAgents.FirstOrDefault(a => a.Type == type)
        ?? caseAgents.First(a => a.Type == CaseType.Generic);
}
