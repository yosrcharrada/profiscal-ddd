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

        // The graph mutates ONE `state` instance in place as it flows the edges, so that reference
        // IS the result — we just drive the stream to completion and return it. (No YieldOutput;
        // see FinalizeAsync.)
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, state, cancellationToken: ct);
        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case WorkflowErrorEvent err:   logger.LogError("[GRAPH] workflow error: {E}", err);   break;
                case ExecutorFailedEvent fail: logger.LogError("[GRAPH] executor failed: {E}", fail); break;
            }
        }
        return state;
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

    // ── [2] Case brief + coverage: the case agent declares WHAT this case typically needs,
    // then the CoveragePlanner (one bounded LLM call, closed aspect library) reads the étendue
    // and adds the aspects THIS consultation implies beyond the static checklist — the fix for
    // "régime fiscal demanded TVA but nobody fetched or wrote it". Fail-open on any LLM error. ──
    private async ValueTask<ConsultationState> BriefAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw    = Stopwatch.StartNew();
        var agent = Agent(state.CaseType);
        var baseBrief = agent.BuildBrief(state);
        var baseCount = baseBrief.RequiredSources.Count;
        state.Brief = await CoveragePlanner.ExpandAsync(baseBrief, state, llm, logger, ct);
        sw.Stop();
        state.Timings.Add(("W2. Brief + coverage", sw.Elapsed.TotalMilliseconds,
            $"{baseCount}+{state.Brief.RequiredSources.Count - baseCount} required src"));
        logger.LogInformation("► [GRAPH:Brief] {L} — checklist: {N} items ({S} static + {D} coverage)",
            state.Brief.Label, state.Brief.RequiredSources.Count,
            baseCount, state.Brief.RequiredSources.Count - baseCount);
        return state;
    }

    // ── [3] Fulfil the brief — DETERMINISTIC métier fetchers ─────────────────────────────
    // The case-aware autonomous ReAct planner runs ONCE, pre-graph (handler Step 2), for open-ended
    // recall. It is deliberately NOT re-invoked here: on every in-graph pass it re-derived the same
    // plan and re-fetched an identical source set (~16s / 2 LLM calls each — a large slice of the
    // workflow time), while the deterministic checklist fetchers — which encode the tax team's exact
    // source map (line-precise parts, treaty-by-subject, newest edition) — are what actually close
    // each item. Those always run.
    private async ValueTask<ConsultationState> FulfilAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var sw    = Stopwatch.StartNew();
        var brief = state.Brief!;
        var agent = Agent(state.CaseType);
        state.RetrievalLoops++;

        var report      = agent.VerifyCompleteness(brief, state.Sources, state.Countries);
        var before      = state.Sources.Count;
        var hasDraft    = !string.IsNullOrEmpty(state.Analyses);
        var judgeDriven = state.JudgeMissingTopics.Count > 0;
        if (judgeDriven) state.JudgeRetrievalsUsed++;   // this pass consumes the one judge-retrieval budget

        if (report.Missing.Count > 0 || judgeDriven)
        {
            // The case-aware autonomous planner already ran ONCE pre-graph (handler Step 2); a second
            // pass here returned an identical source set at full LLM cost. So fulfilment is owned by the
            // DETERMINISTIC métier fetchers below — line-precise parts, treaty-by-subject, newest
            // edition, Arabic exclusion — which are what actually satisfy the checklist.
            logger.LogInformation("► [GRAPH:Fulfil #{L}] deterministic fetchers — {N} checklist item(s){J}",
                state.RetrievalLoops, report.Missing.Count,
                judgeDriven ? $" + {state.JudgeMissingTopics.Count} judge topic(s)" : "");

            var still = agent.VerifyCompleteness(brief, state.Sources, state.Countries);
            foreach (var item in still.Missing.Take(8))
            {
                try   { AddDeduped(state.Sources, await FetchForAsync(item, brief, state, ct)); }
                catch (Exception ex) { logger.LogWarning(ex, "[GRAPH:Fulfil] net item {K}", item.Key); }
            }

            // judge-flagged topics → keyword fetches.
            foreach (var topic in state.JudgeMissingTopics.Take(4))
            {
                try   { AddDeduped(state.Sources, await retrieval.FetchTargetedAsync("", System.Array.Empty<string>(), new[] { topic }, ct)); }
                catch (Exception ex) { logger.LogWarning(ex, "[GRAPH:Fulfil] judge topic {T}", topic); }
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

            // (3) COALESCE paragraph-parts of the same article into ONE citable source. taxmindvf
            //     splits an article into many parts (Art.52=37, Art.7=12…); without this a case that
            //     needs ~10 articles floods the writer's 18-slot window with fragments of 2–3 articles
            //     and the rest ("NC régime privilégié", CTVA 7, CDPF 112…) falls off-screen → the
            //     writer truthfully says NON DOCUMENTÉ for sources that WERE fetched. Also cleans the
            //     "(Part 6/37)" citations. Safe here: no draft exists yet, so re-indexing is allowed.
            CoalesceArticleParts(state.Sources);

            PinCaseSources(state.Sources, state.Countries, brief);
            for (int i = 0; i < state.Sources.Count; i++) state.Sources[i].Index = i + 1;
        }
        else
        {
            for (int i = 0; i < state.Sources.Count; i++)
                if (state.Sources[i].Index <= 0) state.Sources[i].Index = i + 1;
            for (int i = before; i < state.Sources.Count; i++) state.Sources[i].Index = i + 1;
        }

        var added = state.Sources.Count - before;   // NET change: curation may drop older editions
        state.LastFulfilProgressed = added > 0;     // progress guard — read by the routing predicates

        sw.Stop();
        var netTxt = added >= 0 ? $"+{added}" : $"{added} (curation)";
        state.Timings.Add(($"W3. Fulfil #{state.RetrievalLoops}", sw.Elapsed.TotalMilliseconds,
            $"net {netTxt} src, {report.Missing.Count} asked"));
        logger.LogInformation("► [GRAPH:Fulfil #{L}] net {N} sources ({M} items missing before)",
            state.RetrievalLoops, netTxt, report.Missing.Count);
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

        // LINE-PRECISE: the item's own predicates drive the part selection — on the part-split graph
        // the writer receives the article header plus ONLY the alinéas this case needs (with NEXT_PART
        // neighbours for straddles), never the whole menu.
        // mustContain narrows a MULTI-RATE article (Art.52) to the applicable rate LINE, so it only
        // applies when we are selecting a rate line (RequirePercent). For a single-regime article like
        // CDPF Art.112, TextContains is a DISAMBIGUATION guard for IsSatisfiedBy (112 vs « 112 bis »,
        // which share the same leading digits), NOT a line selector — passing it to the fetch would
        // clip the article to the one part carrying the phrase; leave it null so all parts come back.
        if (item.ArticleNumber is not null)
        {
            var mustContain = item.RequirePercent ? item.TextContains : null;
            var lines = await retrieval.FetchArticleLinesAsync(
                item.FetchDocFragment ?? item.DocFragment ?? "",
                item.ArticleNumber, mustContain, item.RequirePercent, ct) ?? new();
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

    // Merge the paragraph-level PARTS of one article (same document + same article number) into a
    // single citable source: concatenated text (reading order), clean article ref (no "(Part x/y)").
    // Non-article chunks (doctrine passages, notes without an article number) stay as-is. This is what
    // makes "1 article = 1 slot" hold again on the part-split graph.
    internal static void CoalesceArticleParts(List<LegalSourceDto> sources)
    {
        static string ArtNum(string? aref)
        {
            if (string.IsNullOrEmpty(aref)) return "";
            var m = Regex.Match(aref, @"\d+(?:\s*(?:bis|ter))?", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.Replace(" ", "").ToLowerInvariant() : "";
        }
        static string CleanRef(string? aref) => string.IsNullOrEmpty(aref) ? "" :
            Regex.Replace(aref, @"\s*\(\s*Part\s*\d+\s*/\s*\d+\s*\)\s*", "", RegexOptions.IgnoreCase).Trim();

        var order  = new List<string>();
        var groups = new Dictionary<string, List<LegalSourceDto>>();
        foreach (var s in sources)
        {
            var num = ArtNum(s.ArticleRef);
            // Only real article parts merge; everything else is its own singleton (unique key).
            var key = num.Length > 0
                ? "A|" + (s.DocName ?? "").ToLowerInvariant() + "|" + num
                : "S|" + (s.ChunkId ?? Guid.NewGuid().ToString("N"));
            if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new(); order.Add(key); }
            list.Add(s);
        }

        var merged = new List<LegalSourceDto>(order.Count);
        foreach (var key in order)
        {
            var parts = groups[key];
            if (parts.Count == 1) { merged.Add(parts[0]); continue; }
            var first = parts[0];
            merged.Add(new LegalSourceDto
            {
                ChunkId      = first.ChunkId,
                DocName      = first.DocName,
                DocType      = first.DocType,
                ArticleRef   = CleanRef(first.ArticleRef),
                SectionTitle = first.SectionTitle,
                Year         = first.Year,
                Text         = string.Join("\n", parts.Select(p => p.Text)),
                Score        = parts.Max(p => p.Score),
                IsExpert     = first.IsExpert,
            });
        }
        sources.Clear();
        sources.AddRange(merged);
    }

    // Pin the decisive sources to the front of the visible window: treaty income articles matching
    // the brief's subjects, then the %-bearing CIRPPIS 52/53 and CTVA 7, then EVERY source that
    // satisfies a checklist item (régime-privilégié list, CDPF 112, BCT, NC 3/2015, CTVA 3/19…) —
    // so nothing the case explicitly requires can be crowded out of the writer's window.
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

        bool SatisfiesChecklist(LegalSourceDto s) =>
            brief.RequiredSources.Any(req => req.IsSatisfiedBy(s, countries));

        var treaty   = sources.Where(IsTreaty).ToList();
        var seen     = new HashSet<LegalSourceDto>(treaty);
        var domestic = sources.Where(s => !seen.Contains(s) && IsDomesticRate(s))
                              .OrderByDescending(Year).ToList();
        foreach (var s in domestic) seen.Add(s);
        var checklist = sources.Where(s => !seen.Contains(s) && SatisfiesChecklist(s)).ToList();
        foreach (var s in checklist) seen.Add(s);
        var rest     = sources.Where(s => !seen.Contains(s)).ToList();

        sources.Clear();
        sources.AddRange(treaty);
        sources.AddRange(domestic);
        sources.AddRange(checklist);
        sources.AddRange(rest);
    }

    // ── [4] Completeness: deterministic checklist verification (no LLM, free, reproducible) ──
    private ValueTask<ConsultationState> CompletenessAsync(
        ConsultationState state, IWorkflowContext ctx, CancellationToken ct)
    {
        var agent  = Agent(state.CaseType);
        var report = agent.VerifyCompleteness(state.Brief!, state.Sources, state.Countries);

        // Existence-conditional items (treaty articles): the FIRST fulfil pass already ran the
        // planner's convention search AND the deterministic treaty-by-subject fetch. If the treaty
        // article still isn't found, its absence is a legal FACT (no convention with that country →
        // droit commun applies), not a retrieval failure to chase for another loop. Firing at loop 1
        // (instead of 2) saves a whole redundant fulfil pass for treaty-less countries like Hong Kong,
        // and is MORE correct — it's a known legal fact, not something to keep empirically probing.
        if (state.RetrievalLoops >= 1 && report.MissingCritical.Any(m => m.ExistenceConditional))
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

    /// <summary>The immutability contract. Rates and citations are compared as NORMALISED DISTINCT
    /// SETS (not multisets): the polish may legitimately consolidate « 35% ou 40% » or drop a
    /// redundant repeat of a rate — that must NOT revert it. What must never happen: a distinct rate
    /// VALUE appears or disappears (hallucinated or lost figure), a citation is dropped, an article
    /// number changes, a verdict/section is lost, or the length collapses. This precise boundary is
    /// what lets the EY-voice rewrite actually apply instead of reverting on every reformatting.</summary>
    public static (bool Ok, string Why) InvariantsPreserved(string before, string after)
    {
        if (string.IsNullOrWhiteSpace(after))              return (false, "empty rewrite");
        if (after.Length < before.Length * 0.55)           return (false, "length collapsed");

        // distinct rate VALUES, comma/dot and spacing normalised (« 1,5 % » == « 1.5% »)
        static HashSet<string> Rates(string t) => Regex.Matches(t, @"\d+(?:[.,]\d+)?\s*%")
            .Select(m => m.Value.Replace(" ", "").Replace(",", ".")).ToHashSet();
        static HashSet<string> Cites(string t) => Regex.Matches(t, @"\[S\d+\]")
            .Select(m => m.Value).ToHashSet();
        static HashSet<string> Arts(string t) => Regex.Matches(t, @"(?i)art(?:icle)?\.?\s*(\d+(?:\s*(?:bis|ter))?)")
            .Select(m => m.Groups[1].Value.Replace(" ", "").ToLowerInvariant()).ToHashSet();
        static int Verdicts(string t) => Regex.Matches(t, @"(?im)^\s*Verdict").Count;
        static int Sections(string t) => Regex.Matches(t, @"(?m)^\s*(?:4\.\d|[A-C]\.\d?|[A-C]\.)").Count;

        if (!Rates(before).SetEquals(Rates(after)))        return (false, "rate value added/removed");
        if (!Cites(before).SetEquals(Cites(after)))        return (false, "citation added/removed");
        if (!Arts(before).SetEquals(Arts(after)))          return (false, "article number changed");
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

        // NOTE: we do NOT ctx.YieldOutputAsync(state) — MAF requires the output TYPE to be declared
        // on the builder, and a graph carrying a mutable domain state uses that same instance as the
        // result. Finalize is terminal (sends no message), so the workflow goes idle and the handler
        // reads the mutated `state` reference directly. Yielding here threw
        // "Cannot output object of type ConsultationState. Expecting one of []" on every run.
    }

    private ICaseAgent Agent(CaseType type) =>
        caseAgents.FirstOrDefault(a => a.Type == type)
        ?? caseAgents.First(a => a.Type == CaseType.Generic);
}
