using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Application.Consultation.Agents;
using FiscalPlatform.Infrastructure.Agents;
using FiscalPlatform.Infrastructure.DomainServices;
using FiscalPlatform.Infrastructure.Guardrails;
using FiscalPlatform.Infrastructure.Kernel;
using FiscalPlatform.Infrastructure.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure;

/// <summary>
/// Composition root for the fiscal engine.
/// NOTE: Elasticsearch-backed services (IConsultationRepository, ISearchAgent,
/// IFeedbackAgent) are intentionally NOT registered here — the host project
/// (Profiscal.API) provides EF/Neo4j-backed replacements so the platform runs
/// with only Neo4j + an LLM.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFiscalEngine(this IServiceCollection services)
    {
        // ── AI agents ─────────────────────────────────────────────────────────
        services.AddSingleton<ILlmAgent,                LlmAgent>();
        services.AddSingleton<IEmbedSearchAgent,        EmbedSearchAgent>();
        services.AddSingleton<IRetrievalAgent,          RetrievalAgent>();
        services.AddSingleton<IDocumentGenerationAgent, DocumentGenerationAgent>();
        services.AddSingleton<IRetrievalPlannerAgent,   RetrievalPlannerAgent>();
        services.AddSingleton<IAcceptanceAgent,         AcceptanceAgent>();

        // ── Case agents (one per income qualification; resolved as IEnumerable<ICaseAgent>,
        //    the handler qualifies then dispatches to the matching Type) ──
        services.AddSingleton<ICaseAgent, GenericAgent>();
        services.AddSingleton<ICaseAgent, RsServiceForeignAgent>();
        services.AddSingleton<ICaseAgent, DividendeAgent>();
        services.AddSingleton<ICaseAgent, InteretAgent>();
        services.AddSingleton<ICaseAgent, RedevanceAgent>();

        // ── Rule-based retrieval policy (config-driven routing, not hardcoded answers) ──
        services.AddSingleton<IRuleBasedRetrieval,
            FiscalPlatform.Infrastructure.Retrieval.FiscalRetrievalPolicy>();

        // ── Domain services (pure logic, no AI) ───────────────────────────────
        services.AddSingleton<IBranchDetector,   BranchDetector>();
        services.AddSingleton<ICountryDetector,  CountryDetector>();
        services.AddSingleton<IKeywordExtractor, KeywordExtractor>();

        // ── Memory ────────────────────────────────────────────────────────────
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<RewardMemory>(); // RLHF reward memory (no-op without ES)

        // ── Guardrails ────────────────────────────────────────────────────────
        services.AddSingleton<FiscalGuardrails>();

        // ── Semantic Kernel (true agent infrastructure) ───────────────────────
        services.AddSingleton<FiscalKernelFactory>();
        services.AddSingleton<Microsoft.SemanticKernel.Kernel>(sp =>
            sp.GetRequiredService<FiscalKernelFactory>().Create());

        return services;
    }
}
