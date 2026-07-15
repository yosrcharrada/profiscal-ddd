using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiscal.API.Fiscal;
using Profiscal.Contracts.Common;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Controllers;

/// <summary>
/// Live JORT feed scraped from iort.gov.tn. Any signed-in user can read the
/// latest notices (they power the dashboard feed); only an admin can force a
/// refresh, which re-scrapes the source and notifies admins of anything new.
/// </summary>
[ApiController]
[Route("api/jort")]
[Authorize]
[Produces("application/json")]
public class JortController(AppDbContext db, IJortSync jortSync) : ControllerBase
{
    /// <summary>Latest scraped notices, newest publication first.</summary>
    [HttpGet("activities")]
    public async Task<IActionResult> Activities([FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.JortActivities.AsNoTracking()
            .OrderByDescending(a => a.PublishedOn ?? a.FirstSeenAt)
            .ThenByDescending(a => a.FirstSeenAt)
            .Take(take)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow.AddDays(-14);
        return Ok(ApiResponse<object>.Ok(rows.Select(a => new
        {
            id          = a.Id,
            title       = a.Title,
            description = a.Description,
            category    = a.Category,
            date        = a.PublishedOn,
            dateText    = a.PublishedText,
            isNew       = a.FirstSeenAt >= cutoff
        })));
    }

    /// <summary>Admin: re-scrape the source now and report how many new notices arrived.</summary>
    [HttpPost("refresh")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var added = await jortSync.RunOnceAsync(forceNotify: true, ct);
        return Ok(ApiResponse<object>.Ok(new { added }));
    }
}
