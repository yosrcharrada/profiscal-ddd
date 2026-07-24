using Profiscal.Domain.Abstractions.Agents;
using MediatR;

namespace Profiscal.Application.KnowledgeBase.Queries.GetStats;

public sealed record GetKnowledgeBaseStatsQuery() : IRequest<KnowledgeBaseStatsDto>;

public sealed class GetKnowledgeBaseStatsQueryHandler(IRetrievalAgent retrievalAgent)
    : IRequestHandler<GetKnowledgeBaseStatsQuery, KnowledgeBaseStatsDto>
{
    public async Task<KnowledgeBaseStatsDto> Handle(GetKnowledgeBaseStatsQuery query, CancellationToken ct) =>
        await retrievalAgent.GetStatsAsync();
}
