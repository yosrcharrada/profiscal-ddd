namespace Profiscal.Domain.Contracts.Requests;

/// <summary>RefreshToken revokes the current session; Everywhere revokes all sessions.</summary>
public record LogoutRequest(string? RefreshToken, bool Everywhere = false);
