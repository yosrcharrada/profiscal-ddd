using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Domain.Repositories;
using FiscalPlatform.Infrastructure.Agents;
using FiscalPlatform.Infrastructure.DomainServices;
using FiscalPlatform.Infrastructure.Guardrails;
using FiscalPlatform.Infrastructure.Kernel;
using FiscalPlatform.Infrastructure.Memory;
using FiscalPlatform.Infrastructure.Persistence;
using FiscalPlatform.Infrastructure.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// Composition root — all infrastructure registrations in one place.
/// Program.cs calls services.AddInfrastructure() only.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // ── AI Agents (single responsibility each) ───────────────────────────
        services.AddSingleton<ILlmAgent,                LlmAgent>();
        services.AddSingleton<IEmbedSearchAgent,        EmbedSearchAgent>();
        services.AddSingleton<IFeedbackAgent,           FeedbackAgent>();
        services.AddSingleton<IRetrievalAgent,          RetrievalAgent>();
        services.AddSingleton<IDocumentGenerationAgent, DocumentGenerationAgent>();
        services.AddSingleton<ISearchAgent,             ElasticsearchSearchAgent>();

        // ── Domain Services (pure logic, no AI) ──────────────────────────────
        services.AddSingleton<IBranchDetector,   BranchDetector>();
        services.AddSingleton<ICountryDetector,  CountryDetector>();
        services.AddSingleton<IKeywordExtractor, KeywordExtractor>();

        // ── Memory ───────────────────────────────────────────────────────────
        services.AddSingleton<ISessionStore,  InMemorySessionStore>();
        services.AddSingleton<RewardMemory>();                  // RLHF reward memory

        // ── Guardrails ───────────────────────────────────────────────────────
        services.AddSingleton<FiscalGuardrails>();

        // ── Semantic Kernel (true agent infrastructure) ───────────────────────
        // FiscalKernelFactory builds a Kernel with RetrievalPlugin + AnalysisPlugin
        // Used by ChatCompletionAgent in RefineConsultationCommandHandler
        services.AddSingleton<FiscalKernelFactory>();
        services.AddSingleton<Microsoft.SemanticKernel.Kernel>(sp =>
            sp.GetRequiredService<FiscalKernelFactory>().Create());

        // ── Repository ───────────────────────────────────────────────────────
        services.AddSingleton<IConsultationRepository, ElasticsearchConsultationRepository>();

        return services;
    }
}
