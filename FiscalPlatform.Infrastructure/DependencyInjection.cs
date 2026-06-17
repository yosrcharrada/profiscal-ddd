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

        // ── TRUE ReAct Retrieval Agent ────────────────────────────────────────
        // Bounded 2-round ReAct loop with parallel tool dispatch.
        // Brain (GPT-4o via ILlmAgent) decides tools, observes results, adapts.
        // ~13-16s total (2 LLM calls + parallel fetches), genuine agent behaviour.
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

        // ── Semantic Kernel (refinement agent only) ───────────────────────────
        services.AddSingleton<FiscalKernelFactory>();
        services.AddSingleton<Microsoft.SemanticKernel.Kernel>(sp =>
            sp.GetRequiredService<FiscalKernelFactory>().Create());

        // ── Repository ────────────────────────────────────────────────────────
        services.AddSingleton<IConsultationRepository, ElasticsearchConsultationRepository>();

        return services;
    }
}
