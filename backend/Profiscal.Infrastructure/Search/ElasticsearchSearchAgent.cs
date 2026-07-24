using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Profiscal.Domain.Abstractions.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Profiscal.Infrastructure.Search;

public sealed class ElasticsearchSearchAgent(
    IConfiguration config, IHttpClientFactory factory,
    ILogger<ElasticsearchSearchAgent> logger) : ISearchAgent
{
    private readonly string _host  = config["Elasticsearch:Host"]  ?? "http://localhost:9200";
    private readonly string _index = config["Elasticsearch:Index"] ?? "tunisian_legal";
    private readonly HttpClient _http = factory.CreateClient();

    // French stop-words excluded from highlight matching only (never from the main
    // query) — mirrors the old Streamlit app's _meaningful_terms(), so filler words
    // like "de"/"la"/"est" don't get spuriously wrapped in <em> marks.
    private static readonly HashSet<string> FrenchStop = new(StringComparer.OrdinalIgnoreCase)
    {
        "le","la","les","l","de","du","des","d","un","une","au","aux",
        "en","et","est","à","a","par","pour","sur","dans","avec","que",
        "qui","qu","se","sa","son","ses","ce","cette","ces","il","ils",
        "elle","elles","je","tu","nous","vous","on","y","ne","pas","plus",
        "ou","si","car","mais","donc","ni","dont","où","lors","dès","tout",
        "tous","toute","toutes","leur","leurs","même","entre","sous","sans",
        "avant","après","pendant","depuis","sont","sera","été","ainsi","soit",
        "tel","tels","telle","selon","afin","notamment","également","lorsque",
    };

    private static readonly Regex WordRe = new(@"[\wÀ-ž]+", RegexOptions.Compiled);

    private static List<string> MeaningfulTerms(string query) =>
        WordRe.Matches(query.ToLowerInvariant())
              .Select(m => m.Value)
              .Where(t => !FrenchStop.Contains(t) && t.Length >= 3)
              .ToList();

    public async Task<SearchResultDto> SearchAsync(SearchRequestDto req, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var filters = new List<object>();
        // The index is built by the folder-based classifier (document_processor.py),
        // whose `document_type` values are exactly the taxonomy the UI sends:
        // Code / Convention / LoiFinances / Doctrine / Commentaire — so pass through directly.
        if (req.DocType   != "all") filters.Add(new { term = new { document_type = req.DocType } });
        if (req.ChunkType != "all") filters.Add(new { term = new { chunk_type = req.ChunkType } });

        // Filter-only search: with no query text, match everything and let the filters do the
        // narrowing (e.g. "show me all Conventions"). Otherwise run the fuzzy multi_match.
        object must = string.IsNullOrWhiteSpace(req.Query)
            ? new object[] { new { match_all = new { } } }
            : MultiMatch(req.Query);

        object query = filters.Any()
            ? (object)new { @bool = new { must, filter = filters } }
            :          new { @bool = new { must } };

        // Same trick as the old app: highlight only against the meaningful terms of
        // the query (stop-words stripped) so highlighting stays clean even though the
        // underlying search still runs the full fuzzy multi_match.
        var terms = MeaningfulTerms(req.Query);
        object highlightQuery = terms.Count > 0
            ? new
              {
                  @bool = new
                  {
                      should = terms.Select(t => (object)new
                      {
                          multi_match = new { query = t, fields = new[] { "content" }, fuzziness = "AUTO", prefix_length = 2 }
                      }).ToArray(),
                      minimum_should_match = 1,
                  }
              }
            : query;

        var body = JsonSerializer.Serialize(new
        {
            size  = req.Size,
            query,
            // Google model: collapse the passage-level matches to ONE hit per document —
            // the top-scoring passage represents the document, inner_hits carries the count
            // of matching passages in that document ("N passages" on the card).
            collapse = new
            {
                field = "document_id",
                inner_hits = new { name = "passages", size = 1 }
            },
            highlight = new
            {
                highlight_query = highlightQuery,
                require_field_match = false,
                fields = new
                {
                    content        = new { number_of_fragments=3, fragment_size=200, pre_tags=new[]{"<em>"}, post_tags=new[]{"</em>"} },
                    article_number = new { number_of_fragments=1 },
                    section_title  = new { number_of_fragments=1 }
                }
            },
            aggs = new
            {
                // With collapse, hits.total counts passages, not documents — this gives the
                // true distinct-document total for the "N résultats" header.
                distinct_docs = new { cardinality = new { field = "document_id" } },
                doc_types     = new { terms = new { field="document_type", size=10 } },
                chunk_types   = new { terms = new { field="chunk_type",    size=20 } }
            },
            _source = new[]{"content","filename","article_number","section_title","chunk_type","document_type","page_number","chunk_id","document_id","seq"}
        });

        var resp     = await _http.PostAsync($"{_host}/{_index}/_search",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var bodyText = await resp.Content.ReadAsStringAsync(ct);
        sw.Stop();
        return Parse(bodyText, sw.Elapsed.TotalMilliseconds);
    }

    public async Task<bool> IsAliveAsync()
    {
        try { var r = await _http.GetAsync($"{_host}/_cluster/health?timeout=3s"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<long> CountAsync()
    {
        try
        {
            var r = await _http.GetAsync($"{_host}/{_index}/_count");
            var b = await r.Content.ReadAsStringAsync();
            using var d = JsonDocument.Parse(b);
            return d.RootElement.GetProperty("count").GetInt64();
        }
        catch { return 0; }
    }

    public async Task<LegalDocumentDto?> GetDocumentAsync(string documentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;
        // Pull every chunk of the document, ordered by reading sequence, and stitch them
        // back into the full text. `seq` is stamped at chunk time in document order.
        var body = JsonSerializer.Serialize(new
        {
            size  = 2000,
            query = new { term = new { document_id = documentId } },
            sort  = new object[] { new { seq = new { order = "asc" } } },
            _source = new[] { "content", "filename", "document_type", "chunk_type", "seq" }
        });
        try
        {
            var resp = await _http.PostAsync($"{_host}/{_index}/_search",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
            if (hits.GetArrayLength() == 0) return null;

            var sb = new StringBuilder();
            string filename = "", docType = "";
            int n = 0;
            foreach (var h in hits.EnumerateArray())
            {
                var src = h.GetProperty("_source");
                if (n == 0) { filename = Str(src, "filename"); docType = Str(src, "document_type"); }
                var c = Str(src, "content");
                if (!string.IsNullOrWhiteSpace(c)) { sb.Append(c.Trim()); sb.Append("\n\n"); }
                n++;
            }
            return new LegalDocumentDto
            {
                DocumentId = documentId, Filename = filename,
                DocumentType = docType, Text = sb.ToString().TrimEnd(), ChunkCount = n
            };
        }
        catch { return null; }
    }

    public async Task<string?> ResolveFilenameAsync(string documentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;
        var body = JsonSerializer.Serialize(new
        {
            size    = 1,
            query   = new { term = new { document_id = documentId } },
            _source = new[] { "filename" }
        });
        try
        {
            var resp = await _http.PostAsync($"{_host}/{_index}/_search",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
            return hits.GetArrayLength() == 0 ? null : Str(hits[0].GetProperty("_source"), "filename");
        }
        catch (Exception ex) { logger.LogWarning(ex, "ResolveFilename failed for {Id}", documentId); return null; }
    }

    private static object MultiMatch(string q) => new object[]
    {
        new
        {
            multi_match = new
            {
                query          = q,
                fields         = new[] { "content^3", "article_number^2", "section_title^1.5", "subsection_title^1.2", "filename" },
                type           = "best_fields",
                fuzziness      = "AUTO",
                prefix_length  = 2,
                max_expansions = 50,
                tie_breaker    = 0.3,
            }
        }
    };

    private static SearchResultDto Parse(string body, double elapsed)
    {
        var r = new SearchResultDto { ElapsedMs = elapsed };
        try
        {
            using var doc  = JsonDocument.Parse(body);
            var hits = doc.RootElement.GetProperty("hits");
            r.MaxScore = hits.TryGetProperty("max_score", out var ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetDouble() : 1.0;
            var aggsPresent = doc.RootElement.TryGetProperty("aggregations", out var aggs);
            // Collapse means hits.total = passages; the distinct-document count is the cardinality agg.
            r.Total = aggsPresent && aggs.TryGetProperty("distinct_docs", out var dd) &&
                      dd.TryGetProperty("value", out var ddv) && ddv.ValueKind == JsonValueKind.Number
                ? ddv.GetInt32()
                : hits.GetProperty("total").GetProperty("value").GetInt32();
            foreach (var h in hits.GetProperty("hits").EnumerateArray())
            {
                var src = h.GetProperty("_source");
                var hit = new SearchHitDto
                {
                    Id           = h.GetProperty("_id").GetString() ?? "",
                    Score        = h.GetProperty("_score").GetDouble(),
                    Content      = Str(src,"content"),     Filename = Str(src,"filename"),
                    ArticleNumber= Str(src,"article_number"), SectionTitle = Str(src,"section_title"),
                    ChunkType    = Str(src,"chunk_type"),  DocumentType = Str(src,"document_type"),
                    DocumentId   = Str(src,"document_id"),
                    PageNumber   = src.TryGetProperty("page_number", out var pn) && pn.ValueKind == JsonValueKind.Number ? pn.GetInt32() : null,
                };
                // Number of matching passages in this document (from the collapse inner_hits total).
                hit.MatchCount = 1;
                if (h.TryGetProperty("inner_hits", out var ih) &&
                    ih.TryGetProperty("passages", out var pg) &&
                    pg.TryGetProperty("hits", out var ph) &&
                    ph.TryGetProperty("total", out var pt) &&
                    pt.TryGetProperty("value", out var ptv) && ptv.ValueKind == JsonValueKind.Number)
                    hit.MatchCount = Math.Max(1, ptv.GetInt32());
                if (h.TryGetProperty("highlight", out var hl))
                {
                    var parts = new List<string>();
                    if (hl.TryGetProperty("content", out var c)) foreach (var f in c.EnumerateArray()) parts.Add(f.GetString() ?? "");
                    hit.Highlight = string.Join(" … ", parts);
                }
                if (string.IsNullOrEmpty(hit.Highlight)) hit.Highlight = hit.Content.Length > 300 ? hit.Content[..300] + "…" : hit.Content;
                r.Hits.Add(hit);
            }
            if (aggsPresent)
            {
                if (aggs.TryGetProperty("doc_types",   out var dt)) r.DocTypeBuckets   = Buckets(dt);
                if (aggs.TryGetProperty("chunk_types", out var ct)) r.ChunkTypeBuckets = Buckets(ct);
            }
        }
        catch { }
        return r;
    }

    private static List<AggBucketDto> Buckets(JsonElement agg) =>
        agg.GetProperty("buckets").EnumerateArray()
           .Select(b => new AggBucketDto(b.GetProperty("key").GetString()!, b.GetProperty("doc_count").GetInt64()))
           .ToList();

    private static string Str(JsonElement el, string k) =>
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
