using Profiscal.Domain.Enums;

namespace Profiscal.Domain.Entities;

/// <summary>
/// A bug report / issue raised by any user. Admins triage these:
/// a reclamation stays Pending until an admin marks it Resolved.
/// </summary>
public class Reclamation
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Subject     { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ReclamationCategory Category { get; set; } = ReclamationCategory.Bug;
    public ReclamationStatus   Status   { get; set; } = ReclamationStatus.Pending;

    public Guid    CreatedById { get; set; }
    public AppUser CreatedBy   { get; set; } = null!;

    public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt   { get; set; }
    public Guid?     ResolvedById { get; set; }
    public string?   AdminNote    { get; set; }
}
