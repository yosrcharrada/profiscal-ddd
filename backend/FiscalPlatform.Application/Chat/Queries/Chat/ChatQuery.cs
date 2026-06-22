using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Chat.Queries.Chat;

public sealed record ChatQuery(string Question, List<string> History) : IRequest<ChatResponseDto>;
public sealed record ChatResponseDto(string Answer, List<SourceChunkDto> Sources, double ElapsedMs);

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
    ILogger<ChatQueryHandler> logger)
    : IRequestHandler<ChatQuery, ChatResponseDto>
{
    private const int MaxPlanningRounds = 2;
    private const int MaxSources        = 10;

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
        "un taux ou un article. Markdown autorisé (titres, listes, gras).";

    public async Task<ChatResponseDto> Handle(ChatQuery query, CancellationToken ct)
    {
        var sw       = System.Diagnostics.Stopwatch.StartNew();
        var question = (query.Question ?? "").Trim();
        logger.LogInformation("┌─ [CHAT-AGENT] '{Q}'", question[..Math.Min(question.Length, 60)]);

        var historyText  = BuildHistoryText(query.History);
        var accumulated  = new List<SourceChunkDto>();
        var seen         = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observations = new StringBuilder();
        var ready        = false;

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
                // Robust fallback: one semantic search on the raw question, then stop planning.
                if (round == 1)
                    plan = new ChatPlan(new() { new ChatToolCall("semantic_search", question, null, null) }, false);
                else { ready = true; break; }
            }

            ready = plan.ReadyToAnswer || plan.ToolCalls.Count == 0;
            if (plan.ToolCalls.Count == 0) break;

            // Act: dispatch every requested tool concurrently (the key ReAct trick).
            var results = await Task.WhenAll(plan.ToolCalls.Select(tc => ExecuteToolAsync(tc, ct)));

            // Observe: fold results into memory with explicit empties flagged.
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

        // ── Rank, cap, and number the sources for the answer ──────────────────
        var finalSources = accumulated.OrderByDescending(s => s.Score).Take(MaxSources).ToList();

        // ── Reason: produce the grounded, cited answer ────────────────────────
        string answer;
        if (finalSources.Count == 0)
        {
            var directUser =
                (historyText.Length > 0 ? $"CONVERSATION:\n{historyText}\n\n" : "") +
                $"QUESTION: {question}\n\n" +
                "Aucune source juridique n'a été trouvée. Si la question est conversationnelle, " +
                "réponds normalement et brièvement. Sinon, indique clairement qu'aucune source ne " +
                "couvre ce point et invite à reformuler ou préciser.";
            answer = await llm.CompleteAsync(AnswerSystem, directUser, "Chat-Answer", 1200, ct)
                     ?? "Je n'ai pas pu répondre.";
        }
        else
        {
            var srcBlock = new StringBuilder();
            for (var i = 0; i < finalSources.Count; i++)
            {
                var s       = finalSources[i];
                var txt     = s.Text ?? "";
                var preview = txt.Length > 600 ? txt[..600] + "…" : txt;
                srcBlock.AppendLine($"[Source {i + 1}] {s.Category} — {s.DocName} {s.ArticleRef}".TrimEnd());
                srcBlock.AppendLine(preview);
                srcBlock.AppendLine();
            }
            var answerUser =
                (historyText.Length > 0 ? $"CONVERSATION:\n{historyText}\n\n" : "") +
                $"SOURCES:\n{srcBlock}\n" +
                $"QUESTION: {question}\n\n" +
                "Réponds en citant [Source N] pour chaque élément.";
            answer = await llm.CompleteAsync(AnswerSystem, answerUser, "Chat-Answer", 1600, ct)
                     ?? "Je n'ai pas pu répondre.";
        }

        sw.Stop();
        logger.LogInformation("└─ [CHAT-AGENT] ✓ {Ms:F0}ms | {N} sources",
            sw.Elapsed.TotalMilliseconds, finalSources.Count);
        return new ChatResponseDto(answer, finalSources, sw.Elapsed.TotalMilliseconds);
    }

    // ── Tool execution (the agent's real Actions) ─────────────────────────────
    private async Task<List<SourceChunkDto>> ExecuteToolAsync(ChatToolCall tc, CancellationToken ct)
    {
        try
        {
            switch (tc.Tool)
            {
                case "semantic_search":
                    return (await embed.SearchAsync(tc.Query ?? "", topK: 8)).Select(ToChunk).ToList();

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
                    return await retrieval.KeywordFallbackAsync(tc.Query ?? "", topK: 8);

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
