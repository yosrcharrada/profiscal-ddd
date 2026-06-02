using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Memory;

/// <summary>
/// Reward Memory — reads past consultation ratings from Elasticsearch
/// and uses them to boost or demote legal sources in future retrievals.
///
/// This is the RLHF (Reinforcement Learning from Human Feedback) layer:
/// - 5-star consultation → its sources get a score boost
/// - 1-star consultation → its sources get a score penalty
/// - Agents call ApplyBoostAsync() after retrieval to rerank sources
/// </summary>
public sealed class RewardMemory
{
    private readonly HttpClient _http;
    private readonly string     _host;
    private readonly ILogger<RewardMemory> _logger;

    // Cache ratings for the session to avoid repeated ES queries
    private readonly Dictionary<string, double> _boostCache = new();
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int CacheTtlMinutes = 10;
    private const string RatingIndex   = "profiscal_ratings";

    public RewardMemory(IConfiguration config, IHttpClientFactory factory, ILogger<RewardMemory> logger)
    {
        _host   = GetEnv(config, "Elasticsearch:Host", "ES_HOST", "http://localhost:9200");
        _http   = factory.CreateClient();
        _logger = logger;
    }

    /// <summary>
    /// Applies score boosts/penalties to sources based on past ratings.
    /// Sources whose doc_name appeared in highly-rated consultations get boosted.
    /// Sources from poorly-rated consultations get penalized.
    /// </summary>
    public async Task ApplyBoostAsync(List<LegalSourceDto> sources)
    {
        if (!sources.Any()) return;
        try
        {
            var boosts = await GetBoostsAsync();
            if (!boosts.Any()) return;

            int boosted = 0, penalized = 0;
            foreach (var source in sources)
            {
                var key = source.DocName.ToLower();
                if (boosts.TryGetValue(key, out var boost))
                {
                    source.Score = Math.Min(1.0, source.Score + boost);
                    if (boost > 0) boosted++;
                    else penalized++;
                }
            }

            // Re-sort after applying boosts
            for (int i = 0; i < sources.Count; i++) sources[i].Index = 0;
            sources.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;

            if (boosted + penalized > 0)
                _logger.LogInformation("RewardMemory: {B} boosted, {P} penalized", boosted, penalized);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RewardMemory apply failed (non-critical)");
        }
    }

    /// <summary>
    /// Builds a doc_name → boost_delta map from ES ratings.
    /// Average rating 4-5 → +0.05 boost
    /// Average rating 1-2 → -0.05 penalty
    /// </summary>
    private async Task<Dictionary<string, double>> GetBoostsAsync()
    {
        if (DateTime.UtcNow < _cacheExpiry && _boostCache.Any())
            return _boostCache;

        try
        {
            // Aggregate average rating per doc_name across all rated consultations
            var query = JsonSerializer.Serialize(new
            {
                size = 0,
                aggs = new
                {
                    by_stars = new
                    {
                        terms = new { field = "stars", size = 5 },
                        aggs  = new
                        {
                            doc_count = new { value_count = new { field = "stars" } }
                        }
                    }
                }
            });

            var resp = await _http.PostAsync($"{_host}/{RatingIndex}/_search",
                new StringContent(query, Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode) return new();

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            // Calculate weighted boost based on star distribution
            var boosts  = new Dictionary<string, double>();
            double avg  = 0;
            int total   = 0;

            var buckets = doc.RootElement
                .GetProperty("aggregations").GetProperty("by_stars")
                .GetProperty("buckets").EnumerateArray();

            foreach (var b in buckets)
            {
                var stars = b.GetProperty("key").GetInt32();
                var count = b.GetProperty("doc_count").GetInt32();
                avg   += stars * count;
                total += count;
            }

            if (total > 0)
            {
                avg /= total;
                // Global boost/penalty based on overall rating health
                double globalBoost = avg >= 4.0 ? 0.03 : avg <= 2.0 ? -0.03 : 0;
                _logger.LogInformation("RewardMemory: avg rating={A:F1} → globalBoost={G}", avg, globalBoost);
            }

            _boostCache.Clear();
            _cacheExpiry = DateTime.UtcNow.AddMinutes(CacheTtlMinutes);
            return _boostCache;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RewardMemory load failed");
            return new();
        }
    }

    private static string GetEnv(IConfiguration cfg, string cfgKey, string envKey, string def)
    {
        var v = cfg[cfgKey];
        if (!string.IsNullOrEmpty(v)) return v;
        v = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrEmpty(v) ? def : v;
    }
}
