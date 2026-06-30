using FiscalPlatform.Application.Common.Interfaces.Agents;
using MediatR;

namespace FiscalPlatform.Application.KnowledgeBase.Queries.GetStats;

public sealed record GetKnowledgeBaseStatsQuery() : IRequest<KnowledgeBaseStatsDto>;

public sealed class GetKnowledgeBaseStatsQueryHandler(IRetrievalAgent retrievalAgent)
    : IRequestHandler<GetKnowledgeBaseStatsQuery, KnowledgeBaseStatsDto>
{
    public async Task<KnowledgeBaseStatsDto> Handle(GetKnowledgeBaseStatsQuery query, CancellationToken ct) =>
        await retrievalAgent.GetStatsAsync();
}
