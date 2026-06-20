namespace Profiscal.Contracts.Requests;

public record RefreshTokenRequest(string AccessToken, string RefreshToken);
