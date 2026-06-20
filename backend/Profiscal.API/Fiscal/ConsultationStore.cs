using System.Text.Json;
using FiscalPlatform.Application.Common.DTOs;
using Microsoft.EntityFrameworkCore;
using Profiscal.Domain.Entities;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Fiscal;

/// <summary>
/// Owns the rich persistence of consultations (sections + full ConsultationOutput JSON
/// + owning user). This is the authoritative writer; the MediatR repository's SaveAsync
/// is a no-op to avoid double writes. Lets a consultation be listed, reopened, edited,
/// re-rated, and exported.
/// </summary>
public sealed class ConsultationStore(AppDbContext db)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<FiscalConsultation> SaveAsync(
        Guid id, string reference, string clientName, string situation, string fiscalQuestion,
        ConsultationOutput output, Guid? ownerUserId, CancellationToken ct = default)
    {
        var row = await db.FiscalConsultations.FirstOrDefaultAsync(c => c.Id == id, ct);
        var isNew = row is null;
        row ??= new FiscalConsultation { Id = id, CreatedAt = DateTime.UtcNow };

        row.Reference       = reference;
        row.ClientName      = clientName;
        row.Situation       = situation;
        row.FiscalQuestion  = fiscalQuestion;
        row.ContexteFaits   = output.ContexteFaits;
        row.Etendue         = output.Etendue;
        row.Abbreviations   = output.Abbreviations;
        row.SommairExecutif = output.SommairExecutif;
        row.Analyses        = output.Analyses;
        row.Documents       = output.Documents;
        row.OutputJson      = JsonSerializer.Serialize(output, Json);
        row.Method          = output.Method;
        row.SourcesCount    = output.Sources.Count;
        row.ElapsedMs       = output.ElapsedMs;
        row.OwnerUserId     = ownerUserId;
        if (!isNew) row.UpdatedAt = DateTime.UtcNow;

        if (isNew) db.FiscalConsultations.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Persist an edited output for an existing consultation (after refinement).</summary>
    public async Task UpdateOutputAsync(Guid id, ConsultationOutput output, CancellationToken ct = default)
    {
        var row = await db.FiscalConsultations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return;
        row.ContexteFaits   = output.ContexteFaits;
        row.Etendue         = output.Etendue;
        row.Abbreviations   = output.Abbreviations;
        row.SommairExecutif = output.SommairExecutif;
        row.Analyses        = output.Analyses;
        row.Documents       = output.Documents;
        row.OutputJson      = JsonSerializer.Serialize(output, Json);
        row.RefineCount    += 1;
        row.UpdatedAt       = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<FiscalConsultation>> ListAsync(Guid? ownerUserId, string? search, CancellationToken ct = default)
    {
        var q = db.FiscalConsultations.AsNoTracking().AsQueryable();
        if (ownerUserId is not null) q = q.Where(c => c.OwnerUserId == ownerUserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.ClientName.ToLower().Contains(s) ||
                             c.Reference.ToLower().Contains(s) ||
                             c.FiscalQuestion.ToLower().Contains(s));
        }
        return await q.OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync(ct);
    }

    public Task<FiscalConsultation?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.FiscalConsultations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public ConsultationOutput? DeserializeOutput(FiscalConsultation row)
    {
        try { return JsonSerializer.Deserialize<ConsultationOutput>(row.OutputJson, Json); }
        catch { return null; }
    }
}
