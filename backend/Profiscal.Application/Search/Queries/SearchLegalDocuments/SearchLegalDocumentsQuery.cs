using Profiscal.Domain.Abstractions.Agents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Profiscal.Application.Search.Queries.SearchLegalDocuments;

public sealed record SearchLegalDocumentsQuery(SearchRequestDto Request) : IRequest<SearchResultDto>;

public sealed class SearchLegalDocumentsQueryHandler(
    ISearchAgent searchAgent,
    ILogger<SearchLegalDocumentsQueryHandler> logger)
    : IRequestHandler<SearchLegalDocumentsQuery, SearchResultDto>
{
    public async Task<SearchResultDto> Handle(SearchLegalDocumentsQuery query, CancellationToken ct)
    {
        logger.LogInformation("Search: '{Q}'", query.Request.Query);
        return await searchAgent.SearchAsync(query.Request, ct);
    }
}
