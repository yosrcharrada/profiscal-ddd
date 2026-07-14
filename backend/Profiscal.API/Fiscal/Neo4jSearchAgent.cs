using FiscalPlatform.Application.Common.Interfaces.Agents;
using Neo4j.Driver;

namespace Profiscal.API.Fiscal;

/// <summary>
/// Neo4j-backed legal search agent for the taxmind graph. Uses Neo4j's native
/// full-text (BM25/Lucene) index `chunk_content` over (Chunk.content, Chunk.title) —
/// real BM25 ranking, no Elasticsearch required. Falls back to CONTAINS scoring if the
/// full-text index is unavailable.
/// </summary>
public sealed class Neo4jSearchAgent : ISearchAgent, IDisposable
{
    private readonly IDriver _driver;
    private readonly string  _db;
    private readonly ILogger<Neo4jSearchAgent> _logger;

    public Neo4jSearchAgent(IConfiguration config, ILogger<Neo4jSearchAgent> logger)
    {
        _logger = logger;
        // .env NEO4J_* vars WIN over appsettings/user-secrets (same precedence as RetrievalAgent):
        // the single root .env drives every Neo4j consumer, .NET and Python alike.
        var uri  = Environment.GetEnvironmentVariable("NEO4J_URI")
                 ?? (config["Neo4j:Uri"]      is { Length: > 0 } u ? u : "neo4j://127.0.0.1:7687");
        var user = Environment.GetEnvironmentVariable("NEO4J_USERNAME")
                 ?? (config["Neo4j:Username"] is { Length: > 0 } n ? n : "neo4j");
        var pass = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")
                 ?? (config["Neo4j:Password"] is { Length: > 0 } p ? p : "");
        var envDb = Environment.GetEnvironmentVariable("NEO4J_DATABASE");
        _db      = !string.IsNullOrWhiteSpace(envDb) ? envDb!
                 : (config["Neo4j:Database"] is { Length: > 0 } d ? d : "taxmind");
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    // doc_type is derived from the corpus a chunk belongs to (taxmind has no Chunk.doc_type).
    private const string DocTypeExpr =
        "CASE c.corpus WHEN 'Conventions' THEN 'Convention' " +
        "WHEN 'Lois_des_Finances' THEN 'LoiFinances' " +
        "WHEN 'Notes_Communes' THEN 'Doctrine' ELSE 'Code' END";

    private static string SanitizeLucene(List<string> terms) =>
        terms.Count == 0 ? "*" : string.Join(" OR ", terms.Select(t => t.Replace("\"", " ")));

    public async Task<SearchResultDto> SearchAsync(SearchRequestDto req, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new SearchResultDto();

        var terms = req.Query.ToLower()
            .Split(new[] { ' ', ',', '.', ';', ':', '?', '!', '"', '\'', '(', ')', '-', '/' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3).Distinct().Take(12).ToList();
        if (terms.Count == 0) terms.Add(req.Query.Trim().ToLower());

        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDatabase(_db));

            // BM25 ranking via the native full-text index; doc_type filter applied on the
            // corpus-derived value; falls back to CONTAINS scoring if the index is missing.
            var size = req.Size <= 0 ? 50 : Math.Min(req.Size, 100);
            var args = new
            {
                terms,
                q         = SanitizeLucene(terms),
                docType   = string.IsNullOrEmpty(req.DocType)   ? "all" : req.DocType,
                chunkType = string.IsNullOrEmpty(req.ChunkType) ? "all" : req.ChunkType,
                corpus    = string.IsNullOrEmpty(req.Corpus)    ? "all" : req.Corpus,
                number    = req.Number   ?? "",
                dateText  = req.DateText ?? "",
                year      = req.Year,
                size,
            };

            // JORT-style metadata filters (Numéro/Année/Date) join the chunk's parent :Document
            // (every Convention/LoiFinances/NoteCommune/CodeFiscal is also a :Document, doc_id-indexed).
            //   • law_number is 'YYYY-NN' → the NUMÉRO is the part AFTER the year (last segment),
            //     the YYYY belongs to ANNÉE. NoteCommune numéro lives in the doc_id suffix (NC_YYYY_NN).
            const string metaFilter = @"
                WITH c, score
                OPTIONAL MATCH (m:Document {doc_id: c.doc_id})
                WITH c, score, m
                WHERE ($number = ''
                       OR last(split(coalesce(m.law_number,''),'-')) CONTAINS $number
                       OR coalesce(m.nc_number,'') CONTAINS $number
                       OR (c.corpus = 'Notes_Communes' AND last(split(c.doc_id,'_')) CONTAINS $number))
                  AND ($year = 0 OR (
                       toString(coalesce(m.fiscal_year, m.year, '')) CONTAINS toString($year)
                       OR head(split(coalesce(m.law_number,''),'-')) = toString($year)
                       OR coalesce(m.law_date,'') CONTAINS toString($year)
                       OR coalesce(m.date_signature,'') CONTAINS toString($year)))
                  AND ($dateText = '' OR coalesce(m.date, m.law_date, m.date_signature, '') CONTAINS $dateText)";

            const string proj = @"
                RETURN c.chunk_id AS id, c.content AS text, c.doc_id AS doc_name,
                       {0} AS doc_type,
                       coalesce(c.article_display, c.article_number, '') AS article_ref,
                       c.title AS section_title, c.chunk_type AS chunk_type,
                       null AS page_num, {1} AS hits";

            var bm25 = $@"
                CALL db.index.fulltext.queryNodes('chunk_content', $q) YIELD node AS c, score
                WHERE c.content <> ''
                  AND ($docType = 'all' OR {DocTypeExpr} = $docType)
                  AND ($chunkType = 'all' OR c.chunk_type = $chunkType)
                  AND ($corpus = 'all' OR c.corpus = $corpus)
                {metaFilter}
                {string.Format(proj, DocTypeExpr, "score")}
                ORDER BY hits DESC
                LIMIT $size";

            var contains = $@"
                MATCH (c:Chunk)
                WHERE c.content <> '' AND c.chunk_type IS NOT NULL
                  AND ($docType = 'all' OR {DocTypeExpr} = $docType)
                  AND ($chunkType = 'all' OR c.chunk_type = $chunkType)
                  AND ($corpus = 'all' OR c.corpus = $corpus)
                  AND ANY(t IN $terms WHERE toLower(c.content) CONTAINS t)
                WITH c, toFloat(SIZE([t IN $terms WHERE toLower(c.content) CONTAINS t])) AS score
                {metaFilter}
                {string.Format(proj, DocTypeExpr, "score")}
                ORDER BY hits DESC, (CASE WHEN c.chunk_type = 'article' THEN 0 ELSE 1 END)
                LIMIT $size";

            IResultCursor cursor;
            try { cursor = await session.RunAsync(bm25, args); _ = await cursor.PeekAsync(); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "full-text index unavailable — using CONTAINS fallback");
                cursor = await session.RunAsync(contains, args);
            }

            var docTypeCounts   = new Dictionary<string, long>();
            var chunkTypeCounts = new Dictionary<string, long>();
            double maxHits = 1;

            await foreach (var r in cursor)
            {
                var text    = r["text"].As<string>() ?? "";
                var hits    = r["hits"].As<double>();
                var docType = r["doc_type"].As<string>() ?? "";
                var chkType = r["chunk_type"].As<string>() ?? "";
                maxHits = Math.Max(maxHits, hits);

                var hit = new SearchHitDto
                {
                    Id            = r["id"].As<string>() ?? "",
                    Score         = hits,
                    Content       = text,
                    Filename      = r["doc_name"].As<string>() ?? "",
                    ArticleNumber = r["article_ref"].As<string>() ?? "",
                    SectionTitle  = r["section_title"].As<string>() ?? "",
                    ChunkType     = chkType,
                    DocumentType  = docType,
                    PageNumber    = r["page_num"]?.As<int?>(),
                    Highlight     = Highlight(text, terms),
                };
                result.Hits.Add(hit);
                if (!string.IsNullOrEmpty(docType)) docTypeCounts[docType] = docTypeCounts.GetValueOrDefault(docType) + 1;
                if (!string.IsNullOrEmpty(chkType)) chunkTypeCounts[chkType] = chunkTypeCounts.GetValueOrDefault(chkType) + 1;
            }

            result.Total            = result.Hits.Count;
            result.MaxScore         = maxHits;
            result.DocTypeBuckets   = docTypeCounts.OrderByDescending(kv => kv.Value)
                                          .Select(kv => new AggBucketDto(kv.Key, kv.Value)).ToList();
            result.ChunkTypeBuckets = chunkTypeCounts.OrderByDescending(kv => kv.Value)
                                          .Select(kv => new AggBucketDto(kv.Key, kv.Value)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neo4j search failed for '{Q}'", req.Query);
        }

        sw.Stop();
        result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
        return result;
    }

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

    public async Task<long> CountAsync()
    {
        try
        {
            await using var s = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await s.RunAsync("MATCH (c:Chunk) RETURN count(c) AS n");
            var rec = await r.SingleAsync();
            return rec["n"].As<long>();
        }
        catch { return 0; }
    }

    public async Task<LegalDocumentDto?> GetDocumentAsync(string documentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;
        try
        {
            await using var s = _driver.AsyncSession(o => o.WithDatabase(_db));
            var r = await s.RunAsync(
                @"MATCH (c:Chunk)
                  WHERE coalesce(c.doc_id, c.document_id) = $id AND c.content <> ''
                  RETURN c.content AS content,
                         coalesce(c.filename, c.doc_id, c.document_id) AS filename
                  ORDER BY coalesce(c.seq, c.part_number, 0)",
                new { id = documentId });
            var recs = await r.ToListAsync();
            if (recs.Count == 0) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var rec in recs)
            {
                var c = rec["content"].As<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(c)) { sb.Append(c.Trim()); sb.Append("\n\n"); }
            }
            return new LegalDocumentDto
            {
                DocumentId = documentId,
                Filename   = recs[0]["filename"].As<string>() ?? documentId,
                Text       = sb.ToString().TrimEnd(),
                ChunkCount = recs.Count
            };
        }
        catch { return null; }
    }

    private static string Highlight(string text, List<string> terms)
    {
        var lower = text.ToLower();
        var idx = -1;
        foreach (var t in terms) { idx = lower.IndexOf(t, StringComparison.Ordinal); if (idx >= 0) break; }
        if (idx < 0) return text.Length > 300 ? text[..300] + "…" : text;
        var start = Math.Max(0, idx - 120);
        var len = Math.Min(text.Length - start, 320);
        var snippet = text.Substring(start, len);
        return (start > 0 ? "…" : "") + snippet + (start + len < text.Length ? "…" : "");
    }

    public void Dispose() => _driver?.Dispose();
}
