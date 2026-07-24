namespace Profiscal.Domain.Contracts.Responses;

/// <summary>An active refresh-token session (one per device/browser).</summary>
public record SessionResponse(
    Guid Id,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? CreatedByIp,
    string? UserAgent,
    bool IsCurrent
);
