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

    // ── Timing table (identical format to old project) ────────────────────────
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
            var dur   = $"{total.Ms:F0}ms / {total.Ms / 60000.0:F2}min".PadRight(23);
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

        // try/finally guarantees timing table always prints — even on failure
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
            throw; // re-throw so ExceptionHandlingMiddleware returns correct HTTP status
        }
    }

    private async Task<ConsultationGeneratedDto> RunPipelineAsync(
        GenerateConsultationCommand cmd, CancellationToken ct,
        Stopwatch total, List<TimingEntry> timings)
    {
        // ── Step 1: Non-LLM detection ─────────────────────────────────────────
        var sw1 = Stopwatch.StartNew();
        var branches             = branchDetector.Detect(cmd.Situation, cmd.FiscalQuestion);
        var (countries, isIntl)  = countryDetector.Detect(cmd.Situation + " " + cmd.FiscalQuestion);
        var (keywords, entities) = keywordExtractor.Extract(cmd.Situation, cmd.FiscalQuestion);
        sw1.Stop();
        var s1Notes = $"kw={keywords.Count} br=[{string.Join(",", branches)}]";
        timings.Add(new("1. Detection (agents)", sw1.Elapsed.TotalMilliseconds, s1Notes));
        logger.LogInformation("► [STEP 1] ({Ms:F0}ms / {Min:F2}min) | {N}",
            sw1.Elapsed.TotalMilliseconds, sw1.Elapsed.TotalMinutes, s1Notes);

        // ── Step 2: Embed search ───────────────────────────────────────────────
        logger.LogInformation("┌─ [STEP 2] Embed search…");
        var sw2 = Stopwatch.StartNew();
        var query     = cmd.FiscalQuestion + " " + cmd.Situation[..Math.Min(cmd.Situation.Length, 200)];
        var embedSources = await embedAgent.SearchAsync(query, topK: 20);
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
        // KEY FIX: exclude ALL conventions when no country detected
        // A wrong convention is far worse than no convention
        var filteredEmbed = embedSources.Where(s =>
        {
            if (s.DocType != "Convention") return true;
            if (!countries.Any())          return false;
            return countries.Any(c => s.DocName.Contains(c, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        sw2.Stop();
        timings.Add(new("2. Embed search", sw2.Elapsed.TotalMilliseconds,
            $"{filteredEmbed.Count} hits conv={convHints.Count}"));
        logger.LogInformation("└─ [STEP 2] ✓ ({Ms:F0}ms / {Min:F2}min) | {N} results",
            sw2.Elapsed.TotalMilliseconds, sw2.Elapsed.TotalMinutes, filteredEmbed.Count);

        // ── Step 3: Neo4j retrieval ────────────────────────────────────────────
        logger.LogInformation("┌─ [STEP 3] Neo4j retrieval…");
        var sw3 = Stopwatch.StartNew();
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
        CorrectArticleRefs(sources);
        if (sources.Count == 0)
            throw new NoSourcesFoundException(cmd.Situation);
        sw3.Stop();
        var srcTypes = string.Join(" ", sources.GroupBy(s => s.DocType)
            .Select(g => $"{g.Key}:{g.Count()}"));
        timings.Add(new("3. Neo4j retrieval", sw3.Elapsed.TotalMilliseconds,
            $"{sources.Count} src {method}"));
        logger.LogInformation("└─ [STEP 3] ✓ ({Ms:F0}ms / {Min:F2}min) | {N} [{T}] via {M}",
            sw3.Elapsed.TotalMilliseconds, sw3.Elapsed.TotalMinutes,
            sources.Count, srcTypes, method);

        // ── Step 4: LLM Phase 1 ───────────────────────────────────────────────
        logger.LogInformation("┌─ [PHASE 1] contexte / étendue / sommaire / pays…");
        var sw4 = Stopwatch.StartNew();
        var p1Raw = await llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase1Prompt(cmd, sources, isIntl, branches), "Phase1", 2800, ct);

        if (p1Raw is null)
        {
            sw4.Stop();
            timings.Add(new("4. LLM Phase 1", sw4.Elapsed.TotalMilliseconds, "FAILED"));
            logger.LogError("└─ [PHASE 1] ✗ ({Ms:F0}ms)", sw4.Elapsed.TotalMilliseconds);
            throw new ConsultationGenerationException("LLM Phase 1 returned null");
        }

        // ── Reflective Loop: retry Phase 1 once if JSON is malformed ──────────
        var p1 = ParseJsonDict(p1Raw);
        if (p1 is null)
        {
            logger.LogWarning("│  [PHASE 1] JSON malformed — retrying with correction prompt");
            var retryPrompt = BuildPhase1Prompt(cmd, sources, isIntl, branches) +
                "\n\nATTENTION: Votre reponse precedente n'etait pas du JSON valide. " +
                "Repondez UNIQUEMENT avec le JSON demande, sans texte avant ou apres.";
            var retryRaw = await llmAgent.CompleteAsync(SystemPrompt, retryPrompt,
                "Phase1-Retry", 2800, ct);
            p1 = retryRaw is not null ? ParseJsonDict(retryRaw) : null;
            if (p1 is null)
            {
                sw4.Stop();
                timings.Add(new("4. LLM Phase 1", sw4.Elapsed.TotalMilliseconds, "JSON FAILED x2"));
                logger.LogError("└─ [PHASE 1] ✗ JSON parse failed after retry");
                throw new ConsultationGenerationException("LLM Phase 1 returned invalid JSON after retry");
            }
        }

        var etendueItems = GetList(p1, "etendue_items");
        var sommaire     = GetStr(p1, "sommaire_executif");
        sw4.Stop();
        timings.Add(new("4. LLM Phase 1", sw4.Elapsed.TotalMilliseconds,
            $"{etendueItems.Count} items"));
        logger.LogInformation("└─ [PHASE 1] ✓ ({Ms:F0}ms / {Min:F2}min) | {N} items",
            sw4.Elapsed.TotalMilliseconds, sw4.Elapsed.TotalMinutes, etendueItems.Count);

        // ── Step 4b: Post-Phase-1 country detection ───────────────────────────
        // The LLM knows company origins from training — detects country even if not explicit
        var detectedCountry = GetStr(p1, "pays_non_resident")
            ?.Trim().ToLower()
            .Replace("é","e").Replace("è","e").Replace("ê","e").Replace("â","a");
        if (!string.IsNullOrEmpty(detectedCountry) && !countries.Contains(detectedCountry))
        {
            logger.LogInformation("► [STEP 4b] Phase 1 detected country: '{C}'", detectedCountry);
            countries.Add(detectedCountry);
            isIntl = true;
            var sw4b     = Stopwatch.StartNew();
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
                sw4b.Stop();
                timings.Add(new("4b. Convention fetch", sw4b.Elapsed.TotalMilliseconds,
                    $"{detectedCountry} +{newConv.Count}"));
                logger.LogInformation(
                    "└─ [STEP 4b] ✓ ({Ms:F0}ms) | +{N} convention chunks for '{C}'",
                    sw4b.Elapsed.TotalMilliseconds, newConv.Count, detectedCountry);
            }
            else
            {
                sw4b.Stop();
                logger.LogWarning("└─ [STEP 4b] No convention found for '{C}' in KB", detectedCountry);
            }
        }

        // ── Step 5: LLM Phase 2 + 3 — PARALLEL ───────────────────────────────
        logger.LogInformation("┌─ [PHASE 2+3] analyses ‖ table — parallel…");
        var sw5   = Stopwatch.StartNew();
        var task2 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase2Prompt(cmd, sources, etendueItems, sommaire, isIntl, branches),
            "Phase2", 3800, ct);
        var task3 = llmAgent.CompleteAsync(SystemPrompt,
            BuildPhase3Prompt(cmd, sources, etendueItems),
            "Phase3", 3500, ct);
        await Task.WhenAll(task2, task3);

        var p2Raw = task2.Result;
        var p3Raw = task3.Result;

        if (p2Raw is null)
        {
            sw5.Stop();
            timings.Add(new("5. LLM Phase 2+3", sw5.Elapsed.TotalMilliseconds, "PHASE 2 FAILED"));
            logger.LogError("└─ [PHASE 2+3] ✗ Phase 2 returned null ({Ms:F0}ms)",
                sw5.Elapsed.TotalMilliseconds);
            throw new ConsultationGenerationException("LLM Phase 2 returned null");
        }

        var p2    = ParseJsonDict(p2Raw) ?? new Dictionary<string, JsonElement>();
        var p3    = p3Raw is not null ? ParseJsonDict(p3Raw) : null;
        var table = ParseTable(p3);

        if (p3Raw is null)
            logger.LogWarning("│  [PHASE 3] returned null — table will be empty");

        sw5.Stop();
        timings.Add(new("5. LLM Phase 2+3 (‖)", sw5.Elapsed.TotalMilliseconds,
            $"table={table.Count}/{etendueItems.Count}"));
        logger.LogInformation("└─ [PHASE 2+3] ✓ ({Ms:F0}ms / {Min:F2}min) | table={T}/{N}",
            sw5.Elapsed.TotalMilliseconds, sw5.Elapsed.TotalMinutes,
            table.Count, etendueItems.Count);

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
            ElapsedMs       = total.Elapsed.TotalMilliseconds,
        };

        // ── Step 7: Document generation ───────────────────────────────────────
        var sw7 = Stopwatch.StartNew();
        byte[] docBytes;
        try
        {
            docBytes = docAgent.Generate(new GenerateDocumentRequest(
                cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion,
                cmd.Documents, output));
        }
        catch (Exception ex)
        {
            sw7.Stop();
            logger.LogError(ex, "│  [DOC] Generation failed — returning empty document");
            docBytes = Array.Empty<byte>();
        }
        sw7.Stop();
        timings.Add(new("6. Document generation", sw7.Elapsed.TotalMilliseconds, "Word .docx"));
        logger.LogInformation("► [STEP 6] Document generated ({Ms:F0}ms)",
            sw7.Elapsed.TotalMilliseconds);

        // ── Step 8: Persist (fire-and-forget) ─────────────────────────────────
        var aggregate = FiscalPlatform.Domain.Aggregates.Consultation.Consultation.Create(
            cmd.Reference, cmd.ClientName, cmd.Situation, cmd.FiscalQuestion,
            cmd.Documents,
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
            $"PHASE 1 — JSON avec 6 cles exactes.\n\n" +
            $"Client    : {cmd.ClientName}\n" +
            $"Situation : {cmd.Situation}\n" +
            $"Question  : {cmd.FiscalQuestion}\n" +
            $"Ordre     : {(isIntl ? "Convention -> Codes -> LdF -> Doctrine" : "Codes -> LdF -> Doctrine")}\n" +
            $"Branches  : {string.Join(", ", branches)}\n" +
            attachedNote + "\n\n" +
            SourcesBlock(sources) +
            "\nREGLES:\n" +
            "- etendue_items: UNIQUEMENT les points que le client a explicitement demandes. ZERO ajout.\n" +
            "- contexte_faits: faits purs, ZERO citation. Debut: \"Nous comprenons que :\"\n" +
            "- etendue: section 1.2, utilise etendue_items.\n" +
            "- abbreviations: SIGLE : Definition\n" +
            "- sommaire_executif: verdicts concis, max 1 [Sn] par point.\n" +
            "- pays_non_resident: pays de residence de la partie etrangere (ex: france, maroc, allemagne). " +
            "Identifier depuis le contexte meme si non mentionne explicitement (nom de societe, groupe, devise). " +
            "Laisser vide si transaction purement tunisienne.\n\n" +
            "{\"etendue_items\":[],\"contexte_faits\":\"\",\"etendue\":\"\"," +
            "\"abbreviations\":\"\",\"sommaire_executif\":\"\",\"pays_non_resident\":\"\"}";
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
