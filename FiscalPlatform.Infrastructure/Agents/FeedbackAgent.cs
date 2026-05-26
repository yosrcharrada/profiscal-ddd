using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

public sealed class FeedbackAgent(IConfiguration config, IHttpClientFactory factory, ILogger<FeedbackAgent> logger) : IFeedbackAgent
{
    private readonly string _host = (config["Elasticsearch:Host"] is { Length: > 0 } h) ? h : (Environment.GetEnvironmentVariable("ES_HOST") ?? "http://localhost:9200");
    private const string RatingIndex = "profiscal_ratings";

    public async Task SaveRatingAsync(Guid id, string reference, string client, int stars, string? comment, CancellationToken ct = default)
    {
        try
        {
            var http = factory.CreateClient();
            var doc  = JsonSerializer.Serialize(new
            {
                consultation_id = id.ToString(), reference, client_name = client,
                stars, comment, rated_at = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            });
            var url  = $"{_host}/{RatingIndex}/_doc/{id:N}";
            await http.PutAsync(url, new StringContent(doc, Encoding.UTF8, "application/json"), ct);
            logger.LogInformation("Rating saved [{Ref}] {Stars}★", reference, stars);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Rating save failed (non-critical)"); }
    }

    public async Task<double> GetAverageRatingAsync(string clientName, CancellationToken ct = default)
    {
        try
        {
            var http  = factory.CreateClient();
            var query = JsonSerializer.Serialize(new
            {
                query = new { match = new { client_name = clientName } },
                aggs  = new { avg_rating = new { avg = new { field = "stars" } } },
                size  = 0,
            });
            var resp = await http.PostAsync($"{_host}/{RatingIndex}/_search",
                new StringContent(query, Encoding.UTF8, "application/json"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("aggregations").GetProperty("avg_rating")
                .GetProperty("value").GetDouble();
        }
        catch { return 0; }
    }
}
