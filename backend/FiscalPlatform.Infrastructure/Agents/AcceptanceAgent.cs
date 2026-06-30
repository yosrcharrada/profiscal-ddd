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
        "Tu es un relecteur fiscal senior EY. Tu ÉVALUES un projet de consultation (tu ne le " +
        "réécris pas) et tu listes TOUTES ses faiblesses, sur ces critères:\n" +
        "1. COMPLÉTUDE: chaque point de l'ÉTENDUE / chaque question reçoit-il une réponse directe ? " +
        "Si un taux/montant est demandé, un chiffre précis est-il donné et cité [Sn] ? Un " +
        "\"NON DOCUMENTÉ\" alors que l'info est attendue = faiblesse.\n" +
        "2. FIDÉLITÉ / ANCRAGE: chaque affirmation (taux, article, verdict) renvoie-t-elle à une " +
        "source [Sn] ? Toute affirmation non sourcée = hallucination à corriger.\n" +
        "3. PERTINENCE DES SOURCES: les bons textes sont-ils cités ? Article manifestement faux ou " +
        "hors sujet ?\n" +
        "4. DÉCISION: un SEUL verdict clair par point (OUI/NON/X%/EXONÉRÉ/SOUMIS) ? Pas de verdicts " +
        "conditionnels (\"Si X alors Y\") ni d'hésitation ?\n" +
        "5. APPLICATION AUX FAITS: l'analyse est-elle appliquée aux faits précis du client (pas " +
        "une réponse générique) ?\n" +
        "6. COHÉRENCE: pas de contradictions internes (ex: analyse dit X mais conclusion dit autre " +
        "chose) ?\n" +
        "7. TON: document final professionnel, sans raisonnement à voix haute (\"Détermination\", " +
        "\"le scénario applicable\", \"sur la base du fait établi\") ?\n" +
        "8. ÉTAPES OBLIGATOIRES: pour un prestataire étranger, le risque d'établissement stable " +
        "est-il traité (droit commun PUIS Art.5 convention) avant la conclusion ?\n" +
        "9. TAUX LE PLUS FAVORABLE: le taux/traitement retenu est-il le PLUS FAVORABLE légalement " +
        "applicable (convention vs droit commun) dont toutes les conditions sont remplies ? Si la " +
        "convention réduit/exonère et que le projet applique quand même le taux de droit commun (ex: " +
        "RS 15% Art.52) sans écarter la convention par une justification = faiblesse. En l'absence d'ES " +
        "et hors redevance, le revenu est un bénéfice d'entreprise imposable seulement dans l'État de " +
        "résidence (pas de RS en Tunisie) : vérifier que cette issue n'a pas été manquée.\n\n" +
        "Réponds UNIQUEMENT en JSON:\n" +
        "{\"accept\":true|false,\"score\":0.0-1.0,\"issues\":[\"faiblesse concrète\"]," +
        "\"needs_more_sources\":true|false," +
        "\"missing_topics\":[\"ex: taux retenue à la source\",\"ex: Art. 52 CIRPPIS\"]," +
        "\"revision_instructions\":\"consignes précises pour corriger le projet\"}\n" +
        "accept=false dès qu'il existe une faiblesse réelle (pas seulement un taux manquant). " +
        "needs_more_sources=true UNIQUEMENT si la correction exige une source absente; sinon false " +
        "(la faiblesse est corrigeable avec les sources déjà fournies).";

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
            var guidance = root.TryGetProperty("revision_instructions", out var g) && g.ValueKind == JsonValueKind.String
                ? g.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(guidance) && issues.Count > 0)
                guidance = "Corrige: " + string.Join(" ; ", issues);

            var verdict = new AcceptanceVerdict(accept, score, issues, needs, topics, guidance);
            logger.LogInformation("[ACCEPT] accept={A} score={S:F2} needsMore={N} issues={I} topics=[{T}]",
                verdict.Accept, verdict.Score, verdict.NeedsMoreSources, issues.Count, string.Join(", ", topics));
            return verdict;
        }
        catch (Exception ex) { logger.LogWarning(ex, "[ACCEPT] parse failed — accepting"); return Pass(); }
    }

    private static AcceptanceVerdict Pass() => new(true, 0.8, new(), false, new(), "");

    private static List<string> StrList(JsonElement root, string key) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
               .Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : new();

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s[..n] + "…" : s);
}
