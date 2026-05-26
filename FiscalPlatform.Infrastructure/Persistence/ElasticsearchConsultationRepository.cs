using System.Text;
using System.Text.Json;
using FiscalPlatform.Domain.Aggregates.Consultation;
using FiscalPlatform.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Persistence;

public sealed class ElasticsearchConsultationRepository(
    IConfiguration config, IHttpClientFactory factory,
    ILogger<ElasticsearchConsultationRepository> logger)
    : IConsultationRepository
{
    private readonly string _host = (config["Elasticsearch:Host"] is { Length: > 0 } h) ? h : (Environment.GetEnvironmentVariable("ES_HOST") ?? "http://localhost:9200");
    private const string Index = "profiscal_consultations";
    private readonly HttpClient _http = factory.CreateClient();

    public async Task SaveAsync(Consultation c, CancellationToken ct = default)
    {
        try
        {
            var doc = JsonSerializer.Serialize(new
            {
                domain_id = c.Id.ToString(), client_name = c.ClientName, reference = c.Reference,
                date = c.GeneratedAt.ToString("yyyy-MM-dd"), situation = c.Situation,
                fiscal_question = c.FiscalQuestion, sommaire = c.SommairExecutif,
                analyses = c.Analyses, documents = c.Documents, method = c.Method,
                sources_count = c.SourcesCount, elapsed_min = Math.Round(c.ElapsedMs / 60000.0, 2),
                rating = c.Rating, rating_comment = c.RatingComment,
                branches = c.Branches.Select(b => b.Code).ToArray(),
                countries = c.Countries.ToArray(), is_international = c.IsInternational,
                refine_count = c.RefineCount,
            });
            var id  = Uri.EscapeDataString($"{c.Reference}_{c.Id:N[..8]}");
            await _http.PutAsync($"{_host}/{Index}/_doc/{id}",
                new StringContent(doc, Encoding.UTF8, "application/json"), ct);
            logger.LogDebug("ES saved [{Ref}]", c.Reference);
        }
        catch (Exception ex) { logger.LogWarning(ex, "ES save failed [{Ref}]", c.Reference); }
    }

    public async Task<IReadOnlyList<ConsultationSummary>> SearchByClientAsync(string client, CancellationToken ct = default)
    {
        try
        {
            var q = JsonSerializer.Serialize(new
            {
                query = new { multi_match = new { query = client, fields = new[]{"client_name^3","situation","fiscal_question"}, fuzziness = "AUTO" } },
                sort  = new object[]{ new { date = new { order = "desc" } } }, size = 10,
            });
            var resp = await _http.PostAsync($"{_host}/{Index}/_search",
                new StringContent(q, Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<ConsultationSummary>();
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("hits").GetProperty("hits").EnumerateArray()
                .Select(h =>
                {
                    var s = h.GetProperty("_source");
                    return new ConsultationSummary(
                        Guid.TryParse(Str(s,"domain_id"), out var gid) ? gid : Guid.Empty,
                        Str(s,"client_name"), Str(s,"reference"), Str(s,"date"),
                        Str(s,"sommaire"), Str(s,"analyses"), Str(s,"method"),
                        s.TryGetProperty("sources_count", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0,
                        s.TryGetProperty("elapsed_min",   out var em) && em.ValueKind == JsonValueKind.Number ? em.GetDouble() : 0,
                        s.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null,
                        Str(s,"rating_comment"),
                        s.TryGetProperty("branches", out var br) && br.ValueKind == JsonValueKind.Array
                            ? br.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>(),
                        s.TryGetProperty("countries", out var co) && co.ValueKind == JsonValueKind.Array
                            ? co.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>(),
                        s.TryGetProperty("is_international", out var ii) && ii.ValueKind == JsonValueKind.True);
                }).ToList().AsReadOnly();
        }
        catch (Exception ex) { logger.LogWarning(ex, "ES search failed [{C}]", client); return Array.Empty<ConsultationSummary>(); }
    }

    public Task<Consultation?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Consultation?>(null);

    public async Task UpdateRatingAsync(Guid id, int stars, string? comment, CancellationToken ct = default)
    {
        try
        {
            var update = JsonSerializer.Serialize(new { doc = new { rating = stars, rating_comment = comment } });
            await _http.PostAsync($"{_host}/{Index}/_update/{id:N}",
                new StringContent(update, Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Rating update failed"); }
    }

    private static string Str(JsonElement el, string k) =>
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
