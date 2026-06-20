namespace Profiscal.Contracts.Responses;

public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    IEnumerable<string> Roles,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool IsLockedOut
);
