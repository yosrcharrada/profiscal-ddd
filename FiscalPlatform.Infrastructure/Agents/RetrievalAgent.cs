using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Retrieval Agent — wraps all Neo4j graph queries.
/// Implements the legal hierarchy retrieval pipeline:
///   International: Convention → Code → LoiFinances → Doctrine
///   Local:         Code → LoiFinances → Doctrine
/// "Newest documents" requirement: uses most recent year per doc_name prefix.
/// Arabic filter: skips chunks containing Arabic characters.
/// </summary>
public sealed class RetrievalAgent : IRetrievalAgent, IDisposable
{
    private readonly IDriver               _driver;
    private readonly string                _db;
    private readonly ILogger<RetrievalAgent> _logger;

    private const string F  =
        "c.chunk_id AS id, c.text AS text, c.doc_name AS doc_name, " +
        "c.doc_type AS doc_type, c.article_ref AS article_ref, " +
        "c.section_title AS section_title, c.annee AS annee";

    private const string CH =
        "ch.chunk_id AS id, ch.text AS text, ch.doc_name AS doc_name, " +
        "ch.doc_type AS doc_type, ch.article_ref AS article_ref, " +
        "ch.section_title AS section_title, ch.annee AS annee";

    private static readonly Dictionary<string, int> TypeRank = new()
    {
        ["Convention"]=0,["Code"]=1,["LoiFinances"]=2,
        ["Decret"]=3,["Arrete"]=4,["Doctrine"]=5,["Commentaire"]=6,
    };

    public RetrievalAgent(IConfiguration config, ILogger<RetrievalAgent> logger)
    {
        _logger = logger;
        var uri  = (config["Neo4j:Uri"]      is { Length: > 0 } u) ? u : (Environment.GetEnvironmentVariable("NEO4J_URI")      ?? "neo4j://127.0.0.1:7687");
        var user = (config["Neo4j:Username"] is { Length: > 0 } n) ? n : (Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j");
        var pass = (config["Neo4j:Password"] is { Length: > 0 } p) ? p : (Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "neo4j");
        _db      = (config["Neo4j:Database"] is { Length: > 0 } d) ? d : (Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "tunisian-fiscal");
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MAIN RETRIEVAL PIPELINE
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

        // ── STEP 0: Hierarchy-guided fetch ───────────────────────────────────
        // International: Convention first (highest authority)
        if (isInternational && countries.Any())
        {
            if (conventionEmbedHints.Any())
            {
                foreach (var h in conventionEmbedHints.Take(8))
                {
                    var key = !string.IsNullOrEmpty(h.ChunkId) ? h.ChunkId : h.DocName + h.ArticleRef;
                    if (seen.Add(key)) all.Add(h);
                }
            }
            else
            {
                all.AddRange(await ConventionKeywordSearchAsync(session, countries, keywords, seen));
            }
        }

        // ── STEP 1: Branch-guided code fetches ───────────────────────────────
        // MEETING REQUIREMENT: Use newest documents — filter by most recent year
        if (branches.Contains("IS"))
        {
            // Art. 45 (s'applique, personnes morales) + Art. 47 (bénéfices passibles)
            var c = await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "s'applique", "personnes morales", "exerçant",
                        "bénéfices passibles", "établissements situés",
                        "bénéfice imposable", "résultat", "impôt sur les sociétés" },
                seen, limit: 5);
            all.AddRange(c);
        }
        if (branches.Contains("IRPP"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "revenu", "personne physique", "retenue barème", "catégorie", "traitements" },
                seen, limit: 4));
        }
        if (branches.Contains("TVA"))
        {
            // Use most recent CTVA version
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "ctva",
                new[] { "soumises", "affaires", "activités", "assujetti", "exonér" },
                seen, limit: 4));
        }
        if (branches.Contains("Retenue"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "retenue à la source", "non-résident", "art. 52", "prestataire" },
                seen, limit: 4));
        }
        if (branches.Contains("PrixTransfert"))
        {
            all.AddRange(await TargetedDocFetchWithKeywordsAsync(session, "irpp",
                new[] { "48 septies", "pleine concurrence", "parties liées", "prix de transfert" },
                seen, limit: 4));
            all.AddRange(await TargetedDocFetchAsync(session, "cdpf", seen, limit: 3));
        }

        // ── STEP 2: Lois de Finances ─────────────────────────────────────────
        if (all.Count < maxResults)
        {
            var expanded = keywords.Concat(entities).Distinct().ToList();
            int perType  = Math.Max(2, (maxResults - all.Count) / 3);
            foreach (var dt in new[] { "LoiFinances", "Doctrine", "Commentaire" })
                all.AddRange(await KeywordSearchByTypeAsync(session, dt, expanded, entities, perType, seen));
        }

        // ── STEP 3: Neighbor expansion ───────────────────────────────────────
        var seeds = all.Take(10).Where(r => !string.IsNullOrEmpty(r.ChunkId))
                       .Select(r => r.ChunkId).ToList();
        all.AddRange(await NeighborExpandAsync(session, seeds, seen));

        // ── STEP 4: Diversity + newest documents enforcement ──────────────────
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
    // TARGETED FETCH — no year ordering, Arabic filter
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
        IAsyncSession session, List<string> countries, List<string> keywords, HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        foreach (var country in countries)
        {
            if (results.Count >= 6) break;
            foreach (var kw in keywords.Take(8))
            {
                if (results.Count >= 6) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk {{doc_type:'Convention', chunk_type:'text'}})
                        WHERE toLower(c.doc_name) CONTAINS toLower($country)
                          AND toLower(c.text) CONTAINS toLower($kw)
                        RETURN {F}, 0.85 AS score
                        ORDER BY (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END) LIMIT 2",
                        new { country, kw });
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

        // a. Next chunk
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:NEXT_CHUNK]->(c:Chunk)
                WHERE c.chunk_type = 'text' RETURN DISTINCT {F}, 0.75 AS score LIMIT 6",
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
                WHERE nc.chunk_type = 'text' RETURN DISTINCT {CH}, 0.7 AS score LIMIT 5",
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

    // ─────────────────────────────────────────────────────────────────────────
    // DIVERSITY + NEWEST DOCUMENTS
    // Meeting requirement: prefer most recent year per doc_name prefix
    // ─────────────────────────────────────────────────────────────────────────
    private static List<LegalSourceDto> EnsureDiversityAndNewest(
        List<LegalSourceDto> all, int maxTotal)
    {
        // Group by doc_name prefix (strip year suffix) and keep only newest
        var newestPerDoc = all
            .GroupBy(s => Regex.Replace(s.DocName, @"[-_]?\d{4}.*$", "", RegexOptions.IgnoreCase).Trim().ToLower())
            .Select(g => g.OrderByDescending(s =>
            {
                var m = Regex.Match(s.Year ?? "", @"\d{4}");
                return m.Success ? int.Parse(m.Value) : 0;
            }).First())
            .ToList();

        // Apply diversity slots
        var com      = newestPerDoc.Where(r => r.DocType == "Commentaire").OrderByDescending(r => r.Score).ToList();
        var doc      = newestPerDoc.Where(r => r.DocType == "Doctrine").OrderByDescending(r => r.Score).ToList();
        var other    = newestPerDoc.Where(r => r.DocType != "Commentaire" && r.DocType != "Doctrine")
                                   .OrderBy(r => TypeRank.GetValueOrDefault(r.DocType, 9))
                                   .ThenByDescending(r => r.Score).ToList();
        var reserved = com.Take(4).Concat(doc.Take(3)).ToList();
        return other.Take(maxTotal - reserved.Count).Concat(reserved).Take(maxTotal).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CHAT — GraphRAG support
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

    public async Task<List<SourceChunkDto>> GraphExpandAsync(List<string> entities, int topK = 6)
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

    public async Task<List<SourceChunkDto>> KeywordFallbackAsync(string query, int topK = 8)
    {
        try
        {
            var kws = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(k => k.Length >= 4).ToList();
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await session.RunAsync(@"
                MATCH (c:Chunk)
                WHERE ANY(kw IN $kws WHERE toLower(c.text) CONTAINS kw)
                RETURN c.doc_name AS doc_name, c.page_num AS page_num,
                       c.text AS text, c.chunk_type AS chunk_type,
                       c.article_ref AS article_ref, 0.5 AS score LIMIT $topK",
                new { kws, topK });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "KeywordFallback failed"); return new(); }
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
                MATCH (c:Chunk)              WITH count(c) AS chunks
                MATCH (e:Entity)             WITH chunks, count(e) AS ents
                MATCH ()-[rel]->()           WITH chunks, ents, count(rel) AS rels
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
                var cr = await s.RunAsync("MATCH (c:Chunk {doc_type:$dt}) RETURN count(c) AS n", new { dt });
                var n  = await cr.SingleAsync();
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
            Score        = r.Keys.Contains("score") ? r["score"]?.As<double>() ?? defaultScore : defaultScore,
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
                PageNum    = r.Keys.Contains("page_num")  ? r["page_num"]?.As<int>()    ?? 0   : 0,
                Text       = r["text"]?.As<string>()       ?? "",
                ChunkType  = r.Keys.Contains("chunk_type")? r["chunk_type"]?.As<string>() ?? "text" : "text",
                ArticleRef = r.Keys.Contains("article_ref")? r["article_ref"]?.As<string>() ?? "" : "",
                Score      = r.Keys.Contains("score")     ? r["score"]?.As<double>()    ?? 0.0 : 0.0,
            });
        }
        return list;
    }

    public void Dispose() => _driver?.Dispose();
}
