using FiscalPlatform.Domain.Aggregates.Consultation;
using FiscalPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Fiscal;

/// <summary>
/// EF-backed replacement for the original project's Elasticsearch consultation repository.
/// The CONTROLLER is the sole writer of the rich record (sections + OutputJson + owner),
/// so SaveAsync here is a no-op to avoid a double-write race with that path.
/// History, lookup, and rating all read/update the FiscalConsultations table.
/// </summary>
public sealed class EfConsultationRepository(AppDbContext db) : IConsultationRepository
{
    public Task SaveAsync(Consultation consultation, CancellationToken ct = default) =>
        Task.CompletedTask; // controller persists the full record (see ConsultationStore)

    public async Task<IReadOnlyList<ConsultationSummary>> SearchByClientAsync(
        string clientName, CancellationToken ct = default)
    {
        var rows = await db.FiscalConsultations.AsNoTracking()
            .Where(c => c.ClientName.ToLower().Contains(clientName.ToLower()))
            .OrderByDescending(c => c.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        return rows.Select(c => new ConsultationSummary(
            c.Id, c.ClientName, c.Reference, c.CreatedAt.ToString("yyyy-MM-dd"),
            c.SommairExecutif, c.Analyses, c.Method, c.SourcesCount,
            Math.Round(c.ElapsedMs / 60000.0, 2), c.Rating, c.RatingComment,
            string.IsNullOrEmpty(c.Branches) ? Array.Empty<string>() : c.Branches.Split(','),
            string.IsNullOrEmpty(c.Countries) ? Array.Empty<string>() : c.Countries.Split(','),
            c.IsInternational)).ToList();
    }

    public Task<Consultation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<Consultation?>(null); // aggregate rehydration not needed for our flows

    public async Task UpdateRatingAsync(Guid id, int stars, string? comment, CancellationToken ct = default)
    {
        var row = await db.FiscalConsultations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return;
        row.Rating = stars;
        row.RatingComment = comment;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
