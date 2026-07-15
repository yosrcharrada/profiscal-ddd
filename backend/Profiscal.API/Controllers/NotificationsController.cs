using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiscal.Contracts.Common;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Controllers;

/// <summary>
/// In-app notifications for the current user. The navbar bell polls
/// <c>GET /api/notifications</c> for the list + unread count and rings when the
/// count grows; the other endpoints mark items read.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    /// <summary>My notifications (newest first) plus the unread count.</summary>
    [HttpGet]
    public async Task<IActionResult> Mine([FromQuery] int take = 30, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var me = CurrentUserId;

        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == me)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var unread = await db.Notifications
            .CountAsync(n => n.RecipientId == me && !n.IsRead, ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            unread,
            items = rows.Select(n => new
            {
                id = n.Id, type = n.Type, title = n.Title, body = n.Body,
                linkUrl = n.LinkUrl, isRead = n.IsRead, createdAt = n.CreatedAt
            })
        }));
    }

    /// <summary>Mark one notification read.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var me = CurrentUserId;
        await db.Notifications
            .Where(n => n.Id == id && n.RecipientId == me)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.IsRead, true), ct);
        return Ok(ApiResponse<object>.Ok(new { ok = true }));
    }

    /// <summary>Mark every unread notification read.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var me = CurrentUserId;
        var n = await db.Notifications
            .Where(x => x.RecipientId == me && !x.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsRead, true), ct);
        return Ok(ApiResponse<object>.Ok(new { updated = n }));
    }
}
