using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Common;
using Profiscal.Contracts.Requests;
using Profiscal.Contracts.Responses;

namespace Profiscal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Register a new user account.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Login and receive an access token + rotating refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Exchange a refresh token for a new token pair (rotation + reuse detection).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshTokenAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Logout: revoke the given refresh token, or all sessions when Everywhere=true.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(CurrentUserId, request, ct);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    /// <summary>Change password. Revokes all existing sessions and returns a fresh token pair.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ChangePasswordAsync(CurrentUserId, request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>The current authenticated user's profile and roles.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), 200)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await authService.GetMeAsync(CurrentUserId, ct);
        return Ok(ApiResponse<UserResponse>.Ok(result));
    }

    /// <summary>Active sessions (devices) for the current user. Pass the refresh token in X-Refresh-Token to flag the current one.</summary>
    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SessionResponse>>), 200)]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var currentToken = Request.Headers["X-Refresh-Token"].FirstOrDefault();
        var result = await authService.GetSessionsAsync(CurrentUserId, currentToken, ct);
        return Ok(ApiResponse<IReadOnlyList<SessionResponse>>.Ok(result));
    }

    /// <summary>Revoke one of the current user's sessions.</summary>
    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await authService.RevokeSessionAsync(CurrentUserId, id, ct);
        return Ok(ApiResponse<object>.Ok(null!));
    }

    private Guid CurrentUserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
