using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Search;

public sealed class ElasticsearchSearchAgent(
    IConfiguration config, IHttpClientFactory factory,
    ILogger<ElasticsearchSearchAgent> logger) : ISearchAgent
{
    private readonly string _host  = config["Elasticsearch:Host"]  ?? "http://localhost:9200";
    private readonly string _index = config["Elasticsearch:Index"] ?? "tunisian_legal";
    private readonly HttpClient _http = factory.CreateClient();

    public async Task<SearchResultDto> SearchAsync(SearchRequestDto req, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var filters = new List<object>();
        if (req.DocType   != "all") filters.Add(new { term = new { document_type = req.DocType } });
        if (req.ChunkType != "all") filters.Add(new { term = new { chunk_type    = req.ChunkType } });

        object query = filters.Any()
            ? (object)new { @bool = new { must = MultiMatch(req.Query), filter = filters } }
            :          new { @bool = new { must = MultiMatch(req.Query) } };

        var body = JsonSerializer.Serialize(new
        {
            size  = req.Size,
            query,
            highlight = new
            {
                fields = new
                {
                    content        = new { number_of_fragments=3, fragment_size=200, pre_tags=new[]{"<em>"}, post_tags=new[]{"</em>"} },
                    article_number = new { number_of_fragments=1 },
                    section_title  = new { number_of_fragments=1 }
                }
            },
            aggs = new
            {
                doc_types   = new { terms = new { field="document_type", size=10 } },
                chunk_types = new { terms = new { field="chunk_type",    size=20 } }
            },
            _source = new[]{"content","filename","article_number","section_title","chunk_type","document_type","page_number","chunk_id"}
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

    private static object MultiMatch(string q) => new object[]
    {
        new { multi_match = new { query=q, fields=new[]{"content^3","article_number^2","section_title^1.5","filename"}, type="best_fields", fuzziness="AUTO", @operator="or" } }
    };

    private static SearchResultDto Parse(string body, double elapsed)
    {
        var r = new SearchResultDto { ElapsedMs = elapsed };
        try
        {
            using var doc  = JsonDocument.Parse(body);
            var hits = doc.RootElement.GetProperty("hits");
            r.Total    = hits.GetProperty("total").GetProperty("value").GetInt32();
            r.MaxScore = hits.TryGetProperty("max_score", out var ms) && ms.ValueKind == JsonValueKind.Number ? ms.GetDouble() : 1.0;
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
                    PageNumber   = src.TryGetProperty("page_number", out var pn) && pn.ValueKind == JsonValueKind.Number ? pn.GetInt32() : null,
                };
                if (h.TryGetProperty("highlight", out var hl))
                {
                    var parts = new List<string>();
                    if (hl.TryGetProperty("content", out var c)) foreach (var f in c.EnumerateArray()) parts.Add(f.GetString() ?? "");
                    hit.Highlight = string.Join(" … ", parts);
                }
                if (string.IsNullOrEmpty(hit.Highlight)) hit.Highlight = hit.Content.Length > 300 ? hit.Content[..300] + "…" : hit.Content;
                r.Hits.Add(hit);
            }
            if (doc.RootElement.TryGetProperty("aggregations", out var aggs))
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
