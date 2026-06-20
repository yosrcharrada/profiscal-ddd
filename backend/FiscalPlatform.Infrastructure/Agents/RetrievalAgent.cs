using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Retrieval Agent — wraps all Neo4j graph queries.
/// Legal hierarchy: International: Convention → Code → LoiFinances → Doctrine
///                  Local:         Code → LoiFinances → Doctrine
/// Newest documents: keeps most recent year per doc_name prefix.
/// Arabic filter: skips chunks containing Arabic characters.
/// </summary>
public sealed class RetrievalAgent : IRetrievalAgent, IDisposable
{
    private readonly IDriver               _driver;
    private readonly string                _db;
    private readonly ILogger<RetrievalAgent> _logger;

    private const string F =
        "c.chunk_id AS id, c.text AS text, c.doc_name AS doc_name, " +
        "c.doc_type AS doc_type, c.article_ref AS article_ref, " +
        "c.section_title AS section_title, c.annee AS annee";

    private const string CH =
        "ch.chunk_id AS id, ch.text AS text, ch.doc_name AS doc_name, " +
        "ch.doc_type AS doc_type, ch.article_ref AS article_ref, " +
        "ch.section_title AS section_title, ch.annee AS annee";

    // Field alias for 'next' node in NEXT_CHUNK queries
    private const string Fnext =
        "next.chunk_id AS id, next.text AS text, next.doc_name AS doc_name, " +
        "next.doc_type AS doc_type, next.article_ref AS article_ref, " +
        "next.section_title AS section_title, next.annee AS annee";

    // Note Commune N°2/2015 exact doc_name in Neo4j
    private const string NoteCommune2DocName =
        "note-commune-numero-2-principes-et-modalites-dimposition-des-revenus-de-source-tunisienne-realises-p";

    private static readonly Dictionary<string, int> TypeRank = new()
    {
        ["Convention"]=0, ["Code"]=1, ["LoiFinances"]=2,
        ["Decret"]=3, ["Arrete"]=4, ["Doctrine"]=5, ["Commentaire"]=6,
    };

    // Maps country names (as detected by CountryDetector) to the actual substring
    // that appears in Neo4j convention doc_names.
    // Needed because some doc_names are truncated (100-char limit) or use different forms.
    private static readonly Dictionary<string, string> CountryDocNameFragment =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["france"]          = "republique-fra",  // truncated: '...republique-française' → '...republique-fra'
        ["grece"]           = "helleniq",         // doc uses 'hellenique' not 'grece'
        ["libye"]           = "lyb",              // doc uses 'lybienne' not 'libye'
        ["arabie saoudite"] = "arabie-saoud",     // space vs hyphen
        ["angleterre"]      = "royaume-uni",      // alias: no separate convention
        ["usa"]             = "etats-unis",       // alias
    };

    private static string ToDocFragment(string country) =>
        CountryDocNameFragment.TryGetValue(country, out var frag) ? frag : country;

    public RetrievalAgent(IConfiguration config, ILogger<RetrievalAgent> logger)
    {
        _logger = logger;
        var uri  = (config["Neo4j:Uri"]      is { Length: > 0 } u) ? u
                 : (Environment.GetEnvironmentVariable("NEO4J_URI")      ?? "neo4j://127.0.0.1:7687");
        var user = (config["Neo4j:Username"] is { Length: > 0 } n) ? n
                 : (Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j");
        var pass = (config["Neo4j:Password"] is { Length: > 0 } p) ? p
                 : (Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "neo4j");
        _db      = (config["Neo4j:Database"] is { Length: > 0 } d) ? d
                 : (Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "tunisian-fiscal");
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HEALTH + STATS
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> IsAliveAsync()
    {
        try
        {
            await using var s = _driver.AsyncSession(o => o.WithDatabase(_db));
            await s.RunAsync("RETURN 1");
            return true;
        }
        catch { return false; }
    }

    public async Task<KnowledgeBaseStatsDto> GetStatsAsync()
    {
        var stats = new KnowledgeBaseStatsDto();
        try
        {
            await using var s = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await s.RunAsync(@"
                MATCH (c:Chunk)      WITH count(c) AS chunks
                MATCH (e:Entity)     WITH chunks, count(e) AS ents
                MATCH ()-[rel]->()   WITH chunks, ents, count(rel) AS rels
                RETURN chunks, ents, rels");
            var rec = await r.SingleAsync();
            stats.TotalChunks    = rec["chunks"].As<long>();
            stats.TotalEntities  = rec["ents"].As<long>();
            stats.TotalRelations = rec["rels"].As<long>();

            foreach (var (dt, prop) in new[]
            {
                ("LoiFinances","LoisCount"), ("Doctrine","NotesCount"),
                ("Code","CodesCount"),       ("Convention","ConventionsCount"),
            })
            {
                var cr = await s.RunAsync(
                    "MATCH (c:Chunk {doc_type:$dt}) RETURN count(c) AS n", new { dt });
                var n = await cr.SingleAsync();
                switch (prop)
                {
                    case "LoisCount":        stats.LoisCount        = n["n"].As<long>(); break;
                    case "NotesCount":       stats.NotesCount       = n["n"].As<long>(); break;
                    case "CodesCount":       stats.CodesCount       = n["n"].As<long>(); break;
                    case "ConventionsCount": stats.ConventionsCount = n["n"].As<long>(); break;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetStats failed"); }
        return stats;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MAIN RETRIEVAL
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LegalSourceDto>> RetrieveSourcesAsync(
        List<string> keywords, List<string> entities, List<string> countries,
        bool isInternational, HashSet<string> branches,
        List<LegalSourceDto> conventionEmbedHints, int maxResults = 30,
        CancellationToken ct = default)
    {
        var all  = new List<LegalSourceDto>();
        var seen = new HashSet<string>();

        await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

        // ── STEP 0: Convention (highest authority for international cases) ──
        if (isInternational && countries.Any())
        {
            if (conventionEmbedHints.Any())
            {
                int added = 0;
                foreach (var h in conventionEmbedHints)
                {
                    var deduKey = !string.IsNullOrEmpty(h.ChunkId)
                        ? h.ChunkId
                        : h.DocName + "|" + h.Text[..Math.Min(h.Text.Length, 60)];
                    if (seen.Add(deduKey)) { all.Add(h); added++; }
                    if (added >= 6) break;
                }
            }
            else
            {
                all.AddRange(await ConventionKeywordSearchAsync(
                    session, countries, keywords, seen));
            }
        }

        // ── STEP 1: Branch-guided code fetch ─────────────────────────────────
        if (branches.Contains("IS"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "s'applique", "personnes morales", "exercant",
                        "benefices passibles", "etablissements situes",
                        "benefice imposable", "resultat", "impot sur les societes" },
                seen, limit: 5));
        }
        if (branches.Contains("IRPP"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "revenu", "personne physique", "retenue bareme",
                        "categorie", "traitements" },
                seen, limit: 4));
        }
        if (branches.Contains("TVA"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "ctva",
                new[] { "soumises", "affaires", "activites", "assujetti", "exoner" },
                seen, limit: 4));
        }
        if (branches.Contains("Retenue"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "retenue a la source", "non-resident",
                        "art. 52", "prestataire" },
                seen, limit: 4));
        }
        if (branches.Contains("PrixTransfert"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "48 septies", "pleine concurrence",
                        "parties liees", "prix de transfert" },
                seen, limit: 4));
            all.AddRange(await TargetedDocFetchAsync(session, "cdpf", seen, limit: 3));
        }

        // ── STEP 2: Lois de Finances, Doctrine, Commentaire ──────────────────
        if (all.Count < maxResults)
        {
            var expanded = keywords.Concat(entities).Distinct().ToList();
            int perType  = Math.Max(2, (maxResults - all.Count) / 3);
            foreach (var dt in new[] { "LoiFinances", "Doctrine", "Commentaire" })
                all.AddRange(await KeywordSearchByTypeAsync(
                    session, dt, expanded, entities, perType, seen));
        }

        // ── STEP 3: Neighbor expansion ────────────────────────────────────────
        var seeds = all.Take(10)
                       .Where(r => !string.IsNullOrEmpty(r.ChunkId))
                       .Select(r => r.ChunkId).ToList();
        all.AddRange(await NeighborExpandAsync(session, seeds, seen));

        // ── STEP 4: Diversity + newest documents ──────────────────────────────
        var final = EnsureDiversityAndNewest(all, maxResults);
        for (int i = 0; i < final.Count; i++) final[i].Index = i + 1;

        _logger.LogInformation(
            "RetrievalAgent: {T} sources | Conv:{C} Code:{Co} Doc:{D} Expert:{E}",
            final.Count,
            final.Count(s => s.DocType == "Convention"),
            final.Count(s => s.DocType == "Code"),
            final.Count(s => s.DocType == "Doctrine"),
            final.Count(s => s.IsExpert));
        return final;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE RETRIEVAL HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<LegalSourceDto>> TargetedDocFetchAsync(
        IAsyncSession session, string docNameKeyword,
        HashSet<string> seen, int limit = 5)
    {
        var results = new List<LegalSourceDto>();
        try
        {
            var res = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE toLower(c.doc_name) CONTAINS toLower($dk)
                  AND c.chunk_type = 'text' AND c.text <> ''
                RETURN {F}, 0.9 AS score
                ORDER BY (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END), c.chunk_id
                LIMIT $lim",
                new { dk = docNameKeyword, lim = limit * 15 });
            await foreach (var r in res)
            {
                if (results.Count >= limit) break;
                var text = r["text"]?.As<string>() ?? "";
                if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.9);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "TargetedFetch {D}", docNameKeyword); }
        return results;
    }

    private async Task<List<LegalSourceDto>> TargetedDocFetchWithKeywordsAsync(
        IAsyncSession session, string docNameKeyword, string[] contentKws,
        HashSet<string> seen, int limit = 5)
    {
        var results = new List<LegalSourceDto>();
        foreach (var kw in contentKws)
        {
            if (results.Count >= limit) break;
            try
            {
                var res = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE toLower(c.doc_name) CONTAINS toLower($dk)
                      AND c.chunk_type = 'text' AND c.text <> ''
                      AND toLower(c.text) CONTAINS toLower($kw)
                    RETURN {F}, 0.9 AS score
                    ORDER BY (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END), c.chunk_id
                    LIMIT 5",
                    new { dk = docNameKeyword, kw });
                await foreach (var r in res)
                {
                    if (results.Count >= limit) break;
                    var text = r["text"]?.As<string>() ?? "";
                    if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.9);
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "FetchWithKw {D}/{K}", docNameKeyword, kw); }
        }
        return results;
    }

    private async Task<List<LegalSourceDto>> ConventionKeywordSearchAsync(
        IAsyncSession session, List<string> countries, List<string> keywords,
        HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        foreach (var country in countries)
        {
            if (results.Count >= 6) break;
            var docFrag = ToDocFragment(country);
            foreach (var kw in keywords.Take(8))
            {
                if (results.Count >= 6) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk {{doc_type:'Convention', chunk_type:'text'}})
                        WHERE toLower(c.doc_name) CONTAINS toLower($docFrag)
                          AND toLower(c.text) CONTAINS toLower($kw)
                        RETURN {F}, 0.85 AS score
                        ORDER BY (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END)
                        LIMIT 2",
                        new { docFrag, kw });
                    await foreach (var r in res) TryAdd(results, seen, r, 0.85);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "ConvKw {C}/{K}", country, kw); }
            }
        }
        return results;
    }

    private async Task<List<LegalSourceDto>> KeywordSearchByTypeAsync(
        IAsyncSession session, string docType, List<string> keywords,
        List<string> entities, int limit, HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        var terms   = keywords.Concat(entities).Distinct().ToList();
        try
        {
            var res = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE c.doc_type = $dt AND c.chunk_type = 'text' AND c.text <> ''
                  AND ANY(kw IN $kws WHERE toLower(c.text) CONTAINS toLower(kw))
                RETURN {F}, 0.7 AS score
                ORDER BY (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END), c.chunk_id
                LIMIT $lim",
                new { dt = docType, kws = terms, lim = limit * 5 });
            await foreach (var r in res)
            {
                if (results.Count >= limit) break;
                var text = r["text"]?.As<string>() ?? "";
                if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.7);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "KwByType {T}", docType); }
        return results;
    }

    private async Task<List<LegalSourceDto>> NeighborExpandAsync(
        IAsyncSession session, List<string> seedIds, HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        if (!seedIds.Any()) return results;

        // a. NEXT_CHUNK
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:NEXT_CHUNK]->(c:Chunk)
                WHERE c.chunk_type = 'text'
                RETURN DISTINCT {F}, 0.75 AS score LIMIT 6",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.75);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "NEXT_CHUNK"); }

        // b. Entity hop
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})<-[:APPEARS_IN]-(e:Entity)-[:APPEARS_IN]->(c:Chunk)
                WHERE c.chunk_type = 'text' AND c.chunk_id <> sid
                RETURN DISTINCT {F}, 0.65 AS score LIMIT 8",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.65);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "EntityHop"); }

        // c. INTERPRETS
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (c:Chunk {{chunk_id: sid}})<-[:INTERPRETS]-(nc:Chunk)
                WHERE nc.chunk_type = 'text'
                RETURN DISTINCT {CH}, 0.7 AS score LIMIT 5",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.7);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "INTERPRETS"); }

        // d. COMMENTS_ON
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (c:Chunk {{chunk_id: sid}})-[:PART_OF]->(ldf:LoiFinances)
                MATCH (com:Commentaire)-[:COMMENTS_ON]->(ldf)
                MATCH (ch:Chunk)-[:PART_OF]->(com) WHERE ch.chunk_type = 'text'
                RETURN DISTINCT {CH}, 0.7 AS score LIMIT 4",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.7);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "COMMENTS_ON"); }

        return results;
    }

    private static List<LegalSourceDto> EnsureDiversityAndNewest(
        List<LegalSourceDto> all, int maxTotal)
    {
        var newestPerDoc = all
            .GroupBy(s => Regex.Replace(
                s.DocName, @"[-_]?\d{4}.*$", "",
                RegexOptions.IgnoreCase).Trim().ToLower())
            .Select(g => g.OrderByDescending(s =>
            {
                var m = Regex.Match(s.Year ?? "", @"\d{4}");
                return m.Success ? int.Parse(m.Value) : 0;
            }).First())
            .ToList();

        var com      = newestPerDoc.Where(r => r.DocType == "Commentaire")
                                   .OrderByDescending(r => r.Score).ToList();
        var doc      = newestPerDoc.Where(r => r.DocType == "Doctrine")
                                   .OrderByDescending(r => r.Score).ToList();
        var other    = newestPerDoc.Where(r => r.DocType != "Commentaire" && r.DocType != "Doctrine")
                                   .OrderBy(r => TypeRank.GetValueOrDefault(r.DocType, 9))
                                   .ThenByDescending(r => r.Score).ToList();
        var reserved = com.Take(4).Concat(doc.Take(3)).ToList();
        return other.Take(maxTotal - reserved.Count).Concat(reserved).Take(maxTotal).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GRAPHRAG CHAT SUPPORT
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<SourceChunkDto>> VectorSearchAsync(float[] embedding, int topK = 8)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await session.RunAsync(@"
                CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                YIELD node AS c, score
                WHERE c.chunk_type = 'text' AND score >= 0.3
                RETURN c.doc_name AS doc_name, c.page_num AS page_num,
                       c.text AS text, c.chunk_type AS chunk_type,
                       c.article_ref AS article_ref, score
                LIMIT $topK",
                new { topK, emb = embedding.Select(f => (double)f).ToArray() });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "VectorSearch failed"); return new(); }
    }

    public async Task<List<SourceChunkDto>> GraphExpandAsync(
        List<string> entities, int topK = 6)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await session.RunAsync(@"
                UNWIND $ents AS ent
                MATCH (e:Entity)-[:APPEARS_IN]->(c:Chunk)
                WHERE (toLower(e.normalized) CONTAINS toLower(ent)
                    OR toLower(e.text) CONTAINS toLower(ent))
                  AND c.chunk_type = 'text'
                RETURN DISTINCT c.doc_name AS doc_name, c.page_num AS page_num,
                       c.text AS text, c.chunk_type AS chunk_type,
                       c.article_ref AS article_ref, 0.6 AS score
                LIMIT $topK",
                new { ents = entities, topK });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GraphExpand failed"); return new(); }
    }

    public async Task<List<SourceChunkDto>> KeywordFallbackAsync(
        string query, int topK = 8)
    {
        try
        {
            var kws = query.ToLower()
                          .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(k => k.Length >= 4).ToList();
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await session.RunAsync(@"
                MATCH (c:Chunk)
                WHERE ANY(kw IN $kws WHERE toLower(c.text) CONTAINS kw)
                RETURN c.doc_name AS doc_name, c.page_num AS page_num,
                       c.text AS text, c.chunk_type AS chunk_type,
                       c.article_ref AS article_ref, 0.5 AS score
                LIMIT $topK",
                new { kws, topK });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "KeywordFallback failed"); return new(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TARGETED FETCH METHODS — called by RetrievalPlannerPlugin (TRUE SK Agent)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<LegalSourceDto>> FetchConventionArticleAsync(
        string country, string[] keywords, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var docFrag = ToDocFragment(country);
            foreach (var kw in keywords.Take(4))
            {
                if (results.Count >= 8) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE toLower(c.doc_name) CONTAINS toLower($docFrag)
                          AND c.doc_type = 'Convention'
                          AND c.chunk_type = 'text'
                          AND toLower(c.text) CONTAINS toLower($kw)
                        RETURN {F}, 0.92 AS score
                        ORDER BY c.annee DESC, c.article_ref
                        LIMIT 3",
                        new { docFrag, kw });
                    await foreach (var r in res)
                    {
                        var text = r["text"]?.As<string>() ?? "";
                        if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.92);
                    }
                }
                catch (Exception ex)
                { _logger.LogDebug(ex, "FetchConvArticle {C}/{K}", country, kw); }
            }

            var seedIds = results.Select(r => r.ChunkId)
                                 .Where(id => !string.IsNullOrEmpty(id)).Take(3).ToList();
            if (seedIds.Any())
            {
                try
                {
                    var nRes = await session.RunAsync($@"
                        MATCH (c:Chunk)-[:NEXT_CHUNK]->(next:Chunk)
                        WHERE c.chunk_id IN $ids AND next.doc_type = 'Convention'
                        RETURN {Fnext}, 0.88 AS score LIMIT 3",
                        new { ids = seedIds });
                    await foreach (var r in nRes)
                    {
                        var text = r["text"]?.As<string>() ?? "";
                        if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.88);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchConvArticle expand"); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchConventionArticle {C}", country); }

        _logger.LogInformation("FetchConventionArticle {C}: {N} chunks", country, results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchNoteCommune2Async(
        string? country, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

            // General principles
            try
            {
                var pRes = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE c.doc_name = $docName AND c.chunk_type = 'text'
                      AND (toLower(c.text) CONTAINS 'résidence'
                           OR toLower(c.text) CONTAINS 'source tunisienne'
                           OR toLower(c.text) CONTAINS 'imposition')
                    RETURN {F}, 0.90 AS score
                    ORDER BY c.annee DESC LIMIT 3",
                    new { docName = NoteCommune2DocName });
                await foreach (var r in pRes) TryAdd(results, seen, r, 0.90);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "NC2 principles"); }

            // Country row in Annexe 1
            if (!string.IsNullOrEmpty(country))
            {
                try
                {
                    var cRes = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE c.doc_name = $docName AND c.chunk_type = 'text'
                          AND toLower(c.text) CONTAINS toLower($country)
                        RETURN {F}, 0.95 AS score
                        ORDER BY c.annee DESC LIMIT 4",
                        new { docName = NoteCommune2DocName, country });
                    await foreach (var r in cRes) TryAdd(results, seen, r, 0.95);

                    var countryIds = results
                        .Where(r => r.Text.Contains(country, StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.ChunkId)
                        .Where(id => !string.IsNullOrEmpty(id)).Take(2).ToList();

                    if (countryIds.Any())
                    {
                        var nRes = await session.RunAsync($@"
                            MATCH (c:Chunk)-[:NEXT_CHUNK]->(next:Chunk)
                            WHERE c.chunk_id IN $ids AND next.doc_name = $docName
                            RETURN {Fnext}, 0.88 AS score LIMIT 3",
                            new { ids = countryIds, docName = NoteCommune2DocName });
                        await foreach (var r in nRes) TryAdd(results, seen, r, 0.88);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "NC2 country {C}", country); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchNoteCommune2"); }

        _logger.LogInformation("FetchNoteCommune2 country={C}: {N} chunks", country, results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchDomesticRetenueAsync(
        List<string> keywords, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            foreach (var kw in keywords.Take(5))
            {
                if (results.Count >= 8) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE toLower(c.doc_name) CONTAINS 'irpp'
                          AND c.chunk_type = 'text'
                          AND toLower(c.text) CONTAINS toLower($kw)
                        RETURN {F}, 0.88 AS score
                        ORDER BY c.annee DESC LIMIT 3",
                        new { kw });
                    await foreach (var r in res)
                    {
                        var text = r["text"]?.As<string>() ?? "";
                        if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.88);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchRetenue {K}", kw); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchDomesticRetenue"); }

        _logger.LogInformation("FetchDomesticRetenue: {N} chunks", results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchDomesticTaxRulesAsync(
        string taxType, string[] keywords, CancellationToken ct = default)
    {
        var results   = new List<LegalSourceDto>();
        var seen      = new HashSet<string>();
        var docFilter = taxType.ToUpper() == "TVA" ? "ctva" : "irpp";
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            foreach (var kw in keywords.Take(5))
            {
                if (results.Count >= 8) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE toLower(c.doc_name) CONTAINS $docFilter
                          AND c.chunk_type = 'text'
                          AND toLower(c.text) CONTAINS toLower($kw)
                        RETURN {F}, 0.87 AS score
                        ORDER BY c.annee DESC LIMIT 3",
                        new { docFilter, kw });
                    await foreach (var r in res)
                    {
                        var text = r["text"]?.As<string>() ?? "";
                        if (!ContainsArabic(text)) TryAdd(results, seen, r, 0.87);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchTaxRules {T}/{K}", taxType, kw); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchDomesticTaxRules {T}", taxType); }

        _logger.LogInformation("FetchDomesticTaxRules {T}: {N} chunks", taxType, results.Count);
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static bool ContainsArabic(string text) =>
        text.Any(c => c >= '\u0600' && c <= '\u06FF');

    private static void TryAdd(List<LegalSourceDto> list, HashSet<string> seen,
        IRecord r, double defaultScore)
    {
        var id = r["id"]?.As<string>() ?? "";
        if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
        var dt = r["doc_type"]?.As<string>() ?? "";
        list.Add(new LegalSourceDto
        {
            ChunkId      = id,
            DocName      = r["doc_name"]?.As<string>()      ?? "",
            DocType      = dt,
            ArticleRef   = r["article_ref"]?.As<string>()   ?? "",
            SectionTitle = r["section_title"]?.As<string>() ?? "",
            Year         = r["annee"]?.As<string>()         ?? "",
            Text         = r["text"]?.As<string>()          ?? "",
            Score        = r.Keys.Contains("score")
                           ? r["score"]?.As<double>() ?? defaultScore
                           : defaultScore,
            IsExpert     = dt == "Commentaire",
        });
    }

    private static async Task<List<SourceChunkDto>> MapChunks(IResultCursor cursor)
    {
        var list = new List<SourceChunkDto>();
        await foreach (var r in cursor)
        {
            list.Add(new SourceChunkDto
            {
                DocName    = r["doc_name"]?.As<string>()    ?? "",
                PageNum    = r.Keys.Contains("page_num")
                             ? r["page_num"]?.As<int>() ?? 0 : 0,
                Text       = r["text"]?.As<string>()        ?? "",
                ChunkType  = r.Keys.Contains("chunk_type")
                             ? r["chunk_type"]?.As<string>() ?? "text" : "text",
                ArticleRef = r.Keys.Contains("article_ref")
                             ? r["article_ref"]?.As<string>() ?? "" : "",
            });
        }
        return list;
    }

    public void Dispose() => _driver?.Dispose();
}
