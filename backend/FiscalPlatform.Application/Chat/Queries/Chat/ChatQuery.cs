using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Chat.Queries.Chat;

public sealed record ChatQuery(string Question, List<string> History) : IRequest<ChatResponseDto>;
public sealed record ChatResponseDto(string Answer, List<SourceChunkDto> Sources, double ElapsedMs);

// ── Streaming events (consumed by the SSE endpoint and aggregated by Handle) ──
public abstract record ChatStreamEvent;
public sealed record ChatStatusEvent(string Phase, string Text)        : ChatStreamEvent;
public sealed record ChatSourcesEvent(List<SourceChunkDto> Sources)    : ChatStreamEvent;
public sealed record ChatTokenEvent(string Text)                       : ChatStreamEvent;
public sealed record ChatDoneEvent(double ElapsedMs)                   : ChatStreamEvent;

/// <summary>
/// Legal chatbot as a TRUE bounded-ReAct agent — same architecture as RetrievalPlannerAgent,
/// not a fixed one-shot RAG.
///   Brain   : GPT-4o decides which retrieval tools to call (JSON contract, all at once).
///   Memory  : conversation history + an observation log carried across rounds.
///   Actions : real Neo4j / embed-server retrieval tools, dispatched in PARALLEL each round.
///   Observe-and-adapt: round 2 reacts to what round 1 returned (or fills gaps), then the
///                      agent answers. Bounded to 2 planning rounds to stay fast on the slow
///                      EY endpoint, followed by one grounded-answer call.
/// The final answer cites [Source N] aligned to the returned Sources list so the UI can make
/// each citation clickable.
/// </summary>
public sealed class ChatQueryHandler(
    IRetrievalAgent retrieval,
    ILlmAgent llm,
    IEmbedSearchAgent embed,
    IBranchDetector branchDetector,
    ICountryDetector countryDetector,
    IKeywordExtractor keywordExtractor,
    IRuleBasedRetrieval ruleRetrieval,
    IFiscalGuardrails guardrails,
    ILogger<ChatQueryHandler> logger)
    : IRequestHandler<ChatQuery, ChatResponseDto>
{
    private const int MaxPlanningRounds = 2;
    private const int MaxSources        = 10;
    // Per-source character caps for the answer prompt. A rate-bearing article must arrive whole
    // or its rate lines — which sit at the END of the menu — are cut off (see BuildAnswerPrompt).
    // Mirrors the consultation writer's 15 000; worst case here is 10 sources, but only the rate
    // articles reach the high cap and gpt-4o's window absorbs it.
    private const int RateChars         = 15000;
    private const int PlainChars        = 1500;
    // Minimum-quality bar for the accumulated source set before we let the agent answer. The chat
    // pipeline has no Completeness node (the consultation workflow does), so a planner round that
    // returns 1-2 weak/tangential chunks used to sail straight through. Below this count — or when
    // a targeted-completeness check fails — we fire ONE extra bounded retrieval attempt.
    private const int MinAcceptableSources = 3;

    private const string PlannerSystem =
        "Tu es un agent de recherche juridique fiscale tunisienne. Ton rôle: décider quels outils " +
        "appeler pour rassembler les sources nécessaires afin de répondre à la question.\n" +
        "OUTILS DISPONIBLES:\n" +
        "  semantic_search   {\"query\":\"...\"}                       → OUTIL PRINCIPAL: recherche sémantique. À utiliser pour PRESQUE TOUTE question (meilleure pertinence).\n" +
        "  search_convention {\"country\":\"...\",\"query\":\"...\"}    → dès qu'un pays/non-résident est impliqué (donne 'country' en français minuscule).\n" +
        "  keyword_search    {\"query\":\"...\"}                       → UNIQUEMENT pour retrouver un article précis cité par numéro (ex: \"Art. 52 CIRPPIS\"). Évite-le pour les questions générales.\n" +
        "  graph_expand      {\"entities\":\"e1, e2\"}                 → étend via entités liées du graphe (optionnel).\n\n" +
        "RÈGLE: appelle TOUJOURS semantic_search pour une question fiscale (sauf salutation pure). " +
        "Ajoute search_convention en parallèle si un pays étranger est en jeu.\n" +
        "RÉPONDS UNIQUEMENT EN JSON, sans aucun texte autour:\n" +
        "{\"thought\":\"bref\",\"tool_calls\":[{\"tool\":\"semantic_search\",\"query\":\"...\"}],\"ready_to_answer\":false}\n" +
        "- Mets plusieurs outils dans tool_calls pour les lancer en parallèle.\n" +
        "- Si la question est purement conversationnelle (salutation, remerciement), OU si les " +
        "OBSERVATIONS contiennent déjà assez de sources: {\"tool_calls\":[],\"ready_to_answer\":true}.";

    private const string AnswerSystem =
        "Tu es un assistant fiscal tunisien expert, style EY — précis, formel, en français.\n" +
        "Réponds à la QUESTION en te basant UNIQUEMENT sur les SOURCES numérotées fournies.\n" +
        "Cite chaque affirmation avec [Source N] (N = numéro EXACT de la source utilisée).\n" +
        "Tout taux, article ou règle cité DOIT renvoyer à une [Source N]. Si un point n'est pas " +
        "couvert par les sources, écris 'NON DOCUMENTÉ' pour ce point — n'invente jamais une règle, " +
        "un taux ou un article. Markdown autorisé (titres, listes, gras).\n\n" +
        Common.FiscalPrompts.MetierCore;

    // Non-streaming entry point (MediatR). Aggregates the streaming events into one DTO,
    // so there is a SINGLE agent implementation (StreamAsync) behind both endpoints.
    public async Task<ChatResponseDto> Handle(ChatQuery query, CancellationToken ct)
    {
        var sb      = new StringBuilder();
        var sources = new List<SourceChunkDto>();
        double ms   = 0;
        await foreach (var ev in StreamAsync(query, ct))
        {
            switch (ev)
            {
                case ChatTokenEvent t:   sb.Append(t.Text);     break;
                case ChatSourcesEvent s: sources = s.Sources;   break;
                case ChatDoneEvent d:    ms = d.ElapsedMs;      break;
            }
        }
        var answer = sb.ToString().Trim();
        // Output guardrail: grounding scan (invalid citations, uncited rates) — logged, non-blocking.
        guardrails.ValidateTextWarnings(answer, sources.Count);
        return new ChatResponseDto(answer.Length == 0 ? "Je n'ai pas pu répondre." : answer, sources, ms);
    }

    /// <summary>
    /// The agent as a live event stream: status updates (what it's doing), then the source
    /// list, then the answer token-by-token, then done. Same ReAct loop as before.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatQuery query, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw       = System.Diagnostics.Stopwatch.StartNew();
        var question = (query.Question ?? "").Trim();
        logger.LogInformation("┌─ [CHAT-AGENT] (stream) '{Q}'", question[..Math.Min(question.Length, 60)]);

        // ── Input guardrail: block substantive OFF-TOPIC questions before any LLM/retrieval spend.
        // Short conversational messages (greetings, thanks, follow-ups) pass through — the agent
        // handles those without sources.
        var (inputOk, inputReason) = guardrails.ValidateInput(question, question);
        var looksSubstantive = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6;
        if (!inputOk && looksSubstantive)
        {
            logger.LogWarning("└─ [CHAT-AGENT] input guardrail blocked: {R}", inputReason);
            yield return new ChatSourcesEvent(new List<SourceChunkDto>());
            yield return new ChatTokenEvent(inputReason ??
                "La question ne semble pas être de nature fiscale. Veuillez préciser votre question fiscale.");
            yield return new ChatDoneEvent(sw.Elapsed.TotalMilliseconds);
            yield break;
        }

        var historyText  = BuildHistoryText(query.History);
        var accumulated  = new List<SourceChunkDto>();
        var seen         = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observations = new StringBuilder();
        var ready        = false;

        yield return new ChatStatusEvent("analyzing", "Analyse de la question…");

        // ── Reason + Act + Observe (bounded) ──────────────────────────────────
        for (int round = 1; round <= MaxPlanningRounds && !ready; round++)
        {
            var planUser =
                (historyText.Length > 0 ? $"CONVERSATION:\n{historyText}\n\n" : "") +
                $"QUESTION ACTUELLE: {question}\n\n" +
                (observations.Length > 0 ? $"OBSERVATIONS:\n{observations}\n\n" : "") +
                (round == MaxPlanningRounds ? "Dernier tour — ne demande des outils que si vraiment nécessaire.\n" : "") +
                "Quels outils appeler? Réponds en JSON.";

            var planRaw = await llm.CompleteAsync(PlannerSystem, planUser, $"Chat-Plan{round}", 700, ct);
            var plan    = ParsePlan(planRaw);

            if (plan is null)
            {
                if (round == 1)
                    plan = new ChatPlan(new() { new ChatToolCall("semantic_search", question, null, null) }, false);
                else { ready = true; break; }
            }

            ready = plan.ReadyToAnswer || plan.ToolCalls.Count == 0;
            if (plan.ToolCalls.Count == 0) break;

            var desc = string.Join(", ", plan.ToolCalls.Select(t =>
            {
                var a = t.Query ?? t.Country ?? t.Entities ?? "";
                return a.Length > 0 ? $"{Friendly(t.Tool)} « {(a.Length > 48 ? a[..48] + "…" : a)} »" : Friendly(t.Tool);
            }));
            yield return new ChatStatusEvent("searching", $"Recherche de sources juridiques — {desc}");

            // Act: dispatch every requested tool concurrently (the key ReAct trick).
            var results = await Task.WhenAll(plan.ToolCalls.Select(tc => ExecuteToolAsync(tc, ct)));

            foreach (var (tc, chunks) in plan.ToolCalls.Zip(results))
            {
                var added = 0;
                foreach (var c in chunks)
                {
                    var txt = c.Text ?? "";
                    var key = (c.DocName + "|" + c.ArticleRef + "|" + txt[..Math.Min(txt.Length, 60)]).Trim();
                    if (seen.Add(key)) { accumulated.Add(c); added++; }
                }
                var arg = tc.Query ?? tc.Country ?? tc.Entities ?? "";
                observations.AppendLine(
                    $"- {tc.Tool}({arg}): {(chunks.Count == 0 ? "VIDE — aucune source" : $"+{added} sources")}");
                logger.LogInformation("│  [CHAT-AGENT] r{R} {T}(q='{Arg}'): {C} hits, +{A} new",
                    round, tc.Tool, arg, chunks.Count, added);
            }
        }

        // ── Completeness safety net (the chat analogue of the consultation Completeness node) ──
        // Instead of only rescuing a COMPLETELY empty result, verify the accumulated set actually
        // clears a minimum bar before answering. All checks are cheap and LLM-free (they reuse the
        // existing CountryDetector + the same '%' rate signal the consultation pipeline uses via
        // RequirePercent). They trigger AT MOST ONE additional bounded retrieval attempt — never a loop.
        if (question.Length > 0)
        {
            // (a) foreign country named but no convention for it in the accumulated sources.
            var (qCountries, _) = countryDetector.Detect(question);
            var foreignCountries = qCountries
                .Where(c => { var k = c.Trim().ToLowerInvariant(); return k.Length > 0 && !k.StartsWith("tunis"); })
                .ToList();
            bool missingConvention = foreignCountries.Count > 0 && !accumulated.Any(s =>
                string.Equals(s.Category, "Convention", StringComparison.OrdinalIgnoreCase));

            // (b) question asks for a rate/amount but no accumulated source carries a real rate ('%').
            //     Same signal family as CaseBrief.RequirePercent (source text must contain '%').
            var ql = question.ToLowerInvariant();
            bool asksRate = ql.Contains("taux") || ql.Contains('%') || ql.Contains("pourcentage")
                            || ql.Contains("combien") || ql.Contains("montant");
            bool missingRate = asksRate && !accumulated.Any(s => (s.Text ?? "").Contains('%'));

            // baseline blunt net (was `== 0`): a planner round returning a couple of near-empty hits.
            bool tooFew = accumulated.Count < MinAcceptableSources;

            if (tooFew || missingConvention || missingRate)
            {
                yield return new ChatStatusEvent("searching", "Recherche approfondie dans le corpus…");

                // One bounded attempt: a targeted convention fetch for the named country (only when
                // that's what's missing) PLUS the full engine deep retrieve, dispatched together.
                var extraTasks = new List<Task<List<SourceChunkDto>>>();
                if (missingConvention)
                    extraTasks.Add(ExecuteToolAsync(
                        new ChatToolCall("search_convention", question, foreignCountries[0], null), ct));
                extraTasks.Add(DeepRetrieveAsync(question, ct));

                foreach (var list in await Task.WhenAll(extraTasks))
                    foreach (var c in list)
                    {
                        var txt = c.Text ?? "";
                        var key = (c.DocName + "|" + c.ArticleRef + "|" + txt[..Math.Min(txt.Length, 60)]).Trim();
                        if (seen.Add(key)) accumulated.Add(c);
                    }
                logger.LogInformation(
                    "│  [CHAT-AGENT] completeness net (tooFew={F} missingConv={C} missingRate={R}) → {N} sources",
                    tooFew, missingConvention, missingRate, accumulated.Count);
            }
        }

        var finalSources = accumulated.OrderByDescending(s => s.Score).Take(MaxSources).ToList();
        yield return new ChatSourcesEvent(finalSources);
        yield return new ChatStatusEvent("writing", "Rédaction de la réponse…");

        // Reason: stream the grounded, cited answer token-by-token.
        var answerUser = BuildAnswerPrompt(finalSources, historyText, question);
        var any = false;
        await foreach (var tok in llm.StreamAsync(AnswerSystem, answerUser, "Chat-Answer", ct))
        {
            any = true;
            yield return new ChatTokenEvent(tok);
        }
        if (!any) // streaming unavailable → one-shot fallback so the user still gets an answer
        {
            var full = await llm.CompleteAsync(AnswerSystem, answerUser, "Chat-Answer", 1600, ct)
                       ?? "Je n'ai pas pu répondre.";
            yield return new ChatTokenEvent(full);
        }

        sw.Stop();
        logger.LogInformation("└─ [CHAT-AGENT] ✓ {Ms:F0}ms | {N} sources (stream)",
            sw.Elapsed.TotalMilliseconds, finalSources.Count);
        yield return new ChatDoneEvent(sw.Elapsed.TotalMilliseconds);
    }

    private static string Friendly(string tool) => tool switch
    {
        "semantic_search"   => "recherche sémantique",
        "search_convention" => "convention fiscale",
        "keyword_search"    => "recherche ciblée",
        "graph_expand"      => "graphe juridique",
        _                   => tool,
    };

    private static string BuildAnswerPrompt(
        List<SourceChunkDto> sources, string historyText, string question)
    {
        var ctx = historyText.Length > 0 ? $"CONVERSATION:\n{historyText}\n\n" : "";
        if (sources.Count == 0)
            return ctx + $"QUESTION: {question}\n\n" +
                "Aucune source juridique n'a été trouvée. Si la question est conversationnelle, " +
                "réponds normalement et brièvement. Sinon, indique clairement qu'aucune source ne " +
                "couvre ce point et invite à reformuler ou préciser.";

        var srcBlock = new StringBuilder();
        for (var i = 0; i < sources.Count; i++)
        {
            var s       = sources[i];
            var txt     = s.Text ?? "";
            // Retrieval hands back ONE coalesced entry per article (all parts merged), so a
            // rate-bearing article arrives whole — the CIRPPIS Art.52 rate menu alone is ~13 200
            // chars, and its lines are ordered general-first: « 3% … honoraires servis aux PM
            // soumises à l'IS » sits near char 12 000 and « 1% … bénéfices soumis à l'IS au taux
            // de 20% » after it. A flat 600-char cap therefore truncated every article to its
            // heading — "ARTICLE 52 : … font l'objet d'une retenue à la source aux taux suivants :"
            // — and cut away every rate below it. The chatbot then answered NON DOCUMENTÉ to
            // "quel est le taux de RS sur les honoraires ?", correctly, about the text it was
            // shown. Same failure the consultation writer hit at an 8 600 cap; it caps
            // rate-bearing sources at 15 000 for exactly this reason, so mirror that here.
            var rateBearing = txt.Contains('%') ||
                              txt.Contains("taux", StringComparison.OrdinalIgnoreCase);
            var cap     = rateBearing ? RateChars : PlainChars;
            var preview = txt.Length > cap ? txt[..cap] + "…" : txt;
            srcBlock.AppendLine($"[Source {i + 1}] {s.Category} — {s.DocName} {s.ArticleRef}".TrimEnd());
            srcBlock.AppendLine(preview);
            srcBlock.AppendLine();
        }
        return ctx + $"SOURCES:\n{srcBlock}\n" + $"QUESTION: {question}\n\n" +
               "Réponds en citant [Source N] pour chaque élément.";
    }

    // ── Tool execution (the agent's real Actions) ─────────────────────────────
    private async Task<List<SourceChunkDto>> ExecuteToolAsync(ChatToolCall tc, CancellationToken ct)
    {
        try
        {
            switch (tc.Tool)
            {
                case "semantic_search":
                {
                    // Try the embed server first; if it's unavailable or has no vector index
                    // (e.g. on a machine where embeddings aren't indexed), fall back to the
                    // engine's full Neo4j retrieval pipeline so the chat still gets real sources.
                    var hits = (await embed.SearchAsync(tc.Query ?? "", topK: 8)).Select(ToChunk).ToList();
                    if (hits.Count == 0)
                        hits = await DeepRetrieveAsync(tc.Query ?? "", ct);
                    return hits;
                }

                case "search_convention":
                {
                    // This tool targets ONE foreign country's convention. Skip if no country, or
                    // if the country is Tunisia itself (the home country) — "tunisie" matches every
                    // convention doc_name (…tunisienne…) and would flood/bias the results.
                    var country = (tc.Country ?? "").Trim().ToLowerInvariant();
                    if (country is "" or "tunisie" or "tunisienne" or "tunisia" or "tunisian")
                        return new();

                    var scoped = (await embed.SearchScopedAsync(tc.Query ?? "", country, topK: 6))
                                 .Select(ToChunk).ToList();
                    var kws = (tc.Query ?? "")
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 3).Take(4).ToArray();
                    if (kws.Length == 0) kws = new[] { "redevances", "établissement stable" };
                    var conv = (await retrieval.FetchConventionArticleAsync(country, kws, ct))
                               .Select(ToChunk).ToList();
                    logger.LogInformation("│  [CHAT-AGENT] search_convention country='{C}' scoped={S} conv={V}",
                        country, scoped.Count, conv.Count);
                    scoped.AddRange(conv);
                    return scoped;
                }

                case "keyword_search":
                    // Use the real branch-guided engine pipeline (not the crude CONTAINS-any
                    // fallback, which matched generic words like "article" and returned noise).
                    return await DeepRetrieveAsync(tc.Query ?? "", ct);

                case "graph_expand":
                {
                    var ents = (tc.Entities ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    return ents.Count == 0 ? new() : await retrieval.GraphExpandAsync(ents, topK: 6);
                }

                default:
                    return new();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "chat tool {T} failed", tc.Tool);
            return new();
        }
    }

    // Full engine retrieval (branch detection → targeted Neo4j fetch → graph expansion →
    // diversity). Same pipeline that powers consultations — works without the embed server.
    private async Task<List<SourceChunkDto>> DeepRetrieveAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var branches            = branchDetector.Detect(query, "");
        var (keywords, entities)= keywordExtractor.Extract(query, "");
        var (countries, isIntl) = countryDetector.Detect(query);

        // The rule-based policy and the generic keyword/graph search answer different questions.
        // The policy is the "fiscal routing map": for a domestic service-fee branch it PINS
        // CIRPPIS Art.52 + NC 3/2015 by anchor phrase, which is the only reliable way to land on
        // the rate article — the generic search ranks by keyword overlap and, on "quel taux de RS
        // sur les honoraires ?", returned Art.51 sexies and Art.2 while never surfacing Art.52 at
        // all. The consultation handler has always called the policy; the chat never did, which is
        // precisely why it answered NON DOCUMENTÉ to questions the engine answers correctly —
        // the very failure IRuleBasedRetrieval's own summary says it exists to prevent.
        // Policy hits go FIRST so they survive the MaxSources cut.
        var ruleSources = new List<LegalSourceDto>();
        try
        {
            ruleSources = await ruleRetrieval.RetrieveAsync(
                new RuleContext(branches, isIntl, countries, query, ""), ct);
        }
        catch (Exception ex)
        {
            // The policy is an enhancement, not a precondition: keep the generic search on failure.
            logger.LogWarning(ex, "│  [CHAT-AGENT] rule-based retrieval failed — generic search only");
        }

        var generic = await retrieval.RetrieveSourcesAsync(
            keywords, entities, countries, isIntl, branches,
            new List<LegalSourceDto>(), maxResults: 12, ct);

        // De-dupe on the chunk, keeping the policy's copy when both fire.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<LegalSourceDto>();
        foreach (var s in ruleSources.Concat(generic))
        {
            var key = $"{s.DocName}|{s.ArticleRef}|{s.ChunkId}";
            if (seen.Add(key)) merged.Add(s);
        }

        // Merge the paragraph-PARTS of each article into one source, exactly as the consultation
        // graph does before its writer sees them. The graph stores an article as ~37 parts and part 1
        // is only the heading ("ARTICLE 52 : … aux taux suivants :") — every rate lives in a later
        // part. Without this the chat ranked 71 individual parts by score, kept the top 10, and the
        // rate parts never survived the cut, so the answer was NON DOCUMENTÉ while Art.52 was
        // nominally "retrieved". Coalescing restores "1 article = 1 slot" and hands the whole rate
        // menu to the answer prompt as a single source.
        Consultation.Orchestration.ConsultationWorkflow.CoalesceArticleParts(merged);

        if (ruleSources.Count > 0)
            logger.LogInformation("│  [CHAT-AGENT] rule-based policy pinned {N} part(s) → {M} coalesced source(s): {Arts}",
                ruleSources.Count, merged.Count,
                string.Join(", ", merged.Select(s => s.ArticleRef).Where(a => a.Length > 0).Take(6)));

        return merged.Select(ToChunk).ToList();
    }

    private static SourceChunkDto ToChunk(LegalSourceDto s) => new()
    {
        DocName    = s.DocName,
        Text       = s.Text,
        ArticleRef = s.ArticleRef,
        Score      = s.Score,
        ChunkType  = "text",
        Category   = s.DocType,
    };

    private static string BuildHistoryText(List<string>? history)
    {
        if (history is null || history.Count == 0) return "";
        var sb = new StringBuilder();
        for (var i = 0; i < history.Count; i++)
            sb.AppendLine($"{(i % 2 == 0 ? "Utilisateur" : "Assistant")}: {history[i]}");
        return sb.ToString();
    }

    // ── Plan JSON ─────────────────────────────────────────────────────────────
    private sealed record ChatPlan(List<ChatToolCall> ToolCalls, bool ReadyToAnswer);
    private sealed record ChatToolCall(string Tool, string? Query, string? Country, string? Entities);

    private static ChatPlan? ParsePlan(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end   = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root  = doc.RootElement;
            var ready = root.TryGetProperty("ready_to_answer", out var r) && r.ValueKind == JsonValueKind.True;
            var calls = new List<ChatToolCall>();
            if (root.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    var tool = FirstStr(tc, "tool", "name");
                    if (string.IsNullOrEmpty(tool)) continue;
                    calls.Add(new ChatToolCall(
                        tool,
                        FirstStr(tc, "query", "keywords", "q", "question", "text", "input"),
                        FirstStr(tc, "country", "pays"),
                        FirstStr(tc, "entities", "entity")));
                }
            }
            return new ChatPlan(calls, ready);
        }
        catch { return null; }
    }

    private static string? FirstStr(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
            if (el.TryGetProperty(k, out var v))
            {
                if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString();
                // tolerate {"query": ["a","b"]} or {"entities":[...]} → join
                if (v.ValueKind == JsonValueKind.Array)
                {
                    var parts = v.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString());
                    var joined = string.Join(", ", parts);
                    if (!string.IsNullOrWhiteSpace(joined)) return joined;
                }
            }
        return null;
    }
}
