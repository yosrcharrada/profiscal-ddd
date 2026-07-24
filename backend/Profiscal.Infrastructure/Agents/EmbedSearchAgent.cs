using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

public sealed class EmbedSearchAgent(IHttpClientFactory factory, ILogger<EmbedSearchAgent> logger) : IEmbedSearchAgent
{
    private const string Url = "http://127.0.0.1:8081/embed_search";

    public Task<List<LegalSourceDto>> SearchAsync(string query, int topK = 20) => Call(query, "", topK);
    public Task<List<LegalSourceDto>> SearchScopedAsync(string query, string filter, int topK = 8) => Call(query, filter, topK);

    private async Task<List<LegalSourceDto>> Call(string query, string filter, int topK)
    {
        try
        {
            var client = factory.CreateClient(); client.Timeout = TimeSpan.FromSeconds(8);
            var body   = JsonSerializer.Serialize(new { query, top_k = topK, doc_filter = filter.ToLower() });
            var resp   = await client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return new();
            var json = await resp.Content.ReadAsStringAsync();
            var hits = JsonSerializer.Deserialize<List<EmbedHit>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            return hits.Select((h, i) =>
            {
                var dn = h.Doc_Name ?? ""; var dt = h.Doc_Type ?? Infer(dn);
                return new LegalSourceDto { Index=i+1, ChunkId=h.Chunk_Id??"", DocName=dn, DocType=dt,
                    ArticleRef=h.Article_Ref??"", SectionTitle=h.Section_Title??"",
                    Year=h.Annee??Year(dn), Text=h.Text??"", Score=h.Score, IsExpert=dt=="Commentaire" };
            }).ToList();
        }
        catch (Exception ex) { logger.LogDebug(ex, "Embed [{F}] failed", filter); return new(); }
    }

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
    private static string Year(string n) { var m = System.Text.RegularExpressions.Regex.Match(n, @"20\d\d"); return m.Success ? m.Value : ""; }

    private sealed class EmbedHit { public string? Chunk_Id{get;set;} public string? Doc_Name{get;set;} public string? Doc_Type{get;set;} public string? Article_Ref{get;set;} public string? Section_Title{get;set;} public string? Annee{get;set;} public string? Text{get;set;} public double Score{get;set;} }
}
