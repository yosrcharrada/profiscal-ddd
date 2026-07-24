namespace Profiscal.Domain.Contracts.Responses;

/// <summary>
/// Result of admin provisioning. When SMTP isn't configured the credentials
/// email can't be sent, so the generated password is returned once for the
/// admin to share manually; it is never stored in clear anywhere.
/// </summary>
public record CreateUserResponse(
    UserResponse User,
    bool EmailSent,
    string? TemporaryPassword
);
