namespace Profiscal.Domain.Abstractions;

/// <summary>Outbound email (SMTP). Used to deliver generated credentials to provisioned users.</summary>
public interface IEmailService
{
    /// <summary>False when no SMTP host is configured — callers should fall back gracefully.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends the "your account is ready" email with the generated password. Returns true on success.</summary>
    Task<bool> SendCredentialsAsync(
        string toEmail, string fullName, string role, string temporaryPassword,
        CancellationToken ct = default);
}
