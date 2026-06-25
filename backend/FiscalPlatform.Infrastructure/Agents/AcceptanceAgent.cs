using System.Text.Json;
using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Acceptance agent (LLM-as-judge). One focused, token-cheap GPT-4o call that scores a draft
/// consultation on faithfulness / completeness / groundedness and returns ACCEPT or REVISE.
/// Fails OPEN (accepts) on any LLM/parse failure so it never blocks a usable draft — the
/// deterministic guardrails remain the hard gate.
/// </summary>
public sealed class AcceptanceAgent(ILlmAgent llm, ILogger<AcceptanceAgent> logger) : IAcceptanceAgent
{
    private const string JudgeSystem =
        "Tu es un relecteur fiscal senior EY. Tu ÉVALUES un projet de consultation — tu ne le " +
        "réécris pas. Vérifie 3 points:\n" +
        "1. COMPLÉTUDE: la QUESTION posée reçoit-elle une réponse directe ? Si un TAUX est demandé, " +
        "un pourcentage précis est-il donné et cité [Sn] ? Une réponse \"NON DOCUMENTÉ\" alors qu'un " +
        "taux est demandé = INCOMPLET.\n" +
        "2. FIDÉLITÉ: chaque affirmation (taux, article, verdict) renvoie-t-elle à une source [Sn] ?\n" +
        "3. PERTINENCE: l'analyse s'appuie-t-elle sur les bons textes ?\n\n" +
        "Réponds UNIQUEMENT en JSON:\n" +
        "{\"accept\":true|false,\"score\":0.0-1.0,\"issues\":[\"...\"]," +
        "\"needs_more_sources\":true|false," +
        "\"missing_topics\":[\"ex: taux retenue à la source\",\"ex: Art. 52 CIRPPIS\"]}\n" +
        "accept=false et needs_more_sources=true UNIQUEMENT si une information demandée " +
        "(souvent un taux) manque et nécessite une autre source.";

    public async Task<AcceptanceVerdict> ReviewAsync(AcceptanceRequest req, CancellationToken ct = default)
    {
        var user =
            $"QUESTION:\n{req.FiscalQuestion}\n\n" +
            $"ÉTENDUE:\n{req.Etendue}\n\n" +
            $"PROJET D'ANALYSE:\n{Trunc(req.Analyses, 6000)}\n\n" +
            $"SOURCES DISPONIBLES:\n{Trunc(req.SourcesList, 1500)}\n\n" +
            "Évalue ce projet. Réponds en JSON.";

        string? raw;
        try { raw = await llm.CompleteAsync(JudgeSystem, user, "Acceptance", 600, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "[ACCEPT] judge call failed — accepting"); return Pass(); }

        if (string.IsNullOrWhiteSpace(raw)) return Pass();

        try
        {
            var s = raw.IndexOf('{'); var e = raw.LastIndexOf('}');
            if (s < 0 || e <= s) return Pass();
            using var doc = JsonDocument.Parse(raw[s..(e + 1)]);
            var root = doc.RootElement;

            var accept = !root.TryGetProperty("accept", out var a) || a.ValueKind != JsonValueKind.False;
            var score  = root.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0.8;
            var needs  = root.TryGetProperty("needs_more_sources", out var n) && n.ValueKind == JsonValueKind.True;
            var issues = StrList(root, "issues");
            var topics = StrList(root, "missing_topics");

            var verdict = new AcceptanceVerdict(accept, score, issues, needs, topics);
            logger.LogInformation("[ACCEPT] accept={A} score={S:F2} needsMore={N} issues={I} topics=[{T}]",
                verdict.Accept, verdict.Score, verdict.NeedsMoreSources, issues.Count, string.Join(", ", topics));
            return verdict;
        }
        catch (Exception ex) { logger.LogWarning(ex, "[ACCEPT] parse failed — accepting"); return Pass(); }
    }

    private static AcceptanceVerdict Pass() => new(true, 0.8, new(), false, new());

    private static List<string> StrList(JsonElement root, string key) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
               .Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : new();

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s[..n] + "…" : s);
}
