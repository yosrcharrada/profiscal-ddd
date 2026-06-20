using FiscalPlatform.Application.Common.Interfaces.Agents;
using Neo4j.Driver;

namespace Profiscal.API.Fiscal;

/// <summary>
/// Neo4j-backed replacement for the original project's Elasticsearch search agent.
/// Provides the "semantic search engine" tab using keyword matching over Chunk nodes
/// in the tunisian-fiscal knowledge base — no Elasticsearch required.
/// </summary>
public sealed class Neo4jSearchAgent : ISearchAgent, IDisposable
{
    private readonly IDriver _driver;
    private readonly string  _db;
    private readonly ILogger<Neo4jSearchAgent> _logger;

    public Neo4jSearchAgent(IConfiguration config, ILogger<Neo4jSearchAgent> logger)
    {
        _logger = logger;
        // Read config first, then fall back to the single-underscore NEO4J_* env vars
        // (same convention the engine's RetrievalAgent uses, so one root .env drives both).
        var uri  = config["Neo4j:Uri"]      is { Length: > 0 } u ? u
                 : Environment.GetEnvironmentVariable("NEO4J_URI")      ?? "neo4j://127.0.0.1:7687";
        var user = config["Neo4j:Username"] is { Length: > 0 } n ? n
                 : Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j";
        var pass = config["Neo4j:Password"] is { Length: > 0 } p ? p
                 : Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "";
        _db      = config["Neo4j:Database"] is { Length: > 0 } d ? d
                 : Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "tunisian-fiscal";
        _driver  = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

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

            // Score by how many distinct query terms a chunk contains; filter by doc/chunk type.
            var cypher = @"
                MATCH (c:Chunk)
                WHERE c.text <> '' AND c.chunk_type IS NOT NULL
                  AND ($docType = 'all' OR c.doc_type = $docType)
                  AND ($chunkType = 'all' OR c.chunk_type = $chunkType)
                  AND ANY(t IN $terms WHERE toLower(c.text) CONTAINS t)
                WITH c, SIZE([t IN $terms WHERE toLower(c.text) CONTAINS t]) AS hits
                RETURN c.chunk_id AS id, c.text AS text, c.doc_name AS doc_name,
                       c.doc_type AS doc_type, c.article_ref AS article_ref,
                       c.section_title AS section_title, c.chunk_type AS chunk_type,
                       c.page_num AS page_num, hits
                ORDER BY hits DESC, (CASE WHEN c.article_ref <> '' THEN 0 ELSE 1 END)
                LIMIT $size";

            var cursor = await session.RunAsync(cypher, new
            {
                terms,
                docType   = string.IsNullOrEmpty(req.DocType)   ? "all" : req.DocType,
                chunkType = string.IsNullOrEmpty(req.ChunkType) ? "all" : req.ChunkType,
                size      = req.Size <= 0 ? 50 : Math.Min(req.Size, 100),
            });

            var docTypeCounts   = new Dictionary<string, long>();
            var chunkTypeCounts = new Dictionary<string, long>();
            double maxHits = 1;

            await foreach (var r in cursor)
            {
                var text    = r["text"].As<string>() ?? "";
                var hits    = r["hits"].As<int>();
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
