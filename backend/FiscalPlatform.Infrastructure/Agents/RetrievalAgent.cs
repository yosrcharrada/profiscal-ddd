using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Retrieval Agent — Neo4j graph queries for the "taxmind" knowledge graph.
///
/// taxmind ontology (vs the old flat graph):
///   (:Chunk {chunk_id, content, title, doc_id, corpus, chunk_type, article_number, article_display, embedding(384)})
///   corpus ∈ {Conventions, Lois_des_Finances, Notes_Communes, Recueils_textes_fiscaux}
///   chunk_type ∈ {article, section, introduction, resume, preamble, metadata, ...}
///   rels: NEXT/PREVIOUS, CONTAINS, PART_OF/PART_OF_SECTION, CITES, HAS_TOPIC,
///         SAME_TOPIC/SAME_SECTION/SAME_CHAPTER/SIMILAR_TO, MODIFIES/NEXT_VERSION, SIGNED_WITH
///   indexes: chunk_embeddings (VECTOR 384) + chunk_content (FULLTEXT/BM25 on content,title)
///
/// The RETURN projections below map taxmind properties onto the SAME aliases the rest of the
/// code expects (id/text/doc_name/doc_type/article_ref/section_title/annee), so TryAdd /
/// MapChunks / LegalSourceDto stay unchanged.
/// </summary>
public sealed class RetrievalAgent : IRetrievalAgent, IDisposable
{
    private readonly IDriver                  _driver;
    private readonly string                   _db;
    private readonly ILogger<RetrievalAgent>  _logger;

    // doc_type derived from the corpus/folder the chunk belongs to.
    // DUAL-SCHEMA: works on both graphs — taxmind (doc_id/corpus, whole-article chunks) and
    // taxmindvf (document_id/folder, paragraph-level parts). Switch via NEO4J_DATABASE in .env.
    private const string DocTypeCase =
        "CASE coalesce({0}.corpus, {0}.folder) WHEN 'Conventions' THEN 'Convention' " +
        "WHEN 'Lois_des_Finances' THEN 'LoiFinances' " +
        "WHEN 'Notes_Communes' THEN 'Doctrine' " +
        "WHEN 'Faiez' THEN 'Commentaire' ELSE 'Code' END";

    private static string Proj(string a) =>
        $"{a}.chunk_id AS id, {a}.content AS text, coalesce({a}.doc_id, {a}.document_id) AS doc_name, " +
        string.Format(DocTypeCase, a) + " AS doc_type, " +
        $"coalesce({a}.article_display, {a}.article_number, '') AS article_ref, " +
        $"{a}.title AS section_title, coalesce(toString({a}.year), '') AS annee";

    private static readonly string F     = Proj("c");
    private static readonly string CH    = Proj("ch");
    private static readonly string Fnext = Proj("next");

    // Note Commune doc_id prefixes (taxmind: NC_YYYY_NN).
    private const string NoteCommune2Id = "NC_2015_02"; // international: principes/modalités d'imposition
    private const string NoteCommune3Id = "NC_2015_03"; // domestic honoraires (retenue à la source)

    // Excludes Arabic code versions (e.g. code_tva_2025_ar) at query level, so LIMIT reaches the
    // FRENCH article. Without this, ~40 Arabic duplicates sort first and the number/keyword paths
    // return nothing usable. (The consultation is always French; the C# ContainsArabic filter is a
    // second net.) Interpolate {NoAr} into F-projection ($@) queries — never into plain @ queries.
    private const string NoAr = " AND NOT c.content =~ '(?s).*[؀-ۿ].*' ";

    private static readonly Dictionary<string, int> TypeRank = new()
    {
        ["Convention"]=0, ["Code"]=1, ["LoiFinances"]=2,
        ["Decret"]=3, ["Arrete"]=4, ["Doctrine"]=5, ["Commentaire"]=6,
    };

    // Country → doc_id fragment for conventions (taxmind doc_ids: conv_france, conv_maroc, conv_dba_tun_franz…).
    private static readonly Dictionary<string, string> CountryDocNameFragment =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["allemagne"]       = "franz",        // conv_dba_tun_franz
        ["angleterre"]      = "royaume",
        ["usa"]             = "etats",
        ["arabie saoudite"] = "arabie",
    };

    private static string ToDocFragment(string country) =>
        CountryDocNameFragment.TryGetValue(country ?? "", out var frag) ? frag : (country ?? "");

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
                 : (Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "taxmind");
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    // ── HEALTH + STATS ─────────────────────────────────────────────────────────
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
                MATCH (c:Chunk)    WITH count(c) AS chunks
                OPTIONAL MATCH (t:Topic) WITH chunks, count(t) AS ents
                MATCH ()-[rel]->() RETURN chunks, ents, count(rel) AS rels");
            var rec = await r.SingleAsync();
            stats.TotalChunks    = rec["chunks"].As<long>();
            stats.TotalEntities  = rec["ents"].As<long>();
            stats.TotalRelations = rec["rels"].As<long>();

            foreach (var (corpus, prop) in new[]
            {
                ("Lois_des_Finances","LoisCount"), ("Notes_Communes","NotesCount"),
                ("Recueils_textes_fiscaux","CodesCount"), ("Conventions","ConventionsCount"),
            })
            {
                var cr = await s.RunAsync(
                    "MATCH (c:Chunk) WHERE coalesce(c.corpus, c.folder) = $corpus RETURN count(c) AS n", new { corpus });
                var n = (await cr.SingleAsync())["n"].As<long>();
                switch (prop)
                {
                    case "LoisCount":        stats.LoisCount        = n; break;
                    case "NotesCount":       stats.NotesCount       = n; break;
                    case "CodesCount":       stats.CodesCount       = n; break;
                    case "ConventionsCount": stats.ConventionsCount = n; break;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetStats failed"); }
        return stats;
    }

    // ── MAIN RETRIEVAL ─────────────────────────────────────────────────────────
    public async Task<List<LegalSourceDto>> RetrieveSourcesAsync(
        List<string> keywords, List<string> entities, List<string> countries,
        bool isInternational, HashSet<string> branches,
        List<LegalSourceDto> conventionEmbedHints, int maxResults = 30,
        CancellationToken ct = default)
    {
        var all  = new List<LegalSourceDto>();
        var seen = new HashSet<string>();
        await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

        // STEP 0 — convention (international)
        if (isInternational && countries.Any())
        {
            if (conventionEmbedHints.Any())
            {
                int added = 0;
                foreach (var h in conventionEmbedHints)
                {
                    var k = !string.IsNullOrEmpty(h.ChunkId) ? h.ChunkId
                          : h.DocName + "|" + h.Text[..Math.Min(h.Text.Length, 60)];
                    if (seen.Add(k)) { all.Add(h); added++; }
                    if (added >= 6) break;
                }
            }
            else
                all.AddRange(await ConventionKeywordSearchAsync(session, countries, keywords, seen));
        }

        // STEP 1 — branch-guided code fetch
        if (branches.Contains("IS"))
            all.AddRange(await DocKeywordFetchAsync(session, "code_irpp_is",
                new[] { "personnes morales", "bénéfices", "impôt sur les sociétés", "taux", "résultat" }, seen, 5));
        if (branches.Contains("IRPP"))
            all.AddRange(await DocKeywordFetchAsync(session, "code_irpp_is",
                new[] { "revenu", "personne physique", "barème", "catégorie", "traitements" }, seen, 4));
        if (branches.Contains("TVA"))
            all.AddRange(await DocKeywordFetchAsync(session, "code_tva",
                new[] { "soumises", "affaires", "taux", "assujetti", "exonér" }, seen, 4));
        if (branches.Contains("Retenue"))
            all.AddRange(await DocKeywordFetchAsync(session, "code_irpp_is",
                new[] { "retenue à la source", "honoraires", "non résident", "taux", "versés" }, seen, 5));
        if (branches.Contains("PrixTransfert"))
            all.AddRange(await DocKeywordFetchAsync(session, "code_irpp_is",
                new[] { "48 septies", "pleine concurrence", "prix de transfert", "entreprises associées" }, seen, 4));

        // STEP 2 — Lois de finances + notes communes (doctrine)
        if (all.Count < maxResults)
        {
            var terms = keywords.Concat(entities).Distinct().ToList();
            all.AddRange(await CorpusKeywordSearchAsync(session, "Lois_des_Finances", terms, 4, seen));
            all.AddRange(await CorpusKeywordSearchAsync(session, "Notes_Communes",    terms, 4, seen));
        }

        // STEP 3 — graph neighbour expansion (NEXT + SAME_TOPIC/SIMILAR_TO + CITES)
        var seeds = all.Take(10).Where(r => !string.IsNullOrEmpty(r.ChunkId)).Select(r => r.ChunkId).ToList();
        all.AddRange(await NeighborExpandAsync(session, seeds, seen));

        // STEP 4 — diversity + newest
        var final = EnsureDiversityAndNewest(all, maxResults);
        for (int i = 0; i < final.Count; i++) final[i].Index = i + 1;

        _logger.LogInformation("RetrievalAgent: {T} sources | Conv:{C} Code:{Co} Doc:{D}",
            final.Count, final.Count(s => s.DocType == "Convention"),
            final.Count(s => s.DocType == "Code"), final.Count(s => s.DocType == "Doctrine"));
        return final;
    }

    // ── PRIVATE RETRIEVAL HELPERS ──────────────────────────────────────────────
    private async Task<List<LegalSourceDto>> DocKeywordFetchAsync(
        IAsyncSession session, string docIdFragment, string[] contentKws,
        HashSet<string> seen, int limit)
    {
        var results = new List<LegalSourceDto>();
        foreach (var kw in contentKws)
        {
            if (results.Count >= limit) break;
            try
            {
                var res = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($dk)
                      AND c.content <> '' AND toLower(c.content) CONTAINS toLower($kw)
                    RETURN {F}, 0.9 AS score
                    ORDER BY (CASE WHEN c.chunk_type = 'article' THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC
                    LIMIT 4",
                    new { dk = docIdFragment, kw });
                await foreach (var r in res)
                {
                    if (results.Count >= limit) break;
                    var t = r["text"]?.As<string>() ?? "";
                    if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.9);
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "DocKeywordFetch {D}/{K}", docIdFragment, kw); }
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
            var frag = ToDocFragment(country);
            foreach (var kw in keywords.Take(8))
            {
                if (results.Count >= 6) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE coalesce(c.corpus, c.folder) = 'Conventions'
                          AND toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag)
                          AND toLower(c.content) CONTAINS toLower($kw)
                        RETURN {F}, 0.85 AS score
                        ORDER BY (CASE WHEN c.chunk_type = 'article' THEN 0 ELSE 1 END)
                        LIMIT 2",
                        new { frag, kw });
                    await foreach (var r in res) TryAdd(results, seen, r, 0.85);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "ConvKw {C}/{K}", country, kw); }
            }
        }
        return results;
    }

    private async Task<List<LegalSourceDto>> CorpusKeywordSearchAsync(
        IAsyncSession session, string corpus, List<string> keywords, int limit, HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        if (!keywords.Any()) return results;
        try
        {
            var res = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE coalesce(c.corpus, c.folder) = $corpus AND c.content <> ''
                  AND ANY(kw IN $kws WHERE toLower(c.content) CONTAINS toLower(kw))
                RETURN {F}, 0.7 AS score
                ORDER BY (CASE WHEN c.chunk_type = 'article' THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC
                LIMIT $lim",
                new { corpus, kws = keywords, lim = limit * 5 });
            await foreach (var r in res)
            {
                if (results.Count >= limit) break;
                var t = r["text"]?.As<string>() ?? "";
                if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.7);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "CorpusKw {C}", corpus); }
        return results;
    }

    private async Task<List<LegalSourceDto>> NeighborExpandAsync(
        IAsyncSession session, List<string> seedIds, HashSet<string> seen)
    {
        var results = new List<LegalSourceDto>();
        if (!seedIds.Any()) return results;

        // a. sequential context (NEXT)
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:NEXT]->(c:Chunk)
                WHERE c.content <> ''
                RETURN DISTINCT {F}, 0.75 AS score LIMIT 6",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.75);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "NEXT"); }

        // b. same-topic neighbours
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:SAME_TOPIC]-(c:Chunk)
                WHERE c.content <> '' AND c.chunk_id <> sid
                RETURN DISTINCT {F}, 0.65 AS score LIMIT 8",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.65);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SAME_TOPIC"); }

        // c. semantically-similar neighbours (precomputed SIMILAR_TO edges, chunk↔chunk)
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:SIMILAR_TO]-(c:Chunk)
                WHERE c.content <> '' AND c.chunk_id <> sid
                RETURN DISTINCT {F}, 0.7 AS score LIMIT 5",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.7);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SIMILAR_TO"); }

        return results;
    }

    private static List<LegalSourceDto> EnsureDiversityAndNewest(List<LegalSourceDto> all, int maxTotal)
    {
        // Newest per doc family: strip a trailing _YYYY from the doc_id and keep the latest.
        var newest = all
            .GroupBy(s => Regex.Replace(s.DocName ?? "", @"_\d{4}.*$|_ar$", "", RegexOptions.IgnoreCase).ToLower())
            .Select(g => g.OrderByDescending(s =>
            {
                var m = Regex.Match(s.DocName ?? "", @"_(\d{4})");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }).First())
            .ToList();

        var doc   = newest.Where(r => r.DocType == "Doctrine").OrderByDescending(r => r.Score).ToList();
        var other = newest.Where(r => r.DocType != "Doctrine")
                          .OrderBy(r => TypeRank.GetValueOrDefault(r.DocType, 9))
                          .ThenByDescending(r => r.Score).ToList();
        var reserved = doc.Take(4).ToList();
        return other.Take(maxTotal - reserved.Count).Concat(reserved).Take(maxTotal).ToList();
    }

    // ── GRAPHRAG CHAT SUPPORT ──────────────────────────────────────────────────
    public async Task<List<SourceChunkDto>> VectorSearchAsync(float[] embedding, int topK = 8)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await session.RunAsync(@"
                CALL db.index.vector.queryNodes('chunk_embeddings', $topK, $emb)
                YIELD node AS c, score
                WHERE c.content <> '' AND score >= 0.3
                RETURN coalesce(c.doc_id, c.document_id) AS doc_name, 0 AS page_num,
                       c.content AS text, c.chunk_type AS chunk_type,
                       coalesce(c.article_display, c.article_number, '') AS article_ref, score
                LIMIT $topK",
                new { topK, emb = embedding });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "VectorSearch"); return new(); }
    }

    public async Task<List<SourceChunkDto>> GraphExpandAsync(List<string> entities, int topK = 6)
    {
        var results = new List<SourceChunkDto>();
        if (entities is null || !entities.Any()) return results;
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            // Topic-mediated expansion: chunks linked to topics whose label matches a term.
            var r = await session.RunAsync(@"
                UNWIND $terms AS term
                MATCH (t:Topic) WHERE toLower(t.label) CONTAINS toLower(term)
                MATCH (t)<-[:HAS_TOPIC]-(c:Chunk) WHERE c.content <> ''
                RETURN DISTINCT coalesce(c.doc_id, c.document_id) AS doc_name, 0 AS page_num,
                       c.content AS text, c.chunk_type AS chunk_type,
                       coalesce(c.article_display, c.article_number, '') AS article_ref, 0.7 AS score
                LIMIT $topK",
                new { terms = entities, topK });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "GraphExpand"); return results; }
    }

    public async Task<List<SourceChunkDto>> KeywordFallbackAsync(string query, int topK = 8)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            // Use the native BM25 full-text index when possible; fall back to CONTAINS.
            try
            {
                var ft = await session.RunAsync(@"
                    CALL db.index.fulltext.queryNodes('chunk_content', $q) YIELD node AS c, score
                    WHERE c.content <> ''
                    RETURN coalesce(c.doc_id, c.document_id) AS doc_name, 0 AS page_num, c.content AS text,
                           c.chunk_type AS chunk_type,
                           coalesce(c.article_display, c.article_number, '') AS article_ref, score
                    LIMIT $topK",
                    new { q = SanitizeLucene(query), topK });
                var mapped = await MapChunks(ft);
                if (mapped.Count > 0) return mapped;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "fulltext fallback"); }

            var terms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Where(t => t.Length >= 4).Take(8).ToList();
            var r = await session.RunAsync(@"
                MATCH (c:Chunk)
                WHERE c.content <> '' AND ANY(t IN $terms WHERE toLower(c.content) CONTAINS t)
                RETURN coalesce(c.doc_id, c.document_id) AS doc_name, 0 AS page_num, c.content AS text,
                       c.chunk_type AS chunk_type,
                       coalesce(c.article_display, c.article_number, '') AS article_ref, 0.5 AS score
                LIMIT $topK",
                new { terms, topK });
            return await MapChunks(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "KeywordFallback"); return new(); }
    }

    // ── TARGETED FETCH METHODS (planner + rule-based policy) ────────────────────
    public async Task<List<LegalSourceDto>> FetchConventionArticleAsync(
        string country, string[] keywords, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        var frag    = ToDocFragment(country);
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            foreach (var kw in keywords.Take(6))
            {
                if (results.Count >= 12) break;
                try
                {
                    // Treaty article numbering is convention-specific, so match on the article
                    // SUBJECT (its title — e.g. 'Redevances', 'Etablissement stable'), title first.
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE coalesce(c.corpus, c.folder) = 'Conventions' AND c.chunk_type = 'article'
                          AND toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag)
                          AND (toLower(c.title) CONTAINS toLower($kw) OR toLower(c.content) CONTAINS toLower($kw))
                        RETURN {F}, (CASE WHEN toLower(c.title) CONTAINS toLower($kw) THEN 0.95 ELSE 0.85 END) AS score
                        ORDER BY (CASE WHEN toLower(c.title) CONTAINS toLower($kw) THEN 0 ELSE 1 END), size(c.title)
                        LIMIT 3",
                        new { frag, kw });
                    await foreach (var r in res)
                    {
                        var t = r["text"]?.As<string>() ?? "";
                        if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.92);
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchConvArticle {C}/{K}", country, kw); }
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
            var res = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE coalesce(c.doc_id, c.document_id) STARTS WITH $nc AND c.content <> ''
                RETURN {F}, 0.9 AS score
                ORDER BY (CASE WHEN c.chunk_type='article' THEN 0 ELSE 1 END), c.chunk_index
                LIMIT 8",
                new { nc = NoteCommune2Id });
            await foreach (var r in res)
            {
                var t = r["text"]?.As<string>() ?? "";
                if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.9);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchNoteCommune2"); }
        _logger.LogInformation("FetchNoteCommune2 country={C}: {N} chunks", country, results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchDomesticRetenueAsync(
        List<string> keywords, CancellationToken ct = default)
    {
        // The withholding-rate article is CIRPPIS Art. 52 — fetch it precisely, plus NC N°3/2015.
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            // toString() so this matches whether article_number is stored as a string ('52') or an
            // integer (52) — the import type differs across Neo4j environments. Ordering: NEWEST
            // edition first (doc DESC — the LF changes rates yearly), then reading order within the
            // article (part ASC — taxmindvf splits articles into paragraph-level parts; 0 on the old
            // whole-article graph). LIMIT is generous: on taxmindvf one article = many small parts;
            // on taxmind it returns several editions and the workflow's edition-dedup keeps the newest.
            var art = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE toLower(coalesce(c.doc_id, c.document_id)) CONTAINS 'code_irpp_is'
                  AND (c.chunk_type = 'article' OR c.part_number IS NOT NULL)
                  AND (toString(c.article_number) IN ['52','53'] OR c.article_display CONTAINS '52' OR c.article_display CONTAINS '53')
                RETURN {F}, 0.95 AS score
                ORDER BY coalesce(c.doc_id, c.document_id) DESC, coalesce(c.part_number, 0) ASC, size(c.content) DESC LIMIT 14");
            await foreach (var r in art) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.95); }

            var nc = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE (coalesce(c.doc_id, c.document_id) STARTS WITH $nc3 OR coalesce(c.doc_id, c.document_id) STARTS WITH 'NC_2015_12')
                  AND c.content <> '' AND toLower(c.content) CONTAINS 'honoraires'
                RETURN {F}, 0.9 AS score LIMIT 3",
                new { nc3 = NoteCommune3Id });
            await foreach (var r in nc) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.9); }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchDomesticRetenue"); }
        _logger.LogInformation("FetchDomesticRetenue: {N} chunks", results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchDomesticTaxRulesAsync(
        string taxType, string[] keywords, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        var docFrag = taxType.ToUpper() == "TVA" ? "code_tva" : "code_irpp_is";
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));
            foreach (var kw in keywords.Take(5))
            {
                if (results.Count >= 8) break;
                var res = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE toLower(coalesce(c.doc_id, c.document_id)) CONTAINS $docFrag AND c.content <> ''
                      AND toLower(c.content) CONTAINS toLower($kw)
                    RETURN {F}, 0.87 AS score
                    ORDER BY (CASE WHEN c.chunk_type='article' THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC LIMIT 3",
                    new { docFrag, kw });
                await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.87); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchDomesticTaxRules {T}", taxType); }
        _logger.LogInformation("FetchDomesticTaxRules {T}: {N} chunks", taxType, results.Count);
        return results;
    }

    public async Task<List<LegalSourceDto>> FetchTargetedAsync(
        string docNameFragment, string[] articleRefs, string[] keywords, CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        var frag    = ToDocFragment(docNameFragment ?? "");
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

            // 1) by article number (precise — taxmind has article-typed chunks with article_number).
            //    Newest edition only, exact article number first, then reading order (part ASC — on
            //    taxmindvf an article is many small paragraph parts; LIMIT 8 covers them; on the old
            //    whole-article graph part is 0 and the workflow's edition-dedup keeps the newest copy).
            foreach (var aref in (articleRefs ?? Array.Empty<string>()).Take(8))
            {
                if (results.Count >= 40) break;
                var num = new string((aref ?? "").Where(char.IsDigit).ToArray());
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                          AND (c.chunk_type = 'article' OR c.part_number IS NOT NULL) AND c.content <> ''{NoAr}
                          AND ($num <> '' AND (toString(c.article_number) = $num OR c.article_display CONTAINS $num))
                        RETURN {F}, 0.96 AS score
                        ORDER BY (CASE WHEN toString(c.article_number) = $num THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC, coalesce(c.part_number, 0) ASC, size(c.content) DESC LIMIT 8",
                        new { frag, num });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.96); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchTargeted aref {A}", aref); }
            }

            // 2) by keyword within the document family
            foreach (var kw in (keywords ?? Array.Empty<string>()).Take(5))
            {
                if (results.Count >= 14) break;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                          AND c.content <> ''{NoAr} AND toLower(c.content) CONTAINS toLower($kw)
                        RETURN {F}, 0.9 AS score
                        ORDER BY (CASE WHEN c.chunk_type='article' THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC LIMIT 3",
                        new { frag, kw });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.9); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchTargeted kw {K}", kw); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchTargeted {D}", docNameFragment); }
        _logger.LogInformation("FetchTargeted doc='{D}' refs={R}: {N} chunks",
            docNameFragment, articleRefs?.Length ?? 0, results.Count);
        return results;
    }

    // Number-free: locate a provision by anchor phrase → Topic → keyword → BM25 (all scoped to a
    // doc family). No article numbers — robust to convention/code renumbering.
    public async Task<List<LegalSourceDto>> FetchBySubjectAsync(
        string docFragment, string[] anchorPhrases, string[] topics, string[] keywords,
        CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        var frag    = ToDocFragment(docFragment ?? "");
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

            // 1) anchor phrases via BM25 (tokenised — robust to the PDF line-breaks/accents that
            //    break exact substring matching). Pinpoints the provision without article numbers.
            if ((anchorPhrases ?? Array.Empty<string>()).Any(p => !string.IsNullOrWhiteSpace(p)))
            {
                try
                {
                    var q = SanitizeLucene(string.Join(" ", anchorPhrases!));
                    var res = await session.RunAsync($@"
                        CALL db.index.fulltext.queryNodes('chunk_content', $q) YIELD node AS c, score
                        WHERE c.content <> '' AND ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                        RETURN {F}, 0.92 AS score
                        ORDER BY score DESC LIMIT 3",
                        new { frag, q });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.92); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchBySubject anchors BM25"); }
            }

            // 2) Topic-mediated (taxmind HAS_TOPIC taxonomy).
            foreach (var topic in (topics ?? Array.Empty<string>()).Take(6))
            {
                if (results.Count >= 14 || string.IsNullOrWhiteSpace(topic)) continue;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (t:Topic) WHERE toLower(t.label) CONTAINS toLower($topic)
                        MATCH (t)<-[:HAS_TOPIC]-(c:Chunk)
                        WHERE c.content <> '' AND ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                        RETURN {F}, 0.85 AS score
                        ORDER BY (CASE WHEN c.chunk_type='article' THEN 0 ELSE 1 END) LIMIT 2",
                        new { frag, topic });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.85); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchBySubject topic {T}", topic); }
            }

            // 3) keyword content match.
            foreach (var kw in (keywords ?? Array.Empty<string>()).Take(5))
            {
                if (results.Count >= 14 || string.IsNullOrWhiteSpace(kw)) continue;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                          AND c.content <> '' AND toLower(c.content) CONTAINS toLower($kw)
                        RETURN {F}, 0.8 AS score
                        ORDER BY (CASE WHEN c.chunk_type='article' THEN 0 ELSE 1 END), coalesce(c.doc_id, c.document_id) DESC LIMIT 2",
                        new { frag, kw });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.8); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchBySubject kw {K}", kw); }
            }

            // 4) BM25 safety net within the doc family if still thin.
            if (results.Count < 3)
            {
                try
                {
                    var q = SanitizeLucene(string.Join(" ", (anchorPhrases ?? Array.Empty<string>())
                        .Concat(topics ?? Array.Empty<string>()).Concat(keywords ?? Array.Empty<string>())));
                    var res = await session.RunAsync($@"
                        CALL db.index.fulltext.queryNodes('chunk_content', $q) YIELD node AS c, score
                        WHERE c.content <> '' AND ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
                        RETURN {F}, 0.75 AS score
                        ORDER BY score DESC LIMIT 4",
                        new { frag, q });
                    await foreach (var r in res) { var t=r["text"]?.As<string>()??""; if(!ContainsArabic(t)) TryAdd(results, seen, r, 0.75); }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "FetchBySubject BM25"); }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchBySubject {D}", docFragment); }
        _logger.LogInformation("FetchBySubject doc='{D}' phrases={P} topics={T}: {N} chunks",
            docFragment, anchorPhrases?.Length ?? 0, topics?.Length ?? 0, results.Count);
        return results;
    }

    // ── HELPERS ────────────────────────────────────────────────────────────────
    private static string SanitizeLucene(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return "*";
        var cleaned = Regex.Replace(q, "[+\\-!(){}\\[\\]^\"~*?:\\\\/]", " ");
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Length >= 2).Take(12);
        var joined = string.Join(" OR ", words);
        return string.IsNullOrWhiteSpace(joined) ? "*" : joined;
    }

    private static bool ContainsArabic(string text) =>
        text.Any(c => c >= '؀' && c <= 'ۿ');

    private static void TryAdd(List<LegalSourceDto> list, HashSet<string> seen, IRecord r, double defaultScore)
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
                PageNum    = r.Keys.Contains("page_num") ? r["page_num"]?.As<int>() ?? 0 : 0,
                Text       = r["text"]?.As<string>()        ?? "",
                ChunkType  = r.Keys.Contains("chunk_type") ? r["chunk_type"]?.As<string>() ?? "" : "",
                ArticleRef = r["article_ref"]?.As<string>() ?? "",
                Score      = r.Keys.Contains("score") ? r["score"]?.As<double>() ?? 0 : 0,
            });
        }
        return list;
    }

    public void Dispose() => _driver?.Dispose();
}
