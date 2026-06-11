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

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // ── AI Agents ─────────────────────────────────────────────────────────
        services.AddSingleton<ILlmAgent,                LlmAgent>();
        services.AddSingleton<IEmbedSearchAgent,        EmbedSearchAgent>();
        services.AddSingleton<IFeedbackAgent,           FeedbackAgent>();
        services.AddSingleton<IRetrievalAgent,          RetrievalAgent>();
        services.AddSingleton<IDocumentGenerationAgent, DocumentGenerationAgent>();
        services.AddSingleton<ISearchAgent,             ElasticsearchSearchAgent>();

        // ── True SK ReAct Agent — Retrieval Planner ───────────────────────────
        // Uses SK ChatCompletionAgent with targeted fetch tools.
        // Max 2 ReAct iterations, ~8-12s additional time, gives legally-guided sources.
        services.AddSingleton<IRetrievalPlannerAgent,   RetrievalPlannerAgent>();

        // ── Domain Services (pure logic, no AI) ───────────────────────────────
        services.AddSingleton<IBranchDetector,   BranchDetector>();
        services.AddSingleton<ICountryDetector,  CountryDetector>();
        services.AddSingleton<IKeywordExtractor, KeywordExtractor>();

        // ── Memory ────────────────────────────────────────────────────────────
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<RewardMemory>();

        // ── Guardrails ────────────────────────────────────────────────────────
        services.AddSingleton<FiscalGuardrails>();

        // ── Semantic Kernel (refinement agent) ────────────────────────────────
        services.AddSingleton<FiscalKernelFactory>();
        services.AddSingleton<Microsoft.SemanticKernel.Kernel>(sp =>
            sp.GetRequiredService<FiscalKernelFactory>().Create());

        // ── Repository ────────────────────────────────────────────────────────
        services.AddSingleton<IConsultationRepository, ElasticsearchConsultationRepository>();

        return services;
    }
}
