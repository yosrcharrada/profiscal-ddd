using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Common;
using Profiscal.Contracts.Responses;
using Profiscal.Domain.Entities;
using Profiscal.Domain.Enums;
using Profiscal.Domain.Exceptions;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.Infrastructure.Services;

public class UserAdminService(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    AppDbContext db,
    IRequestContext requestContext) : IUserAdminService
{
    public async Task<PagedResponse<UserResponse>> GetUsersAsync(
        PaginationRequest pagination, string? search, CancellationToken ct = default)
    {
        var query = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email!.ToLower().Contains(s) ||
                u.FirstName.ToLower().Contains(s) ||
                u.LastName.ToLower().Contains(s));
        }

        var total = await query.CountAsync(ct);
        var page     = Math.Max(1, pagination.Page);
        var pageSize = Math.Clamp(pagination.PageSize, 1, 100);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UserResponse>(users.Count);
        foreach (var user in users)
            items.Add(ToUserResponse(user, await userManager.GetRolesAsync(user)));

        return new PagedResponse<UserResponse>(items, total, page, pageSize);
    }

    public async Task<UserResponse> UpdateRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default)
    {
        if (!await roleManager.RoleExistsAsync(role))
            throw new DomainException($"Role '{role}' does not exist.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (actorId == userId && role != "Admin")
            throw new DomainException("You cannot remove your own admin role.");

        var current = await userManager.GetRolesAsync(user);
        if (current.Count > 0)
            await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, role);

        // Existing access tokens keep old role claims until they expire (≤ token lifetime);
        // revoking refresh tokens forces a clean re-issue with the new role.
        await RevokeAllActiveAsync(userId, "Role changed", ct);
        await AuditAsync(userId, user.Email!, AuthEvent.RoleChanged,
            $"{string.Join(",", current)} → {role} (by {actorId})", ct);

        return ToUserResponse(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserResponse> SetLockAsync(Guid actorId, Guid userId, bool locked, CancellationToken ct = default)
    {
        if (actorId == userId)
            throw new DomainException("You cannot lock or unlock your own account.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (locked)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await RevokeAllActiveAsync(userId, "Account locked by admin", ct);
            await AuditAsync(userId, user.Email!, AuthEvent.AccountLocked, $"By {actorId}", ct);
        }
        else
        {
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
            await AuditAsync(userId, user.Email!, AuthEvent.AccountUnlocked, $"By {actorId}", ct);
        }

        return ToUserResponse(user, await userManager.GetRolesAsync(user));
    }

    public async Task<IReadOnlyList<AuditLogResponse>> GetActivityAsync(Guid userId, int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var logs = await db.AuthAuditLogs.AsNoTracking()
            .Where(l => l.AppUserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return logs
            .Select(l => new AuditLogResponse(l.Event.ToString(), l.Detail, l.IpAddress, l.UserAgent, l.CreatedAt))
            .ToList();
    }

    /* ───────────────────────── helpers ───────────────────────── */

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

    private async Task AuditAsync(Guid? userId, string email, AuthEvent evt, string? detail, CancellationToken ct)
    {
        db.AuthAuditLogs.Add(new AuthAuditLog
        {
            AppUserId = userId,
            Email     = email,
            Event     = evt,
            Detail    = detail,
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent
        });
        await db.SaveChangesAsync(ct);
    }

    private static UserResponse ToUserResponse(AppUser user, IEnumerable<string> roles) =>
        new(user.Id, user.FirstName, user.LastName, user.Email!, roles,
            user.CreatedAt, user.LastLoginAt,
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow);
}
