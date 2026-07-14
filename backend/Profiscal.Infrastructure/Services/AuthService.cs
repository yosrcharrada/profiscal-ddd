using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Requests;
using Profiscal.Contracts.Responses;
using Profiscal.Domain.Entities;
using Profiscal.Domain.Enums;
using Profiscal.Domain.Exceptions;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.Infrastructure.Services;

public class AuthService(
    UserManager<AppUser> userManager,
    AppDbContext db,
    IJwtTokenService jwtService,
    IRequestContext requestContext,
    IConfiguration config) : IAuthService
{
    private readonly int _refreshDays = int.TryParse(config["Jwt:RefreshTokenDays"], out var d) ? d : 7;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        // Self-service registration is reserved for the firm's domain; everyone
        // else is provisioned by an admin.
        var allowedDomain = config["Registration:AllowedDomain"] ?? "tn.ey.com";
        var email = req.Email.Trim();
        if (!email.EndsWith($"@{allowedDomain}", StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Registration is restricted to @{allowedDomain} email addresses. Contact your administrator for access.");

        if (await userManager.FindByEmailAsync(email) is not null)
            throw new DomainException($"Email '{email}' is already registered.");

        var user = new AppUser
        {
            FirstName = req.FirstName,
            LastName  = req.LastName,
            Email     = email,
            UserName  = email,
            LastLoginAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            throw new DomainException(string.Join(", ", result.Errors.Select(e => e.Description)));

        // Self-registered accounts start as consultants; an admin attaches the manager.
        await userManager.AddToRoleAsync(user, "Consultant");
        await AuditAsync(user.Id, user.Email!, AuthEvent.Register, ct: ct);

        var (response, _) = await IssueTokensAsync(user, ct);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null)
        {
            await AuditAsync(null, req.Email, AuthEvent.LoginFailed, "Unknown email", ct);
            throw new DomainException("Invalid email or password.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await AuditAsync(user.Id, user.Email!, AuthEvent.AccountLockedOut, "Login attempt while locked", ct);
            throw new DomainException("This account is locked. Try again later or contact an administrator.");
        }

        if (!await userManager.CheckPasswordAsync(user, req.Password))
        {
            await userManager.AccessFailedAsync(user); // counts toward lockout
            await AuditAsync(user.Id, user.Email!, AuthEvent.LoginFailed, "Wrong password", ct);

            if (await userManager.IsLockedOutAsync(user))
            {
                await AuditAsync(user.Id, user.Email!, AuthEvent.AccountLockedOut, "Too many failed attempts", ct);
                throw new DomainException("Too many failed attempts. This account is temporarily locked.");
            }
            throw new DomainException("Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await AuditAsync(user.Id, user.Email!, AuthEvent.LoginSucceeded, ct: ct);

        var (response, _) = await IssueTokensAsync(user, ct);
        return response;
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest req, CancellationToken ct = default)
    {
        var principal = jwtService.ValidateExpiredToken(req.AccessToken)
            ?? throw new DomainException("Invalid access token.");

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")!);

        var hash = Sha256(req.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(r => r.AppUser)
            .FirstOrDefaultAsync(r => r.TokenHash == hash && r.AppUserId == userId, ct)
            ?? throw new DomainException("Invalid refresh token.");

        if (stored.IsRevoked)
        {
            // A rotated/revoked token was presented again — assume theft, kill every session.
            var active = await db.RefreshTokens
                .Where(r => r.AppUserId == userId && r.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in active)
            {
                t.RevokedAt     = DateTime.UtcNow;
                t.RevokedByIp   = requestContext.IpAddress;
                t.RevokedReason = "Token reuse detected";
            }
            await db.SaveChangesAsync(ct);
            await AuditAsync(userId, stored.AppUser.Email!, AuthEvent.TokenReuseDetected,
                "Revoked all sessions", ct);
            throw new DomainException("Session invalidated for security reasons. Please sign in again.");
        }

        if (stored.IsExpired)
            throw new DomainException("Refresh token has expired. Please sign in again.");

        var (response, newHash) = await IssueTokensAsync(stored.AppUser, ct);

        stored.RevokedAt           = DateTime.UtcNow;
        stored.RevokedByIp         = requestContext.IpAddress;
        stored.RevokedReason       = "Rotated";
        stored.ReplacedByTokenHash = newHash;
        await db.SaveChangesAsync(ct);

        await AuditAsync(userId, stored.AppUser.Email!, AuthEvent.TokenRefreshed, ct: ct);
        return response;
    }

    public async Task LogoutAsync(Guid userId, LogoutRequest req, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (req.Everywhere)
        {
            await RevokeAllActiveAsync(userId, "Logout everywhere", ct);
            await AuditAsync(userId, user.Email!, AuthEvent.LogoutEverywhere, ct: ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            var hash = Sha256(req.RefreshToken);
            var token = await db.RefreshTokens
                .FirstOrDefaultAsync(r => r.TokenHash == hash && r.AppUserId == userId, ct);
            if (token is { IsActive: true })
            {
                token.RevokedAt     = DateTime.UtcNow;
                token.RevokedByIp   = requestContext.IpAddress;
                token.RevokedReason = "Logout";
                await db.SaveChangesAsync(ct);
            }
        }
        await AuditAsync(userId, user.Email!, AuthEvent.Logout, ct: ct);
    }

    public async Task<AuthResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest req, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(AppUser), userId);

        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            throw new DomainException(string.Join(", ", result.Errors.Select(e => e.Description)));

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        // Standard practice: a password change invalidates every existing session,
        // then the caller gets a fresh token pair so they stay signed in.
        await RevokeAllActiveAsync(userId, "Password changed", ct);
        await AuditAsync(userId, user.Email!, AuthEvent.PasswordChanged, ct: ct);

        var (response, _) = await IssueTokensAsync(user, ct);
        return response;
    }

    public async Task<UserResponse> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.Users.AsNoTracking()
            .Include(u => u.Manager)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);
        var roles = await userManager.GetRolesAsync(user);
        return ToUserResponse(user, roles);
    }

    public async Task<IReadOnlyList<SessionResponse>> GetSessionsAsync(
        Guid userId, string? currentRefreshToken, CancellationToken ct = default)
    {
        var currentHash = string.IsNullOrWhiteSpace(currentRefreshToken) ? null : Sha256(currentRefreshToken);
        var now = DateTime.UtcNow;

        var sessions = await db.RefreshTokens
            .Where(r => r.AppUserId == userId && r.RevokedAt == null && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return sessions
            .Select(r => new SessionResponse(r.Id, r.CreatedAt, r.ExpiresAt, r.CreatedByIp, r.UserAgent,
                r.TokenHash == currentHash))
            .ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Id == sessionId && r.AppUserId == userId, ct)
            ?? throw new NotFoundException(nameof(RefreshToken), sessionId);

        if (!token.IsActive)
            throw new DomainException("Session is already inactive.");

        token.RevokedAt     = DateTime.UtcNow;
        token.RevokedByIp   = requestContext.IpAddress;
        token.RevokedReason = "Revoked by user";
        await db.SaveChangesAsync(ct);

        var user = await userManager.FindByIdAsync(userId.ToString());
        await AuditAsync(userId, user?.Email ?? string.Empty, AuthEvent.SessionRevoked, $"Session {sessionId}", ct);
    }

    /* ───────────────────────── helpers ───────────────────────── */

    private async Task<(AuthResponse Response, string NewTokenHash)> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = jwtService.GenerateAccessToken(user, roles);

        var rawRefresh = GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            TokenHash   = Sha256(rawRefresh),
            ExpiresAt   = DateTime.UtcNow.AddDays(_refreshDays),
            AppUserId   = user.Id,
            CreatedByIp = requestContext.IpAddress,
            UserAgent   = Truncate(requestContext.UserAgent, 512)
        };

        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);

        var response = new AuthResponse(accessToken, rawRefresh, expiresAt, ToUserResponse(user, roles));
        return (response, refresh.TokenHash);
    }

    private async Task RevokeAllActiveAsync(Guid userId, string reason, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(r => r.AppUserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt     = DateTime.UtcNow;
            t.RevokedByIp   = requestContext.IpAddress;
            t.RevokedReason = reason;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task AuditAsync(Guid? userId, string email, AuthEvent evt, string? detail = null, CancellationToken ct = default)
    {
        db.AuthAuditLogs.Add(new AuthAuditLog
        {
            AppUserId = userId,
            Email     = email,
            Event     = evt,
            Detail    = detail,
            IpAddress = requestContext.IpAddress,
            UserAgent = Truncate(requestContext.UserAgent, 512)
        });
        await db.SaveChangesAsync(ct);
    }

    private static UserResponse ToUserResponse(AppUser user, IEnumerable<string> roles) =>
        new(user.Id, user.FirstName, user.LastName, user.Email!, roles,
            user.CreatedAt, user.LastLoginAt,
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.ManagerId,
            user.Manager is null ? null : $"{user.Manager.FirstName} {user.Manager.LastName}".Trim(),
            user.MustChangePassword);

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
