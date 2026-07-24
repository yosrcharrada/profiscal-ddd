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
public class JortController(AppDbContext db, IJortSync jortSync, IJortScraper scraper) : ControllerBase
{
    // Fresh IORT links expire with their server-side session — cache one per target
    // for a few minutes so a burst of clicks doesn't hammer iort.gov.tn.
    private static readonly Dictionary<string, (string Url, DateTime At)> LinkCache = [];
    private static readonly TimeSpan LinkTtl = TimeSpan.FromMinutes(5);

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

    /// <summary>
    /// Mint a fresh iort.gov.tn deep link in FRENCH (the site's URLs are per-session,
    /// so we open a new session and return its URL). target=latest → Journal officiel
    /// (PageDernierParu), target=home → French Principal page.
    /// </summary>
    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] string target = "latest", CancellationToken ct = default)
    {
        target = target.Equals("home", StringComparison.OrdinalIgnoreCase) ? "home" : "latest";

        lock (LinkCache)
        {
            if (LinkCache.TryGetValue(target, out var hit) && DateTime.UtcNow - hit.At < LinkTtl)
                return Ok(ApiResponse<object>.Ok(new { url = hit.Url }));
        }

        var url = await scraper.GetLiveLinkAsync(target, ct);
        if (url is null)
            return Ok(ApiResponse<object>.Ok(new { url = "http://www.iort.gov.tn" })); // graceful fallback

        lock (LinkCache) LinkCache[target] = (url, DateTime.UtcNow);
        return Ok(ApiResponse<object>.Ok(new { url }));
    }

    // Per-text PDFs are heavy to fetch (full session replay) — keep the last few in memory.
    private static readonly Dictionary<Guid, (byte[] Bytes, DateTime At)> PdfCache = [];
    private static readonly TimeSpan PdfTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Stream the official PDF of one scraped JORT text (located on the live site by
    /// its French title). 404 when the text left the "dernier paru" page.
    /// </summary>
    [HttpGet("activities/{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct = default)
    {
        var activity = await db.JortActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (activity is null) return NotFound(ApiResponse<object>.Fail("Unknown JORT activity."));

        lock (PdfCache)
        {
            if (PdfCache.TryGetValue(id, out var hit) && DateTime.UtcNow - hit.At < PdfTtl)
                return File(hit.Bytes, "application/pdf");
        }

        var bytes = await scraper.GetPdfAsync(activity.Title, ct);
        if (bytes is null)
            return NotFound(ApiResponse<object>.Fail("This text is no longer on the latest JORT page."));

        lock (PdfCache)
        {
            if (PdfCache.Count > 40) PdfCache.Clear(); // crude cap — a JORT issue has a few dozen texts
            PdfCache[id] = (bytes, DateTime.UtcNow);
        }
        return File(bytes, "application/pdf");
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
