using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Retrieval Agent — Neo4j graph queries for the current "taxmindvf" knowledge graph.
///
/// taxmindvf ontology (introspected from the live DB — supersedes the old tunisian-fiscal /
/// taxmind schemas, which had (:Topic) nodes + [:HAS_TOPIC]/[:NEXT]/[:SAME_TOPIC]/[:SIMILAR_TO]
/// relationships that NO LONGER EXIST here):
///   (:Chunk {chunk_id, content, title, doc_id|document_id, corpus|folder, chunk_type,
///            article_number, article_display, provision_uid, part_number, total_parts,
///            topic_id, topic_label, embedding(384)})
///   corpus ∈ {Conventions, Lois_des_Finances, Notes_Communes, Recueils_textes_fiscaux}
///   chunk_type ∈ {article, section, introduction, resume, preamble, metadata, ...}
///   TOPICS are now Chunk PROPERTIES (topic_id slug + topic_label display, ~50 convention
///     subjects on ~3 500 chunks), NOT separate nodes — so "topic-mediated" retrieval matches
///     on c.topic_label / c.topic_id instead of traversing a (:Topic) node.
///   chunk↔chunk rels: NEXT_PART / NEXT_ARTICLE / NEXT_SECTION (sequential context),
///     CITES_ARTICLE / REFERENCES_ARTICLE (citations ≈ related content), COMMENTS_ON (doctrine),
///     ADDS_TO / ABROGATES / MODIFIES / REPLACES (amendment history).
///   doc-level rels: HAS_SECTION, CONTAINS_CHUNK, NEXT_VERSION / NEXT_EDITION, BELONGS_TO_YEAR.
///   indexes: chunk_embeddings (VECTOR 384) + chunk_content (FULLTEXT/BM25 on content,title)
///     + chunk_art_idx(article_number) + chunk_doc_idx(document_id) + chunk_unique(chunk_id).
///
/// The RETURN projections below map taxmindvf properties onto the SAME aliases the rest of the
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
        "WHEN 'Faiez' THEN 'Commentaire' WHEN 'Expert' THEN 'Commentaire' ELSE 'Code' END";

    private static string Proj(string a) =>
        $"{a}.chunk_id AS id, {a}.content AS text, coalesce({a}.doc_id, {a}.document_id) AS doc_name, " +
        string.Format(DocTypeCase, a) + " AS doc_type, " +
        $"coalesce({a}.article_display, {a}.article_number, '') AS article_ref, " +
        $"{a}.title AS section_title, coalesce(toString({a}.year), '') AS annee, " +
        $"coalesce({a}.provision_uid, '') AS provision_uid";

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
    // Match only the HEAD of the content, not the whole body: Arabic chunks are Arabic from the very
    // first characters, so left(...,160) catches every one while turning a full-content regex scan
    // (the dominant cost across the parallel rule-policy fetches) into a cheap fixed-window test.
    private const string NoAr = " AND NOT left(c.content, 160) =~ '(?s).*[؀-ۿ].*' ";

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
        // PRECEDENCE: the .env NEO4J_* vars WIN over appsettings/user-secrets. The single root .env
        // is documented as driving BOTH the Python embed server and this API; the old order
        // (appsettings first) silently sent the API to a different graph than embed_server on any
        // machine whose appsettings/user-secrets disagreed with .env — the root cause of a week of
        // "the data is there but the app can't see it".
        var envDb = Environment.GetEnvironmentVariable("NEO4J_DATABASE");
        var uri  = Environment.GetEnvironmentVariable("NEO4J_URI")
                 ?? (config["Neo4j:Uri"]      is { Length: > 0 } u ? u : "neo4j://127.0.0.1:7687");
        var user = Environment.GetEnvironmentVariable("NEO4J_USERNAME")
                 ?? (config["Neo4j:Username"] is { Length: > 0 } n ? n : "neo4j");
        var pass = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")
                 ?? (config["Neo4j:Password"] is { Length: > 0 } p ? p : "neo4j");
        _db      = !string.IsNullOrWhiteSpace(envDb) ? envDb!
                 : (config["Neo4j:Database"] is { Length: > 0 } d ? d : "taxmind");
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
        _logger.LogInformation("[NEO4J] database = '{Db}' (source: {Src}) | uri = {Uri}",
            _db, !string.IsNullOrWhiteSpace(envDb) ? ".env NEO4J_DATABASE" : "appsettings Neo4j:Database", uri);
    }

    // One-per-process graph fingerprint: proves at a glance WHICH data the app actually sees.
    // Logged on the first retrieval of the process — if this says MISSING, the corrected CDPF/BCT
    // data is not in the database this API is querying (wrong DB name or import not run), and no
    // amount of prompt/code change will make Art.112/BCT appear in consultations.
    private static int _fingerprinted;
    private async Task FingerprintOnceAsync(IAsyncSession session)
    {
        if (Interlocked.Exchange(ref _fingerprinted, 1) == 1) return;
        try
        {
            async Task<long> CountAsync(string cypher)
            {
                var cur = await session.RunAsync(cypher);
                return (await cur.SingleAsync())["n"].As<long>();
            }
            var total  = await CountAsync("MATCH (c:Chunk) RETURN count(c) AS n");
            var art112 = await CountAsync(
                "MATCH (c:Chunk) WHERE toLower(coalesce(c.doc_id,c.document_id)) CONTAINS 'code_droits_procedures' " +
                "AND toString(c.article_number) = '112' " +
                "AND NOT left(c.content,160) =~ '(?s).*[؀-ۿ].*' RETURN count(c) AS n");
            var bct    = await CountAsync(
                "MATCH (c:Chunk) WHERE toLower(coalesce(c.doc_id,c.document_id)) CONTAINS 'circulaire_bct' RETURN count(c) AS n");
            var ok = art112 >= 8 && bct >= 40;
            _logger.LogInformation(
                "[NEO4J FINGERPRINT] db='{Db}' | chunks={T} | CDPF-art112 parts={A} (expect ≥8) | BCT chunks={B} (expect 47) → corrected data {V}",
                _db, total, art112, bct, ok ? "PRESENT ✅" : "MISSING ❌ — wrong database or import not run on this machine");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[NEO4J FINGERPRINT] probe failed"); }
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
            // taxmindvf has no (:Topic) nodes — topics are Chunk properties now, so "entities"
            // is the count of distinct topic labels carried on chunks.
            var r = await s.RunAsync(@"
                MATCH (c:Chunk) WITH count(c) AS chunks, count(DISTINCT c.topic_label) AS ents
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
        await FingerprintOnceAsync(session);

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

        // a. sequential context — taxmindvf has no generic [:NEXT]; the adjacency edges are
        //    NEXT_PART (next paragraph of the same article/provision) and NEXT_ARTICLE (next
        //    article), both directed forward from the seed. Together they give the same
        //    "read-on" context the old [:NEXT] did.
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:NEXT_PART|NEXT_ARTICLE]->(c:Chunk)
                WHERE c.content <> ''
                RETURN DISTINCT {F}, 0.75 AS score LIMIT 6",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.75);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "NEXT_PART/NEXT_ARTICLE"); }

        // b. same-topic neighbours — taxmindvf has no [:SAME_TOPIC] relationship; topics are a
        //    Chunk property (topic_id), so chunks sharing the seed's topic_id are the same-topic
        //    neighbours. Only convention chunks carry a topic_id, so the WITH…WHERE tid IS NOT
        //    NULL gate skips the scan entirely for the (majority) code/law seeds.
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})
                WITH DISTINCT seed.topic_id AS tid WHERE tid IS NOT NULL AND tid <> ''
                MATCH (c:Chunk {{topic_id: tid}})
                WHERE c.content <> ''
                RETURN DISTINCT {F}, 0.65 AS score LIMIT 8",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.65);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "same-topic (topic_id)"); }

        // c. related neighbours — taxmindvf has no precomputed [:SIMILAR_TO]; the nearest
        //    "related content" edges are the citation links CITES_ARTICLE / REFERENCES_ARTICLE
        //    (a chunk and the provision it cites), traversed undirected so we reach both the
        //    citing and the cited chunk.
        try
        {
            var r = await session.RunAsync($@"
                UNWIND $ids AS sid
                MATCH (seed:Chunk {{chunk_id: sid}})-[:CITES_ARTICLE|REFERENCES_ARTICLE]-(c:Chunk)
                WHERE c.content <> '' AND c.chunk_id <> sid
                RETURN DISTINCT {F}, 0.7 AS score LIMIT 5",
                new { ids = seedIds });
            await foreach (var rec in r) TryAdd(results, seen, rec, 0.7);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "CITES_ARTICLE/REFERENCES_ARTICLE"); }

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
            // Topic-mediated expansion: in taxmindvf the topic is a Chunk property (topic_label),
            // not a (:Topic) node, so match chunks whose topic_label contains a term directly.
            var r = await session.RunAsync(@"
                UNWIND $terms AS term
                MATCH (c:Chunk)
                WHERE c.topic_label IS NOT NULL AND toLower(c.topic_label) CONTAINS toLower(term)
                  AND c.content <> ''
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

    // LINE-PRECISE article fetch. Two steps: (1) resolve the NEWEST edition carrying the article;
    // (2) inside that single edition, return the header (part 1) plus ONLY the parts whose text
    // matches the requested line predicates, expanded to their NEXT_PART neighbours because an
    // alinéa's sentence can straddle two parts. On taxmindvf (paragraph parts) this hands the
    // writer exactly the alinéas the case needs; on taxmind (whole-article chunks, part_number
    // null → treated as header) it degrades to the article's chunks. NEXT_PART simply doesn't
    // exist on taxmind, so the OPTIONAL MATCH contributes nothing there.
    public async Task<List<LegalSourceDto>> FetchArticleLinesAsync(
        string docFragment, string articleNumber, string? mustContain, bool requirePercent,
        CancellationToken ct = default)
    {
        var results = new List<LegalSourceDto>();
        var seen    = new HashSet<string>();
        var frag    = (docFragment ?? "").ToLowerInvariant();
        var num     = new string((articleNumber ?? "").Where(char.IsDigit).ToArray());
        var contains = (mustContain ?? "").ToLowerInvariant();
        if (num.Length == 0) return results;
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

            // (0) PROVISION RESOLUTION — the fix for the article-number COLLISION. On taxmindvf a
            //     single article_number (e.g. CTVA « 7 ») is shared by the real code article PLUS
            //     unrelated décrets and their annexed rate tables. The in-place enrichment stamped a
            //     stable `provision_uid` on every chunk (the id of its provision head), so the real
            //     provision is separable from the decree ones. When we have a disambiguating signal
            //     (a content anchor or a required %), resolve the ONE provision whose members carry it,
            //     newest edition first — then read ONLY that provision's parts. On the old whole-article
            //     graph provision_uid is null, so this resolves to nothing and we fall through cleanly.
            string? uid = null;
            if (contains.Length > 0 || requirePercent)
            {
                var pRes = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS $frag)
                      AND toString(c.article_number) = $num AND c.content <> ''{NoAr}
                      AND c.provision_uid IS NOT NULL
                      AND ($contains = '' OR toLower(c.content) CONTAINS $contains)
                      AND (NOT $needPct OR c.content CONTAINS '%')
                    RETURN c.provision_uid AS uid, coalesce(c.doc_id, c.document_id) AS doc
                    ORDER BY doc DESC LIMIT 1",
                    new { frag, num, contains, needPct = requirePercent });
                await foreach (var r in pRes) { uid = r["uid"].As<string>(); break; }
            }

            if (uid is not null)
            {
                // Read the resolved provision. requirePercent = MULTI-RATE article (Art.52): clip to the
                // header + the parts carrying the applicable rate LINE. Otherwise the provision is already
                // a bounded, self-contained unit (CDPF 112 = 8 parts, CTVA 19 = 3 parts) → return it whole.
                var res0 = await session.RunAsync($@"
                    MATCH (c:Chunk)
                    WHERE c.provision_uid = $uid AND c.content <> ''{NoAr}
                      AND ( NOT $needPct
                            OR coalesce(c.part_number, 1) = 1
                            OR ( ($contains = '' OR toLower(c.content) CONTAINS $contains)
                                 AND c.content CONTAINS '%' ) )
                    RETURN {F}, 0.98 AS score
                    ORDER BY coalesce(c.part_number, 1) ASC LIMIT 16",
                    new { uid, contains, needPct = requirePercent });
                await foreach (var r in res0)
                { var t = r["text"]?.As<string>() ?? ""; if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.98); }
                if (results.Count > 0)
                {
                    _logger.LogInformation("FetchArticleLines doc='{D}' art={A}: resolved provision_uid='{U}' → {N} parts (collision-safe)",
                        docFragment, articleNumber, uid, results.Count);
                    return results;
                }
            }

            // (1) FALLBACK — newest edition of this article within the doc family (old graph, or no
            //     disambiguating predicate). Kept intact for whole-article graphs and unambiguous articles.
            string? newest = null;
            var docRes = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS $frag)
                  AND toString(c.article_number) = $num AND c.content <> ''{NoAr}
                RETURN coalesce(c.doc_id, c.document_id) AS doc
                ORDER BY doc DESC LIMIT 1", new { frag, num });
            await foreach (var r in docRes) { newest = r["doc"].As<string>(); break; }
            if (newest is null) return results;

            // (2) header + predicate-matching parts + NEXT_PART neighbours, in reading order.
            //     Clip to the anchor line ONLY for a multi-rate article ($needPct); otherwise return the
            //     whole article — so on the old whole-article graph a single-regime article (CDPF 112)
            //     still comes back complete even though TextContains is now always supplied as an anchor.
            var res = await session.RunAsync($@"
                MATCH (c:Chunk)
                WHERE coalesce(c.doc_id, c.document_id) = $doc
                  AND toString(c.article_number) = $num AND c.content <> ''{NoAr}
                  AND ( NOT $needPct
                        OR coalesce(c.part_number, 1) = 1
                        OR ( ($contains = '' OR toLower(c.content) CONTAINS $contains)
                             AND c.content CONTAINS '%' ) )
                OPTIONAL MATCH (c)-[:NEXT_PART]->(nx:Chunk)
                    WHERE toString(nx.article_number) = $num
                OPTIONAL MATCH (pv:Chunk)-[:NEXT_PART]->(c)
                    WHERE toString(pv.article_number) = $num
                WITH collect(DISTINCT c) + collect(DISTINCT nx) + collect(DISTINCT pv) AS nodes
                UNWIND nodes AS n
                WITH DISTINCT n WHERE n IS NOT NULL
                WITH n AS c
                RETURN {F}, 0.97 AS score
                ORDER BY coalesce(c.part_number, 0) ASC LIMIT 12",
                new { doc = newest, num, contains, needPct = requirePercent });
            await foreach (var r in res)
            { var t = r["text"]?.As<string>() ?? ""; if (!ContainsArabic(t)) TryAdd(results, seen, r, 0.97); }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "FetchArticleLines {D} art {A}", docFragment, articleNumber); }
        // Log the RESOLVED document id, not just the fragment — on the corrected graph art=112
        // resolves to code_droits_procedures_fiscaux_2025 and returns 8 parts; 1 part from a doc
        // with no part numbers means the app is looking at the old whole-article graph.
        _logger.LogInformation("FetchArticleLines doc='{D}' → resolved='{R}' art={A} contains='{C}' pct={P}: {N} parts",
            docFragment, results.FirstOrDefault()?.DocName ?? "(none)", articleNumber, mustContain, requirePercent, results.Count);
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

            // 2) Topic-mediated — in taxmindvf the convention subject is a Chunk property
            //    (topic_label, e.g. "Redevances", "Bénéfices des entreprises"), not a (:Topic)
            //    node, so match chunks whose topic_label contains the subject directly.
            foreach (var topic in (topics ?? Array.Empty<string>()).Take(6))
            {
                if (results.Count >= 14 || string.IsNullOrWhiteSpace(topic)) continue;
                try
                {
                    var res = await session.RunAsync($@"
                        MATCH (c:Chunk)
                        WHERE c.topic_label IS NOT NULL AND toLower(c.topic_label) CONTAINS toLower($topic)
                          AND c.content <> '' AND ($frag = '' OR toLower(coalesce(c.doc_id, c.document_id)) CONTAINS toLower($frag))
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
            ProvisionUid = r.Keys.Contains("provision_uid") ? r["provision_uid"]?.As<string>() ?? "" : "",
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
