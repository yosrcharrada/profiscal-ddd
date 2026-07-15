using Microsoft.EntityFrameworkCore;
using Profiscal.Domain.Entities;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Fiscal;

public interface IJortSync
{
    /// <summary>Scrape once, persist new activities and (optionally) notify admins.
    /// Returns the number of brand-new activities inserted.</summary>
    Task<int> RunOnceAsync(bool forceNotify, CancellationToken ct);
}

/// <summary>
/// Background worker that keeps the JORT feed fresh. Runs shortly after startup
/// and then every 30 minutes. New notices are stored in the DB and — once past
/// the very first backfill — every admin gets a notification so the navbar bell
/// rings. Also exposed via <see cref="IJortSync"/> so an admin can trigger a
/// manual refresh on demand (which always notifies).
/// </summary>
public class JortSyncService(
    IServiceScopeFactory scopeFactory,
    IJortScraper scraper,
    ILogger<JortSyncService> logger) : BackgroundService, IJortSync
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let migrations/seed finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(forceNotify: false, stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "JORT background sync failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(bool forceNotify, CancellationToken ct)
    {
        var scraped = await scraper.ScrapeAsync(ct);
        if (scraped.Count == 0) return 0;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var known = (await db.JortActivities.Select(a => a.Hash).ToListAsync(ct)).ToHashSet();
        var isFirstEverRun = known.Count == 0;

        var now = DateTime.UtcNow;
        var fresh = new List<JortActivity>();
        foreach (var s in scraped)
        {
            if (known.Contains(s.Hash))
            {
                // Touch ScrapedAt so we can tell live items from disappeared ones.
                await db.JortActivities.Where(a => a.Hash == s.Hash)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.ScrapedAt, now), ct);
                continue;
            }

            var entity = new JortActivity
            {
                Title         = s.Title,
                Description   = s.Description,
                Category      = s.Category,
                PublishedOn   = s.PublishedOn,
                PublishedText = s.DateText,
                Hash          = s.Hash,
                FirstSeenAt   = now,
                ScrapedAt     = now
            };
            db.JortActivities.Add(entity);
            fresh.Add(entity);
        }

        if (fresh.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return 0;
        }

        // Notify admins — but skip the initial backfill flood unless forced (manual refresh).
        if (forceNotify || !isFirstEverRun)
        {
            var adminIds = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where r.Name == "Admin"
                select ur.UserId).Distinct().ToListAsync(ct);

            // Newest first so the most recent notice is the freshest notification.
            var ordered = fresh.OrderBy(f => f.PublishedOn ?? DateTime.MinValue).ToList();
            foreach (var adminId in adminIds)
                foreach (var f in ordered)
                    db.Notifications.Add(new Notification
                    {
                        RecipientId = adminId,
                        Type        = "jort",
                        Title       = f.Category switch
                        {
                            "Tender"      => "Nouvel appel d'offres (JORT)",
                            "Plan"        => "Nouveau plan (JORT)",
                            "Publication" => "Nouvelle publication (JORT)",
                            _             => "Nouveau document (JORT)"
                        },
                        Body    = f.Title,
                        LinkUrl = "/dashboard"
                    });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("JORT sync: {New} new activities ({Notify})",
            fresh.Count, (forceNotify || !isFirstEverRun) ? "admins notified" : "backfill, silent");
        return fresh.Count;
    }
}
