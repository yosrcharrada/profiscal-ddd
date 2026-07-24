using Profiscal.Domain.Abstractions.Agents;
using Profiscal.Domain.Abstractions.Services;
using Profiscal.Infrastructure.Agents;
using Profiscal.Infrastructure.DomainServices;
using Profiscal.Infrastructure.Guardrails;
using Profiscal.Infrastructure.Kernel;
using Profiscal.Infrastructure.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Profiscal.Infrastructure;

/// <summary>
/// Composition root for the fiscal engine.
/// NOTE: Elasticsearch-backed services (IConsultationRepository, ISearchAgent,
/// IFeedbackAgent) are intentionally NOT registered here — the host project
/// (Profiscal.Application) provides EF/Neo4j-backed replacements so the platform runs
/// with only Neo4j + an LLM.
/// </summary>
public static class FiscalEngineServiceCollectionExtensions
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

        // NOTE: the case agents (ICaseAgent implementations) and the ConsultationWorkflow
        // orchestrator are APPLICATION-layer types (they live in the Application project with the
        // command handler / orchestration / playbooks they drive), so per the DDD layering they
        // are registered on the Application side (Program.cs), not here. Infrastructure registers
        // only its own implementations of Domain contracts.

        // ── Rule-based retrieval policy (config-driven routing, not hardcoded answers) ──
        services.AddSingleton<IRuleBasedRetrieval,
            Profiscal.Infrastructure.Retrieval.FiscalRetrievalPolicy>();

        // ── Domain services (pure logic, no AI) ───────────────────────────────
        services.AddSingleton<IBranchDetector,   BranchDetector>();
        services.AddSingleton<ICountryDetector,  CountryDetector>();
        services.AddSingleton<IKeywordExtractor, KeywordExtractor>();

        // ── Memory ────────────────────────────────────────────────────────────
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<RewardMemory>(); // RLHF reward memory (no-op without ES)

        // ── Guardrails ────────────────────────────────────────────────────────
        services.AddSingleton<FiscalGuardrails>();
        services.AddSingleton<IFiscalGuardrails>(sp => sp.GetRequiredService<FiscalGuardrails>());

        // ── Semantic Kernel (true agent infrastructure) ───────────────────────
        services.AddSingleton<FiscalKernelFactory>();
        services.AddSingleton<Microsoft.SemanticKernel.Kernel>(sp =>
            sp.GetRequiredService<FiscalKernelFactory>().Create());

        return services;
    }
}
