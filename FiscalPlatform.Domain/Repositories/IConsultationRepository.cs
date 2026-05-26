using FiscalPlatform.Domain.Aggregates.Consultation;

namespace FiscalPlatform.Domain.Repositories;

/// <summary>Repository interface — defined in Domain, implemented in Infrastructure.</summary>
public interface IConsultationRepository
{
    Task SaveAsync(Consultation consultation, CancellationToken ct = default);
    Task<IReadOnlyList<ConsultationSummary>> SearchByClientAsync(string clientName, CancellationToken ct = default);
    Task<Consultation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateRatingAsync(Guid id, int stars, string? comment, CancellationToken ct = default);
}

public sealed record ConsultationSummary(
    Guid   Id, string ClientName, string Reference, string Date,
    string Sommaire, string Analyses, string Method,
    int    SourcesCount, double ElapsedMin, int? Rating, string? RatingComment,
    string[] Branches, string[] Countries, bool IsInternational);
