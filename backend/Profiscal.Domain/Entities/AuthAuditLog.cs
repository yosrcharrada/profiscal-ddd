using Profiscal.Domain.Enums;

namespace Profiscal.Domain.Entities;

/// <summary>Append-only trail of authentication-related events for user tracking.</summary>
public class AuthAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AppUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public AuthEvent Event { get; set; }
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
