using FiscalPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Application.Consultation.Queries.GetConsultationHistory;

public sealed record GetConsultationHistoryQuery(string ClientName)
    : IRequest<IReadOnlyList<ConsultationSummary>>;

public sealed class GetConsultationHistoryQueryHandler(
    IConsultationRepository repository,
    ILogger<GetConsultationHistoryQueryHandler> logger)
    : IRequestHandler<GetConsultationHistoryQuery, IReadOnlyList<ConsultationSummary>>
{
    public async Task<IReadOnlyList<ConsultationSummary>> Handle(
        GetConsultationHistoryQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.ClientName)) return Array.Empty<ConsultationSummary>();
        logger.LogInformation("History query: {C}", query.ClientName);
        return await repository.SearchByClientAsync(query.ClientName, ct);
    }
}
