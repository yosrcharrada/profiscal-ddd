using FiscalPlatform.Domain.Events;
using FiscalPlatform.Domain.Exceptions;
using FiscalPlatform.Domain.ValueObjects;

namespace FiscalPlatform.Domain.Aggregates.Consultation;

/// <summary>
/// Consultation aggregate root.
/// Owns all invariants: rating range, valid sections, session lifecycle.
/// </summary>
public sealed class Consultation
{
    // Identity
    public Guid   Id        { get; private set; }
    public string Reference { get; private set; } = "";
    public string ClientName { get; private set; } = "";

    // Input
    public string            Situation         { get; private set; } = "";
    public string            FiscalQuestion    { get; private set; } = "";
    public List<string>      AttachedDocuments { get; private set; } = new();

    // Detection
    public IReadOnlySet<LegalBranch> Branches    { get; private set; } = new HashSet<LegalBranch>();
    public IReadOnlyList<string>     Countries   { get; private set; } = new List<string>();
    public bool                      IsInternational { get; private set; }

    // Generated sections
    public string ContexteFaits   { get; private set; } = "";
    public string Etendue         { get; private set; } = "";
    public string Abbreviations   { get; private set; } = "";
    public string SommairExecutif { get; private set; } = "";
    public string Analyses        { get; private set; } = "";
    public string Documents       { get; private set; } = "";

    // Metadata
    public DateTime GeneratedAt  { get; private set; }
    public string   Method       { get; private set; } = "";
    public int      SourcesCount { get; private set; }
    public double   ElapsedMs    { get; private set; }

    // Rating
    public int?    Rating        { get; private set; }
    public string? RatingComment { get; private set; }

    // Session
    public string? ActiveSessionId { get; private set; }
    public int     RefineCount     { get; private set; }

    // Domain events
    private readonly List<IDomainEvent> _events = new();
    public  IReadOnlyList<IDomainEvent> DomainEvents => _events.AsReadOnly();
    public  void ClearDomainEvents() => _events.Clear();

    private Consultation() { }

    public static Consultation Create(
        string reference, string clientName, string situation, string fiscalQuestion,
        IEnumerable<string> attachedDocuments,
        IEnumerable<LegalBranch> branches, IEnumerable<string> countries, bool isInternational,
        string contexteFaits, string etendue, string abbreviations,
        string sommaire, string analyses, string documents,
        string method, int sourcesCount, double elapsedMs)
    {
        if (string.IsNullOrWhiteSpace(clientName))     throw new ArgumentException("ClientName required");
        if (string.IsNullOrWhiteSpace(situation))      throw new ArgumentException("Situation required");
        if (string.IsNullOrWhiteSpace(fiscalQuestion)) throw new ArgumentException("FiscalQuestion required");

        var c = new Consultation
        {
            Id               = Guid.NewGuid(),
            Reference        = reference,
            ClientName       = clientName.Trim(),
            Situation        = situation.Trim(),
            FiscalQuestion   = fiscalQuestion.Trim(),
            AttachedDocuments = attachedDocuments.ToList(),
            Branches         = new HashSet<LegalBranch>(branches),
            Countries        = countries.ToList(),
            IsInternational  = isInternational,
            ContexteFaits    = contexteFaits,
            Etendue          = etendue,
            Abbreviations    = abbreviations,
            SommairExecutif  = sommaire,
            Analyses         = analyses,
            Documents        = documents,
            Method           = method,
            SourcesCount     = sourcesCount,
            ElapsedMs        = elapsedMs,
            GeneratedAt      = DateTime.UtcNow,
        };
        c._events.Add(new ConsultationGeneratedEvent(c.Id, c.ClientName, c.Reference));
        return c;
    }

    /// <summary>Rate this consultation (1–5 stars). Domain rule: rating must be valid.</summary>
    public void Rate(int stars, string? comment = null)
    {
        if (stars < 1 || stars > 5) throw new InvalidRatingException(stars);
        Rating        = stars;
        RatingComment = comment;
        _events.Add(new ConsultationRatedEvent(Id, stars));
    }

    /// <summary>Begin a refinement conversation session.</summary>
    public void BeginSession(string sessionId)
    {
        ActiveSessionId = sessionId;
        _events.Add(new ConsultationRefinedEvent(Id, "session_started"));
    }

    /// <summary>Apply a refinement to a specific section.</summary>
    public void RefineSection(string sectionName, string newContent)
    {
        switch (sectionName.ToLowerInvariant())
        {
            case "analyses":        Analyses        = newContent; break;
            case "sommaire":        SommairExecutif = newContent; break;
            case "etendue":         Etendue         = newContent; break;
            case "contextefaits":   ContexteFaits   = newContent; break;
            case "documents":       Documents       = newContent; break;
            default: throw new ArgumentException($"Unknown section: {sectionName}");
        }
        RefineCount++;
        _events.Add(new ConsultationRefinedEvent(Id, sectionName));
    }

    /// <summary>End and archive the session.</summary>
    public void EndSession()
    {
        if (ActiveSessionId is null) return;
        _events.Add(new ConsultationSessionArchivedEvent(ActiveSessionId, Id));
        ActiveSessionId = null;
    }
}
