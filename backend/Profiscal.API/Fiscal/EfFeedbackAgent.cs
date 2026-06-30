using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.EntityFrameworkCore;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Fiscal;

/// <summary>EF-backed replacement for the original Elasticsearch FeedbackAgent (ratings).</summary>
public sealed class EfFeedbackAgent(AppDbContext db) : IFeedbackAgent
{
    public async Task SaveRatingAsync(Guid consultationId, string reference, string clientName,
        int stars, string? comment, CancellationToken ct = default)
    {
        var row = await db.FiscalConsultations.FirstOrDefaultAsync(c => c.Id == consultationId, ct);
        if (row is null) return;
        row.Rating = stars;
        row.RatingComment = comment;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<double> GetAverageRatingAsync(string clientName, CancellationToken ct = default)
    {
        var ratings = await db.FiscalConsultations.AsNoTracking()
            .Where(c => c.ClientName == clientName && c.Rating != null)
            .Select(c => (double)c.Rating!.Value)
            .ToListAsync(ct);
        return ratings.Count == 0 ? 0 : ratings.Average();
    }
}
