using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Common;
using Profiscal.Contracts.Requests;
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
    IRequestContext requestContext,
    IEmailService emailService,
    IConfiguration config) : IUserAdminService
{
    /// <summary>Roles an admin can hand out. "User" is legacy-only and no longer assignable.</summary>
    private static readonly string[] AssignableRoles = ["Admin", "Manager", "Consultant"];

    private string AllowedDomain => config["Registration:AllowedDomain"] ?? "tn.ey.com";

    public async Task<PagedResponse<UserResponse>> GetUsersAsync(
        PaginationRequest pagination, string? search, string? role, CancellationToken ct = default)
    {
        var query = userManager.Users.AsNoTracking().Include(u => u.Manager).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email!.ToLower().Contains(s) ||
                u.FirstName.ToLower().Contains(s) ||
                u.LastName.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleIds = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).ToListAsync(ct);
            var userIds = db.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId);
            query = query.Where(u => userIds.Contains(u.Id));
        }

        var total    = await query.CountAsync(ct);
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

    public async Task<CreateUserResponse> CreateUserAsync(Guid actorId, CreateUserRequest req, CancellationToken ct = default)
    {
        var email = req.Email.Trim();

        if (!email.EndsWith($"@{AllowedDomain}", StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Access is restricted to @{AllowedDomain} email addresses.");

        if (!AssignableRoles.Contains(req.Role))
            throw new DomainException($"Role must be one of: {string.Join(", ", AssignableRoles)}.");

        if (await userManager.FindByEmailAsync(email) is not null)
            throw new DomainException($"Email '{email}' is already registered.");

        // Only consultants report to a manager. Admins and managers never have one.
        AppUser? manager = null;
        if (req.Role == "Consultant")
        {
            if (req.ManagerId is null)
                throw new DomainException("Every consultant must be attached to a manager.");
            manager = await ValidateManagerAsync(req.ManagerId.Value);
        }

        var password = GeneratePassword();
        var user = new AppUser
        {
            FirstName = req.FirstName.Trim(),
            LastName  = req.LastName.Trim(),
            Email     = email,
            UserName  = email,
            EmailConfirmed     = true,
            ManagerId          = manager?.Id,
            MustChangePassword = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new DomainException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, req.Role);
        await AuditAsync(user.Id, email, AuthEvent.UserProvisioned,
            $"Role {req.Role}{(manager is null ? "" : $", manager {manager.Email}")} (by {actorId})", ct);

        var emailSent = await emailService.SendCredentialsAsync(
            email, $"{user.FirstName} {user.LastName}".Trim(), req.Role, password, ct);

        user.Manager = manager;
        return new CreateUserResponse(
            ToUserResponse(user, [req.Role]),
            emailSent,
            emailSent ? null : password);
    }

    public async Task<UserResponse> AssignManagerAsync(Guid actorId, Guid userId, Guid? managerId, CancellationToken ct = default)
    {
        var user = await userManager.Users.Include(u => u.Manager)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (managerId == userId)
            throw new DomainException("A user cannot be their own manager.");

        // Only consultants can be attached to a manager.
        if (managerId is not null)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (!roles.Contains("Consultant"))
                throw new DomainException("Only consultants can be attached to a manager.");
        }

        var manager = managerId is null ? null : await ValidateManagerAsync(managerId.Value);

        user.ManagerId = manager?.Id;
        user.Manager   = manager;
        await userManager.UpdateAsync(user);

        await AuditAsync(userId, user.Email!, AuthEvent.ManagerAssigned,
            manager is null ? $"Manager detached (by {actorId})" : $"Manager {manager.Email} (by {actorId})", ct);

        return ToUserResponse(user, await userManager.GetRolesAsync(user));
    }

    public async Task<IReadOnlyList<ManagerResponse>> GetManagersAsync(CancellationToken ct = default)
    {
        var managers = await userManager.GetUsersInRoleAsync("Manager");
        var ids = managers.Select(m => m.Id).ToList();

        var counts = await db.Users.AsNoTracking()
            .Where(u => u.ManagerId != null && ids.Contains(u.ManagerId.Value))
            .GroupBy(u => u.ManagerId!.Value)
            .Select(g => new { ManagerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ManagerId, g => g.Count, ct);

        return managers
            .OrderBy(m => m.FirstName).ThenBy(m => m.LastName)
            .Select(m => new ManagerResponse(m.Id, m.FirstName, m.LastName, m.Email!,
                counts.GetValueOrDefault(m.Id)))
            .ToList();
    }

    public async Task<IReadOnlyList<GlobalAuditResponse>> GetGlobalActivityAsync(int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await db.AuthAuditLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .Select(l => new
            {
                l.Event, l.Detail, l.Email, l.IpAddress, l.CreatedAt,
                User = db.Users.Where(u => u.Id == l.AppUserId)
                    .Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .Select(l => new GlobalAuditResponse(l.Event.ToString(), l.Detail, l.Email, l.User, l.IpAddress, l.CreatedAt))
            .ToList();
    }

    public async Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var now        = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var weekStart  = now.AddDays(-7);

        var roleCounts = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            group ur by r.Name into g
            select new { Role = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Role!, g => g.Count, ct);

        var totalUsers = await db.Users.CountAsync(ct);
        // SQLite can't translate DateTimeOffset comparisons — count in memory (small table).
        var locked = (await db.Users.Select(u => u.LockoutEnd).ToListAsync(ct))
            .Count(l => l.HasValue && l > DateTimeOffset.UtcNow);
        var newUsers   = await db.Users.CountAsync(u => u.CreatedAt >= monthStart, ct);

        var pendingRecl  = await db.Reclamations.CountAsync(r => r.Status == ReclamationStatus.Pending, ct);
        var resolvedRecl = await db.Reclamations.CountAsync(r => r.Status == ReclamationStatus.Resolved, ct);

        var totalCons    = await db.FiscalConsultations.CountAsync(ct);
        var consThisWeek = await db.FiscalConsultations.CountAsync(c => c.CreatedAt >= weekStart, ct);

        var taskCounts = await db.WorkTasks
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, ct);

        var recentUsers = new List<UserResponse>();
        foreach (var u in await userManager.Users.AsNoTracking().Include(u => u.Manager)
                     .OrderByDescending(u => u.CreatedAt).Take(6).ToListAsync(ct))
            recentUsers.Add(ToUserResponse(u, await userManager.GetRolesAsync(u)));

        var recentActivity = await GetGlobalActivityAsync(12, ct);

        return new AdminOverviewResponse(
            totalUsers,
            roleCounts.GetValueOrDefault("Admin"),
            roleCounts.GetValueOrDefault("Manager"),
            roleCounts.GetValueOrDefault("Consultant") + roleCounts.GetValueOrDefault("User"),
            locked,
            newUsers,
            pendingRecl,
            resolvedRecl,
            totalCons,
            consThisWeek,
            taskCounts.GetValueOrDefault(WorkTaskStatus.Pending),
            taskCounts.GetValueOrDefault(WorkTaskStatus.InProgress),
            taskCounts.GetValueOrDefault(WorkTaskStatus.Submitted),
            taskCounts.GetValueOrDefault(WorkTaskStatus.Approved),
            recentUsers,
            recentActivity);
    }

    public async Task<UserResponse> UpdateRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default)
    {
        if (!await roleManager.RoleExistsAsync(role))
            throw new DomainException($"Role '{role}' does not exist.");

        var user = await userManager.Users.Include(u => u.Manager)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (actorId == userId && role != "Admin")
            throw new DomainException("You cannot remove your own admin role.");

        var current = await userManager.GetRolesAsync(user);
        if (current.Count > 0)
            await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, role);

        // Only consultants report to a manager — promoting to Manager/Admin detaches it.
        if (role != "Consultant" && user.ManagerId is not null)
        {
            user.ManagerId = null;
            user.Manager   = null;
            await userManager.UpdateAsync(user);
        }

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

        var user = await userManager.Users.Include(u => u.Manager)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
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

    private async Task<AppUser> ValidateManagerAsync(Guid managerId)
    {
        var manager = await userManager.FindByIdAsync(managerId.ToString())
            ?? throw new DomainException("The selected manager does not exist.");
        var roles = await userManager.GetRolesAsync(manager);
        if (!roles.Contains("Manager"))
            throw new DomainException($"{manager.Email} is not a manager.");
        return manager;
    }

    /// <summary>14-char password satisfying the Identity policy (upper, lower, digit, symbol).</summary>
    private static string GeneratePassword()
    {
        const string upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower   = "abcdefghijkmnpqrstuvwxyz";
        const string digits  = "23456789";
        const string symbols = "!@#$%*-+?";
        const string all     = upper + lower + digits + symbols;

        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        while (chars.Count < 14)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Fisher–Yates with a crypto RNG so the mandatory classes aren't positional.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
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
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            user.ManagerId,
            user.Manager is null ? null : $"{user.Manager.FirstName} {user.Manager.LastName}".Trim(),
            user.MustChangePassword);
}
