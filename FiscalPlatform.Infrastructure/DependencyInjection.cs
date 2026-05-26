using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Domain.Repositories;
using FiscalPlatform.Infrastructure.Agents;
using FiscalPlatform.Infrastructure.DomainServices;
using FiscalPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// Single DI registration point for the entire Infrastructure layer.
/// API only calls AddInfrastructure() — the Composition Root pattern.
/// All agents, repositories, and domain services registered here.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // ── AI Agents (each has exactly one responsibility) ───────────────────
        services.AddSingleton<ILlmAgent,                LlmAgent>();
        services.AddSingleton<IEmbedSearchAgent,        EmbedSearchAgent>();
        services.AddSingleton<IFeedbackAgent,           FeedbackAgent>();

        // RetrievalAgent and DocumentGenerationAgent registered from API project
        // because they depend on Neo4j.Driver and DocumentFormat.OpenXml
        // which live in Infrastructure — they are added there via partial registrations:

        // ── Domain Services (pure business logic, no AI) ─────────────────────
        services.AddSingleton<IBranchDetector,   BranchDetector>();
        services.AddSingleton<ICountryDetector,  CountryDetector>();
        services.AddSingleton<IKeywordExtractor, KeywordExtractor>();

        // ── Session Store (volatile memory — in-process) ─────────────────────
        services.AddSingleton<ISessionStore, InMemorySessionStore>();

        // ── Repository (Elasticsearch-backed) ────────────────────────────────
        services.AddSingleton<IConsultationRepository, ElasticsearchConsultationRepository>();

        return services;
    }
}
// Note: IRetrievalAgent, IDocumentGenerationAgent, ISearchAgent
// are registered in Program.cs (API layer) because they need IWebHostEnvironment
// or are in Infrastructure/Search subfolder — see FiscalPlatform.API/Program.cs
