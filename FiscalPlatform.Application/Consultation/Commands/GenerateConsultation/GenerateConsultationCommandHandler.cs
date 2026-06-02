using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Domain.Aggregates.Consultation;
using FiscalPlatform.Domain.Exceptions;
using FiscalPlatform.Domain.Repositories;
using FiscalPlatform.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;

/// <summary>
/// Orchestrates the full consultation generation pipeline.
/// Pattern: Parallel Execution inside Orchestrator.
/// LLM is called exactly 3 times (Phase 1 + Phase 2‖Phase 3 parallel).
/// All other steps are non-LLM agents — fast, deterministic.
/// Target: under 1 minute total.
/// Guardrails are applied via FiscalGuardrails in Infrastructure layer.
/// </summary>
public sealed class GenerateConsultationCommandHandler(
    IBranchDetector          branchDetector,
    ICountryDetector         countryDetector,
    IKeywordExtractor        keywordExtractor,
    IEmbedSearchAgent        embedAgent,
    IRetrievalAgent          retrievalAgent,
    ILlmAgent                llmAgent,
    IDocumentGenerationAgent docAgent,
    IConsultationRepository  repository,
    ILogger<GenerateConsultationCommandHandler> logger)
    : IRequestHandler<GenerateConsultationCommand, ConsultationGeneratedDto>
{
    private const string SystemPrompt =
        "Tu es Faiez Choyakh — fiscaliste tunisien senior, EY Tunisia.\n" +
        "CITATIONS: Cite uniquement via [S1],[S2]... Jamais de nom de document en clair.\n" +
        "Ne jamais inventer un article. Quoting = copie exacte du texte [Sn].\n" +
        "ETENDUE: Inclure UNIQUEMENT ce que le client a demande explicitement. Zero ajout.\n" +
        "SOURCES: Appliquer le principe general de [Sn] au cas specifique.\n" +
        "Dernier recours seulement: Ce point necessite des sources non disponibles. Verdict: NON DOCUMENTE.\n" +
        "ORDRE: International: Convention -> Codes -> LdF -> Doctrine. Local: Codes -> LdF -> Doctrine.\n" +
        "VERDICTS: OUI/NON/X%/EXONERE/SOUMIS/DEDUCTIBLE. JSON pur uniquement.";

    public async Task<ConsultationGeneratedDto> Handle(
        GenerateConsultationCommand cmd, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("╔══ GENERATE [{Ref}] — {Client} ══╗", cmd.Reference, cmd.ClientName);

        // ── Step 1: Non-LLM Agent detection (Conditional Branching pattern) ──
        var branches             = branchDetector.Detect(cmd.Situation, cmd.FiscalQuestion);
        var (countries, isIntl)  = countryDetector.Detect(cmd.Situation + " " + cmd.FiscalQuestion);
        var (keywords, entities) = keywordExtractor.Extract(cmd.Situation, cmd.FiscalQuestion);
        logger.LogInformation("  ► Agents: branches=[{B}] countries=[{C}] kw={K}",
            string.Join(",", branches), string.Join(",", countries), keywords.Count);

        // ── Step 2: Embed Agent — semantic search (non-LLM) ──────────────────
        var query        = cmd.FiscalQuestion + " " + cmd.Situation[..Math.Min(cmd.Situation.Length, 200)];
        var embedSources = await embedAgent.SearchAsync(query, topK: 20);

        // Scoped convention embed per country (Hierarchical Delegation pattern)
        var convHints = new List<LegalSourceDto>();
        if (isIntl)
        {
            foreach (var country in countries)
            {
                var hits     = await embedAgent.SearchScopedAsync(query, country, topK: 8);
                var filtered = hits.Where(s =>
                    s.DocType == "Convention" ||
                    s.DocName.Contains(country, StringComparison.OrdinalIgnoreCase)).ToList();
                convHints.AddRange(filtered);
            }
        }

        // Filter wrong-country conventions
        var filteredEmbed = embedSources.Where(s =>
        {
            if (s.DocType != "Convention") return true;
            if (!countries.Any())          return true;
            return countries.Any(c => s.DocName.Contains(c, StringComparison.OrdinalIgnoreCase));
        }).ToList();

        logger.LogInformation("  ► EmbedAgent: {E} results | convHints={C}",
            filteredEmbed.Count, convHints.Count);

        // ── Step 3: Retrieval Agent — graph queries (non-LLM) ────────────────
        List<LegalSourceDto> sources;
        string method;

        if (filteredEmbed.Count >= 5)
        {
            var neo4j = await retrievalAgent.RetrieveSourcesAsync(
                keywords, entities, countries, isIntl, branches, convHints, 15, ct);
            sources = MergeAndDiversify(filteredEmbed, neo4j, 30);
            method  = "vector+keyword";
        }
        else
        {
            sources = await retrievalAgent.RetrieveSourcesAsync(
                keywords, entities, countries, isIntl, branches, convHints, 30, ct);
            method  = "keyword+graph";
        }

        // Runtime article_ref correction
        CorrectArticleRefs(sources);

        if (sources.Count == 0)
            throw new NoSourcesFoundException(cmd.Situation);

        logger.LogInformation("  ► {N} sources [{T}] via {M}",
            sources.Count,
            string.Join(" ", sources.GroupBy(s => s.DocType)
                .Select(g => $"{g.Key}:{g.Count()}")),
            method);

        // ── Step 4: LLM Agent Phase 1 ─────────────────────────────────────────
        var p1Raw = await llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase1Prompt(cmd, sources, isIntl, branches),
            "Phase1", 2800, ct)
            ?? throw new ConsultationGenerationException("LLM Phase 1 returned null");

        var p1           = ParseJsonDict(p1Raw);
        var etendueItems = GetList(p1, "etendue_items");
        var sommaire     = GetStr(p1, "sommaire_executif");
        logger.LogInformation("  ► Phase 1: {N} étendue items", etendueItems.Count);

        // ── Step 5: LLM Phase 2 + 3 — PARALLEL ───────────────────────────────
        var t2 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase2Prompt(cmd, sources, etendueItems, sommaire, isIntl, branches),
            "Phase2", 3800, ct);
        var t3 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase3Prompt(cmd, sources, etendueItems),
            "Phase3", 3500, ct);
        await Task.WhenAll(t2, t3);

        var p2    = ParseJsonDict(t2.Result ?? "{}");
        var p3    = t3.Result is not null ? ParseJsonDict(t3.Result) : null;
        var table = ParseTable(p3);
        logger.LogInformation("  ► Phase 2+3: table={T}/{N}", table.Count, etendueItems.Count);

        // ── Step 6: Build output + resolve citations ──────────────────────────
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
            ElapsedMs       = sw.Elapsed.TotalMilliseconds,
        };

        // ── Step 7: Document Generation Agent (non-LLM) ───────────────────────
        var docRequest = new GenerateDocumentRequest(
            cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion,
            cmd.Documents, output);
        var docBytes = docAgent.Generate(docRequest);

        // ── Step 8: Persist aggregate (Event-Driven side effect) ──────────────
        var aggregate = FiscalPlatform.Domain.Aggregates.Consultation.Consultation.Create(
            cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion,
            cmd.Documents,
            branches.Select(b => FiscalPlatform.Domain.ValueObjects.LegalBranch.TryFrom(b))
                    .Where(b => b is not null).Select(b => b!),
            countries, isIntl,
            output.ContexteFaits, output.Etendue, output.Abbreviations,
            output.SommairExecutif, output.Analyses, output.Documents,
            method, sources.Count, sw.Elapsed.TotalMilliseconds);

        _ = repository.SaveAsync(aggregate, ct);

        var safeClient = Regex.Replace(cmd.ClientName.Trim(), @"[^\w\s-]", "")
                              .Trim().Replace(" ", "_");
        var filename = $"Consultation_{safeClient}_{DateTime.Now:dd-MM-yyyy}.docx";

        sw.Stop();
        logger.LogInformation("  ✓ [{Ref}] {Ms:F0}ms | {N} sources | {M}",
            cmd.Reference, sw.Elapsed.TotalMilliseconds, sources.Count, method);

        return new ConsultationGeneratedDto(
            docBytes, filename, output, method, sw.Elapsed.TotalMilliseconds, aggregate.Id);
    }

    // ── Prompt builders ───────────────────────────────────────────────────────

    private static string SourcesBlock(List<LegalSourceDto> sources)
    {
        var sb = new StringBuilder(
            "== SOURCES JURIDIQUES (utiliser uniquement les plus recentes) ==\n\n");
        foreach (var s in sources.Take(15))
        {
            var label   = s.IsExpert ? "COMMENTAIRE EXPERT — Faiez Choyakh" : s.DocType;
            var preview = s.Text.Length > 250 ? s.Text[..250] + "…" : s.Text;
            sb.AppendLine($"[S{s.Index}] {label} | {s.DocName} | {s.Year} | {s.ArticleRef}");
            sb.AppendLine($"       {preview}\n");
        }
        sb.AppendLine($"!! Cite UNIQUEMENT [S1]..[S{Math.Min(sources.Count, 15)}].");
        return sb.ToString();
    }

    private static string BuildPhase1Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, bool isIntl, HashSet<string> branches)
    {
        var attachedNote = cmd.AttachedDocumentTexts?.Any() == true
            ? $"\n\nDOCUMENTS CLIENT ATTACHES ({cmd.AttachedDocumentTexts.Count}):\n" +
              string.Join("\n---\n", cmd.AttachedDocumentTexts
                  .Select((t, i) => $"[DOC-{i + 1}]: {t[..Math.Min(t.Length, 600)]}"))
            : "";

        return
            $"PHASE 1 — JSON avec 5 cles exactes.\n\n" +
            $"Client    : {cmd.ClientName}\n" +
            $"Situation : {cmd.Situation}\n" +
            $"Question  : {cmd.FiscalQuestion}\n" +
            $"Ordre     : {(isIntl ? "Convention -> Codes -> LdF -> Doctrine" : "Codes -> LdF -> Doctrine")}\n" +
            $"Branches  : {string.Join(", ", branches)}\n" +
            attachedNote + "\n\n" +
            SourcesBlock(sources) +
            "\nREGLES:\n" +
            "- etendue_items: UNIQUEMENT les points que le client a explicitement demandes.\n" +
            "- contexte_faits: faits purs, ZERO citation. Debut: \"Nous comprenons que :\"\n" +
            "- etendue: section 1.2, utilise etendue_items.\n" +
            "- abbreviations: SIGLE : Definition\n" +
            "- sommaire_executif: verdicts concis, max 1 [Sn] par point.\n\n" +
            "{\"etendue_items\":[],\"contexte_faits\":\"\",\"etendue\":\"\",\"abbreviations\":\"\",\"sommaire_executif\":\"\"}";
    }

    private static string BuildPhase2Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, List<string> etendueItems, string sommaire,
        bool isIntl, HashSet<string> branches)
    {
        var n  = etendueItems.Count;
        var et = string.Join("\n", etendueItems.Select((x, i) => $"  {i + 1}. {x}"));
        var bg = new StringBuilder();
        if (branches.Contains("IS"))
            bg.AppendLine("  IS -> citer Art. 45/47 CIRPPIS (personnes morales, benefices passibles)");
        if (branches.Contains("TVA"))
            bg.AppendLine("  TVA -> citer CTVA (soumises, affaires, activites en Tunisie)");
        if (branches.Contains("IRPP"))
            bg.AppendLine("  IRPP -> citer section IRPP CIRPPIS (revenu, personne physique)");
        if (branches.Contains("Retenue"))
            bg.AppendLine("  Retenue -> CIRPPIS retenue + convention si intl");
        if (branches.Contains("PrixTransfert"))
            bg.AppendLine("  PrixTransfert -> Art. 48 septies CIRPPIS + CDPF 17 bis/ter");

        return
            $"PHASE 2 — JSON avec 1 cle: analyses.\n\n" +
            $"Client : {cmd.ClientName} | Question : {cmd.FiscalQuestion}\n\n" +
            $"ETENDUE ({n} points):\n{et}\n\n" +
            SourcesBlock(sources) +
            $"\nORDRE: {(isIntl ? "Convention -> Codes -> LdF -> Doctrine" : "Codes -> LdF -> Doctrine")}\n" +
            bg +
            $"\nFORMAT {n} blocs 4.1 a 4.{n}:\n" +
            "  4.X [Titre]\n" +
            "  Principe applicable : [Sn] : \"citation exacte\".\n" +
            "  Application au cas : appliquer le principe general aux faits.\n" +
            "  Conclusion : VERDICT — justification.\n\n" +
            "[Sn] OBLIGATOIRE par bloc.\n\n" +
            "{\"analyses\":\"4. ANALYSES\\n\\n[blocs]\"}";
    }

    private static string BuildPhase3Prompt(GenerateConsultationCommand cmd,
        List<LegalSourceDto> sources, List<string> etendueItems)
    {
        var lst = string.Join("\n", sources.Take(15)
            .Select(s => $"  [S{s.Index}] {s.DocType} | {s.DocName} ({s.Year}) — {s.ArticleRef}"));
        return
            $"PHASE 3 — JSON: documents + analysis_table ({etendueItems.Count} objets).\n\n" +
            $"Client: {cmd.ClientName}\nSOURCES:\n{lst}\n\n" +
            "{\"documents\":\"5. REFERENCES\\n\\n[sources citees]\"," +
            "\"analysis_table\":[{\"sujet\":\"\",\"analyse\":\"Selon [Sn]: \",\"conclusion\":\"OUI/NON\"}]}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void CorrectArticleRefs(List<LegalSourceDto> sources)
    {
        foreach (var s in sources)
        {
            var m = Regex.Match(s.Text,
                @"^ARTICLE\s+(\d+[\w\s]*?)\s*[:\n]", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var real = "Art. " + m.Groups[1].Value.Trim();
                if (s.ArticleRef != real) s.ArticleRef = real;
            }
        }
    }

    private static string ResolveCitations(string text, List<LegalSourceDto> sources) =>
        Regex.Replace(text, @"\[S(\d+)\]", m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx)) return m.Value;
            return sources.FirstOrDefault(s => s.Index == idx)?.Citation ?? m.Value;
        });

    private static List<LegalSourceDto> MergeAndDiversify(
        List<LegalSourceDto> primary, List<LegalSourceDto> secondary, int maxTotal)
    {
        static string Key(LegalSourceDto s) =>
            s.DocName + "|" + s.Text[..Math.Min(s.Text.Length, 60)].Trim();
        var seen  = new HashSet<string>(primary.Select(Key));
        var all   = primary.Concat(secondary.Where(s => seen.Add(Key(s)))).ToList();
        var com   = all.Where(r => r.DocType == "Commentaire").OrderByDescending(r => r.Score).ToList();
        var doc   = all.Where(r => r.DocType == "Doctrine").OrderByDescending(r => r.Score).ToList();
        var other = all.Where(r => r.DocType != "Commentaire" && r.DocType != "Doctrine")
                       .OrderBy(r => r.DocType == "Convention" ? 0 : r.DocType == "Code" ? 1 : 2)
                       .ThenByDescending(r => r.Score).ToList();
        var reserved = com.Take(4).Concat(doc.Take(3)).ToList();
        var merged   = other.Take(maxTotal - reserved.Count)
                            .Concat(reserved).Take(maxTotal).ToList();
        for (int i = 0; i < merged.Count; i++) merged[i].Index = i + 1;
        return merged;
    }

    private static List<(string Sujet, string Analyse, string Conclusion)> ParseTable(
        Dictionary<string, JsonElement>? p3)
    {
        if (p3 is null || !p3.TryGetValue("analysis_table", out var tbl)
            || tbl.ValueKind != JsonValueKind.Array) return new();
        return tbl.EnumerateArray()
            .Select(r => (GetElStr(r, "sujet"), GetElStr(r, "analyse"), GetElStr(r, "conclusion")))
            .ToList();
    }

    private static Dictionary<string, JsonElement>? ParseJsonDict(string raw)
    {
        raw = Regex.Replace(raw.Trim(), @"^```(json)?\s*", "", RegexOptions.Multiline);
        raw = Regex.Replace(raw.Trim(), @"\s*```$",          "", RegexOptions.Multiline);
        var s = raw.IndexOf('{'); var e = raw.LastIndexOf('}');
        if (s >= 0 && e > s) raw = raw[s..(e + 1)];
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

    private static string GetElStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
        ? v.GetString() ?? "" : "";
}
