using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Profiscal.Domain.Abstractions.Agents;
using Profiscal.Domain.Dtos;
using Profiscal.Infrastructure.Embeddings;

namespace Profiscal.Infrastructure.Agents;

/// <summary>
/// Semantic (vector) search over the legal corpus.
///
/// This used to POST the query to a Python sidecar (<c>embed_server.py</c> on :8081) which
/// embedded the text and ran the Neo4j vector query. Both halves now run in-process:
/// <see cref="OnnxEmbedder"/> produces the query vector, and the graph query is issued through
/// the Neo4j driver. That removes an entire runtime from every environment — Python venv, pip
/// install, a listening port, a health check, and the "did you start the embed server?" step —
/// and removes a network hop from every search.
///
/// The vectors are unchanged: the ONNX model is the same paraphrase-multilingual-MiniLM-L12-v2
/// that produced the stored <c>chunk_embeddings</c>, and the port reproduces it exactly (the
/// measured parity figures are documented on <see cref="OnnxEmbedder"/>). The score floor,
/// document-type inference and returned shape are carried over verbatim, so callers see no
/// behavioural difference.
///
/// Failure policy is deliberately unchanged: any failure returns an EMPTY list rather than
/// throwing. Semantic search enriches the keyword/graph retrieval — a consultation must still be
/// produced when it is unavailable.
/// </summary>
public sealed class EmbedSearchAgent : IEmbedSearchAgent, IDisposable
{
    /// <summary>Cosine floor. Legal text scored against a natural-language question sits lower
    /// than symmetric sentence pairs, so this is below the usual 0.45 — carried over from the
    /// Python implementation so recall is identical.</summary>
    private const double MinScore = 0.30;

    private readonly IDriver _driver;
    private readonly string _database;
    private readonly OnnxEmbedder _embedder;
    private readonly ILogger<EmbedSearchAgent> _logger;

    public EmbedSearchAgent(IConfiguration config, OnnxEmbedder embedder, ILogger<EmbedSearchAgent> logger)
    {
        _embedder = embedder;
        _logger   = logger;

        // Same resolution order as the rest of Infrastructure: .env wins, then appsettings.
        var uri  = Environment.GetEnvironmentVariable("NEO4J_URI")
                   ?? config["Neo4j:Uri"] ?? "neo4j://127.0.0.1:7687";
        var user = Environment.GetEnvironmentVariable("NEO4J_USERNAME")
                   ?? config["Neo4j:Username"] ?? "neo4j";
        var pass = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")
                   ?? (config["Neo4j:Password"] is { Length: > 0 } p ? p : "neo4j");
        _database = Environment.GetEnvironmentVariable("NEO4J_DATABASE")
                    ?? config["Neo4j:Database"] ?? "taxmindvf";

        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, pass));
    }

    public Task<List<LegalSourceDto>> SearchAsync(string query, int topK = 20)
        => SearchInternal(query, "", topK);

    /// <summary>Vector search restricted to one document family (e.g. a country's convention);
    /// the filter is a case-insensitive substring of the document id.</summary>
    public Task<List<LegalSourceDto>> SearchScopedAsync(string query, string docFilter, int topK = 8)
        => SearchInternal(query, docFilter ?? "", topK);

    private async Task<List<LegalSourceDto>> SearchInternal(string query, string docFilter, int topK)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();

        try
        {
            var embedding = _embedder.Embed(query);

            // Over-fetch when filtering: the vector index ranks globally, so applying a narrow
            // filter afterwards would otherwise yield far fewer than topK rows.
            var filter = docFilter.ToLowerInvariant();
            var fetchK = filter.Length > 0 ? Math.Max(topK * 8, 64) : topK;

            await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));
            var cursor = await session.RunAsync(@"
                CALL db.index.vector.queryNodes('chunk_embeddings', $fetchK, $emb)
                YIELD node AS c, score
                WHERE c.content <> '' AND score >= $minScore
                  AND ($filter = '' OR toLower(coalesce(c.doc_id, c.document_id, '')) CONTAINS $filter)
                RETURN coalesce(c.doc_id, c.document_id, '')              AS doc_name,
                       coalesce(c.chunk_id, '')                          AS chunk_id,
                       c.content                                         AS text,
                       coalesce(c.article_display, c.article_number, '') AS article_ref,
                       coalesce(c.section_title, '')                     AS section_title,
                       coalesce(c.annee, '')                             AS annee,
                       score
                ORDER BY score DESC
                LIMIT $topK",
                new { fetchK, emb = embedding, minScore = MinScore, filter, topK });

            var results = new List<LegalSourceDto>();
            var index   = 1;
            await foreach (var r in cursor)
            {
                var docName = r["doc_name"].As<string>() ?? "";
                var docType = Infer(docName);
                results.Add(new LegalSourceDto
                {
                    Index        = index++,
                    ChunkId      = r["chunk_id"].As<string>() ?? "",
                    DocName      = docName,
                    DocType      = docType,
                    ArticleRef   = r["article_ref"].As<string>() ?? "",
                    SectionTitle = r["section_title"].As<string>() ?? "",
                    Year         = r["annee"].As<string>() is { Length: > 0 } y ? y : Year(docName),
                    Text         = r["text"].As<string>() ?? "",
                    Score        = r["score"].As<double>(),
                    IsExpert     = docType == "Commentaire",
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embed [{F}] failed", docFilter);
            return new();
        }
    }

    // Classification kept byte-identical to the previous implementation — the corpus encodes the
    // document family in the id, and downstream code (citations, expert flagging) depends on it.
    private static string Infer(string n)
    {
        n = n.ToLower();
        if (n.Contains("convention")) return "Convention";
        if (n.Contains("code"))       return "Code";
        if (n.Contains("loi") || n.Contains("finances")) return "LoiFinances";
        if (n.Contains("note") || n.Contains("doctrine")) return "Doctrine";
        if (n.Contains("commentaire") || n.Contains("choyakh")) return "Commentaire";
        return "Autre";
    }

    private static string Year(string n)
    {
        var m = Regex.Match(n, @"20\d\d");
        return m.Success ? m.Value : "";
    }

    public void Dispose() => _driver.Dispose();
}
