namespace Profiscal.Domain.Entities;

/// <summary>
/// Flattened persistence record for a generated fiscal consultation.
/// This is the storage projection of the FiscalPlatform Consultation aggregate —
/// it replaces the original project's Elasticsearch index so the platform runs
/// on our existing SQLite database. Sections are stored so a consultation can be
/// reopened, edited, re-rated, and exported later.
/// </summary>
public class FiscalConsultation
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;

    public string Situation      { get; set; } = string.Empty;
    public string FiscalQuestion { get; set; } = string.Empty;

    // Generated sections
    public string ContexteFaits   { get; set; } = string.Empty;
    public string Etendue         { get; set; } = string.Empty;
    public string Abbreviations   { get; set; } = string.Empty;
    public string SommairExecutif { get; set; } = string.Empty;
    public string Analyses        { get; set; } = string.Empty;
    public string Documents       { get; set; } = string.Empty;

    /// <summary>Full ConsultationOutput serialized as JSON (sources, table, etc.) for reopening.</summary>
    public string OutputJson { get; set; } = "{}";

    // Detection metadata
    public string Branches        { get; set; } = string.Empty; // comma-separated
    public string Countries       { get; set; } = string.Empty; // comma-separated
    public bool   IsInternational { get; set; }

    public string Method       { get; set; } = string.Empty;
    public int    SourcesCount { get; set; }
    public double ElapsedMs    { get; set; }
    public int    RefineCount  { get; set; }

    public int?    Rating        { get; set; }
    public string? RatingComment { get; set; }

    /// <summary>The authenticated user who created the consultation (our AppUser id).</summary>
    public Guid?     OwnerUserId { get; set; }
    public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt   { get; set; }
}
