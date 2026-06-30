using System.ComponentModel;
using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Infrastructure.Memory;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure.Kernel.Plugins;

/// <summary>
/// SK Plugin — wraps legal source retrieval as kernel functions.
/// The kernel/agent can call these functions when it needs legal sources.
/// Each function is a tool the agent can reason about and invoke.
/// </summary>
public sealed class RetrievalPlugin
{
    private readonly IRetrievalAgent   _retrieval;
    private readonly IEmbedSearchAgent _embed;
    private readonly RewardMemory      _reward;

    public RetrievalPlugin(
        IRetrievalAgent   retrieval,
        IEmbedSearchAgent embed,
        RewardMemory      reward)
    {
        _retrieval = retrieval;
        _embed     = embed;
        _reward    = reward;
    }

    /// <summary>
    /// Semantic vector search for legal sources most similar to the query.
    /// Returns top-K sources ranked by cosine similarity.
    /// The agent calls this when it needs broad semantic coverage.
    /// </summary>
    [KernelFunction("semantic_search")]
    [Description("Search the Tunisian legal knowledge base semantically. Returns the most relevant legal sources (articles, conventions, doctrine) for the given fiscal question.")]
    public async Task<string> SemanticSearch(
        [Description("The fiscal question or situation to search for")] string query,
        [Description("Number of results to return (default 20)")] int topK = 20)
    {
        var sources = await _embed.SearchAsync(query, topK);
        return FormatSources(sources, "Semantic Search");
    }

    /// <summary>
    /// Scoped semantic search within a specific country's convention.
    /// The agent calls this when the case involves an international party.
    /// </summary>
    [KernelFunction("search_convention")]
    [Description("Search a specific country's double-taxation convention. Use this when the case involves a non-resident party or cross-border transaction.")]
    public async Task<string> SearchConvention(
        [Description("The fiscal question")] string query,
        [Description("Country name in French (e.g. france, maroc, allemagne)")] string country,
        [Description("Number of results")] int topK = 8)
    {
        var sources = await _embed.SearchScopedAsync(query, country, topK);
        return FormatSources(sources, $"Convention {country}");
    }

    /// <summary>
    /// Graph-based keyword retrieval from Neo4j.
    /// The agent calls this to find specific code articles by keyword.
    /// Applies reward memory — boosts sources from highly-rated past consultations.
    /// </summary>
    [KernelFunction("keyword_search")]
    [Description("Search the legal knowledge base by keywords. Best for finding specific articles in CIRPPIS, CTVA, CDPF, or Lois de Finances.")]
    public async Task<string> KeywordSearch(
        [Description("List of keywords to search for, comma-separated")] string keywords,
        [Description("Legal branch: IS, IRPP, TVA, Retenue, PrixTransfert, or ALL")] string branch = "ALL")
    {
        var kws      = keywords.Split(',').Select(k => k.Trim()).Where(k => k.Length > 2).ToList();
        var entities = new List<string>();
        var branches = branch == "ALL"
            ? new HashSet<string> { "IS", "IRPP", "TVA", "Retenue", "PrixTransfert" }
            : new HashSet<string> { branch };

        var sources = await _retrieval.RetrieveSourcesAsync(
            kws, entities, new List<string>(), false, branches, new List<LegalSourceDto>(), 20);

        // Apply reward memory — boost highly-rated chunks
        await _reward.ApplyBoostAsync(sources);

        return FormatSources(sources, "Keyword Search");
    }

    /// <summary>
    /// Get all available legal sources for a complete consultation.
    /// The agent calls this when it needs the full retrieval pipeline.
    /// </summary>
    [KernelFunction("full_retrieval")]
    [Description("Run the complete legal source retrieval pipeline for a fiscal consultation. Returns up to 30 sources covering conventions, codes, lois de finances, and doctrine.")]
    public async Task<string> FullRetrieval(
        [Description("The full client situation")] string situation,
        [Description("The fiscal question")] string fiscalQuestion,
        [Description("Detected legal branches, comma-separated (IS,TVA,Retenue...)")] string branches = "")
    {
        var branchSet = branches.Split(',')
            .Select(b => b.Trim()).Where(b => b.Length > 0)
            .ToHashSet();

        var query    = fiscalQuestion + " " + situation[..Math.Min(situation.Length, 200)];
        var embed    = await _embed.SearchAsync(query, 20);
        var sources  = await _retrieval.RetrieveSourcesAsync(
            new List<string>(), new List<string>(), new List<string>(),
            false, branchSet, embed, 30);

        await _reward.ApplyBoostAsync(sources);

        return FormatSources(sources, "Full Retrieval");
    }

    private static string FormatSources(List<LegalSourceDto> sources, string label)
    {
        if (!sources.Any()) return $"[{label}] No sources found.";
        var sb = new StringBuilder($"[{label}] {sources.Count} sources found:\n\n");
        foreach (var s in sources.Take(15))
        {
            var preview = s.Text.Length > 200 ? s.Text[..200] + "…" : s.Text;
            sb.AppendLine($"[S{s.Index}] {s.DocType} | {s.DocName} | {s.Year} | {s.ArticleRef}");
            sb.AppendLine($"       {preview}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
