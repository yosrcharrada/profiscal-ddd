using FiscalPlatform.Application.Common.DTOs;

namespace FiscalPlatform.Application.Common.Interfaces.Agents;

// ─── RETRIEVAL AGENT ─────────────────────────────────────────────────────────
public interface IRetrievalAgent
{
    Task<List<LegalSourceDto>> RetrieveSourcesAsync(
        List<string> keywords, List<string> entities, List<string> countries,
        bool isInternational, HashSet<string> branches,
        List<LegalSourceDto> conventionEmbedHints, int maxResults = 30,
        CancellationToken ct = default);

    Task<bool> IsAliveAsync();
    Task<KnowledgeBaseStatsDto> GetStatsAsync();

    // For GraphRAG chat
    Task<List<SourceChunkDto>> VectorSearchAsync(float[] embedding, int topK = 8);
    Task<List<SourceChunkDto>> GraphExpandAsync(List<string> entities, int topK = 6);
    Task<List<SourceChunkDto>> KeywordFallbackAsync(string query, int topK = 8);
}

// ─── EMBED SEARCH AGENT ──────────────────────────────────────────────────────
public interface IEmbedSearchAgent
{
    Task<List<LegalSourceDto>> SearchAsync(string query, int topK = 20);
    Task<List<LegalSourceDto>> SearchScopedAsync(string query, string docFilter, int topK = 8);
}

// ─── LLM AGENT ───────────────────────────────────────────────────────────────
public interface ILlmAgent
{
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt,
        string label, int maxTokens = 3000, CancellationToken ct = default);
    Task<string?> ChatAsync(IEnumerable<(string Role, string Content)> history,
        string systemPrompt, CancellationToken ct = default);
}

// ─── DOCUMENT GENERATION AGENT ───────────────────────────────────────────────
public interface IDocumentGenerationAgent
{
    byte[] Generate(GenerateDocumentRequest request);
}

public sealed record GenerateDocumentRequest(
    string Reference, string ClientName, string Situation, string FiscalQuestion,
    List<string> Documents, ConsultationOutput Output);

// ─── FEEDBACK AGENT ──────────────────────────────────────────────────────────
public interface IFeedbackAgent
{
    Task SaveRatingAsync(Guid consultationId, string reference,
        string clientName, int stars, string? comment, CancellationToken ct = default);
    Task<double> GetAverageRatingAsync(string clientName, CancellationToken ct = default);
}

// ─── SEARCH AGENT (Elasticsearch legal docs) ─────────────────────────────────
public interface ISearchAgent
{
    Task<SearchResultDto> SearchAsync(SearchRequestDto request, CancellationToken ct = default);
    Task<bool> IsAliveAsync();
    Task<long> CountAsync();
}

// ─── SHARED DTOs for agents ──────────────────────────────────────────────────
public sealed class KnowledgeBaseStatsDto
{
    public long TotalChunks      { get; set; }
    public long TotalEntities    { get; set; }
    public long TotalRelations   { get; set; }
    public long LoisCount        { get; set; }
    public long NotesCount       { get; set; }
    public long CodesCount       { get; set; }
    public long ConventionsCount { get; set; }
    public long TextChunks       { get; set; }
    public long TableChunks      { get; set; }
    public bool GnnActive        { get; set; }
}

public sealed class SourceChunkDto
{
    public string DocName    { get; set; } = "";
    public int    PageNum    { get; set; }
    public string Text       { get; set; } = "";
    public string ChunkType  { get; set; } = "";
    public string ArticleRef { get; set; } = "";
    public double Score      { get; set; }
    public string Category   { get; set; } = "";
}

public sealed class SearchRequestDto
{
    public string Query     { get; set; } = "";
    public string DocType   { get; set; } = "all";
    public string ChunkType { get; set; } = "all";
    public int    YearMin   { get; set; } = 2000;
    public int    YearMax   { get; set; } = 2030;
    public int    Size      { get; set; } = 50;
}

public sealed class SearchResultDto
{
    public List<SearchHitDto> Hits          { get; set; } = new();
    public int                Total         { get; set; }
    public double             ElapsedMs     { get; set; }
    public double             MaxScore      { get; set; }
    public List<AggBucketDto> DocTypeBuckets   { get; set; } = new();
    public List<AggBucketDto> ChunkTypeBuckets { get; set; } = new();
}

public sealed class SearchHitDto
{
    public string  Id           { get; set; } = "";
    public double  Score        { get; set; }
    public string  Content      { get; set; } = "";
    public string  Filename     { get; set; } = "";
    public string  ArticleNumber { get; set; } = "";
    public string  SectionTitle { get; set; } = "";
    public string  ChunkType    { get; set; } = "";
    public string  DocumentType { get; set; } = "";
    public int?    PageNumber   { get; set; }
    public string  Highlight    { get; set; } = "";
}

public sealed record AggBucketDto(string Key, long Count);
