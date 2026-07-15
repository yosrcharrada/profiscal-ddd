namespace Profiscal.Domain.Entities;

/// <summary>
/// A single item scraped from the IORT "actualité / جميع المستجدات" feed
/// (iort.gov.tn). Each item is a published notice — a tender, a procurement
/// plan or a new-publication announcement. Rows are deduplicated by <see cref="Hash"/>
/// so re-scraping the same feed never creates duplicates.
/// </summary>
public class JortActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short heading of the notice (div _N_A6 on the source page).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full description paragraph (div _N_A7).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Inferred kind: "Tender", "Plan", "Publication", "Notice".</summary>
    public string Category { get; set; } = "Notice";

    /// <summary>Publication date parsed from the source (div _N_A8, dd/MM/yyyy).</summary>
    public DateTime? PublishedOn { get; set; }

    /// <summary>Raw date text exactly as shown on the source page.</summary>
    public string PublishedText { get; set; } = string.Empty;

    /// <summary>SHA-256 of title|date|description — the dedupe key.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>When this row was first inserted (i.e. first observed by the scraper).</summary>
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the scraper last confirmed this item still appears in the feed.</summary>
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}
