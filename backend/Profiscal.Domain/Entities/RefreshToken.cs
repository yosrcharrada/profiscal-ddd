namespace Profiscal.Domain.Entities;

/// <summary>
/// One refresh token = one device session. The raw token value is never stored —
/// only its SHA-256 hash. Rotation links tokens into a family via ReplacedByTokenHash
/// so reuse of an already-rotated token can be detected and the family revoked.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    public Guid AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
}
