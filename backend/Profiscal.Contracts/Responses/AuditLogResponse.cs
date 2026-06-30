namespace Profiscal.Contracts.Responses;

public record AuditLogResponse(
    string Event,
    string? Detail,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAt
);
