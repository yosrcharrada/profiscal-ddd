using Profiscal.Contracts.Requests;
using Profiscal.Contracts.Responses;

namespace Profiscal.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken ct = default);
    Task<AuthResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<UserResponse> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken ct = default);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}
