using Profiscal.Domain.Dtos;

namespace Profiscal.Domain.Abstractions.Agents;

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
    // ─── ADD TO IRetrievalAgent interface ────────────────────────────────────────
    // Targeted fetch methods used by RetrievalPlannerAgent via RetrievalPlannerPlugin

    Task<List<LegalSourceDto>> FetchConventionArticleAsync(
        string country, string[] keywords, CancellationToken ct = default);

    Task<List<LegalSourceDto>> FetchNoteCommune2Async(
        string? country, CancellationToken ct = default);

    Task<List<LegalSourceDto>> FetchDomesticRetenueAsync(
        List<string> keywords, CancellationToken ct = default);

    Task<List<LegalSourceDto>> FetchDomesticTaxRulesAsync(
        string taxType, string[] keywords, CancellationToken ct = default);

    // Precise targeted fetch used by the rule-based retrieval policy: pulls chunks from a
    // given document family (doc_name fragment) matching specific article references
    // (e.g. "Art. 52") and/or keywords. Lets us guarantee a rate-bearing article is in context.
    Task<List<LegalSourceDto>> FetchTargetedAsync(
        string docNameFragment, string[] articleRefs, string[] keywords,
        CancellationToken ct = default);

    // Number-free targeted fetch used by the rule-based policy. Instead of article numbers,
    // it locates a provision by (1) distinctive anchor phrases in the text, (2) taxmind Topic
    // nodes (HAS_TOPIC), and (3) BM25 full-text as a safety net — all scoped to a doc family.
    Task<List<LegalSourceDto>> FetchBySubjectAsync(
        string docFragment, string[] anchorPhrases, string[] topics, string[] keywords,
        CancellationToken ct = default);

    // LINE-PRECISE article fetch: from the NEWEST edition of the article, return the header
    // (part 1) plus ONLY the parts matching the requested line predicates (mustContain /
    // requirePercent) and their NEXT_PART neighbours (a sentence can straddle two parts).
    // On the part-split taxmindvf graph this returns exactly the alinéas a case needs; on the
    // whole-article taxmind graph it degrades gracefully to the article's chunks.
    Task<List<LegalSourceDto>> FetchArticleLinesAsync(
        string docFragment, string articleNumber, string? mustContain, bool requirePercent,
        CancellationToken ct = default);
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

    /// <summary>Streams the completion token-by-token (stream:true). Yields content deltas.</summary>
    IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt,
        string label, CancellationToken ct = default);
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
    /// <summary>Assemble a whole document (all its chunks, in reading order) for the
    /// document-level "open" view — the Google model: results collapse to one card per
    /// document, clicking one shows the entire text.</summary>
    Task<LegalDocumentDto?> GetDocumentAsync(string documentId, CancellationToken ct = default);
    /// <summary>Cheap lookup of just the original PDF filename for a document_id — used to
    /// locate the source PDF on disk for the "open in PDF" action, without paying the cost
    /// of stitching the whole document text together (GetDocumentAsync).</summary>
    Task<string?> ResolveFilenameAsync(string documentId, CancellationToken ct = default);
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
    public string DocType   { get; set; } = "all";   // Convention | LoiFinances | Doctrine | Code
    public string ChunkType { get; set; } = "all";
    public int    YearMin   { get; set; } = 2000;
    public int    YearMax   { get; set; } = 2030;
    public int    Size      { get; set; } = 50;
    // JORT-style corpus filters (taxmind-backed).
    public string Corpus    { get; set; } = "all";    // Conventions | Lois_des_Finances | Notes_Communes | Recueils_textes_fiscaux
    public string Number    { get; set; } = "";       // law_number / nc_number
    public string DateText  { get; set; } = "";       // date / law_date / date_signature (substring)
    public int    Year      { get; set; } = 0;         // 0 = all
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
    // Document-level (field-collapse) fields: which document this hit belongs to, and
    // how many passages in that document matched (shown as "N passages" on the card).
    public string  DocumentId   { get; set; } = "";
    public int     MatchCount   { get; set; } = 1;
}

/// <summary>A whole legal document, its chunks concatenated in reading order — the payload
/// behind clicking a collapsed result to read the full text.</summary>
public sealed class LegalDocumentDto
{
    public string DocumentId   { get; set; } = "";
    public string Filename     { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Text         { get; set; } = "";
    public int    ChunkCount   { get; set; }
}

public sealed record AggBucketDto(string Key, long Count);
