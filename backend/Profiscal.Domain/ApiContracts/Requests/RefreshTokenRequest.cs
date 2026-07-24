namespace Profiscal.Domain.Contracts.Requests;

public record RefreshTokenRequest(string AccessToken, string RefreshToken);
