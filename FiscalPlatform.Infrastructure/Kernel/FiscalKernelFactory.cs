using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Common.Interfaces.Services;
using FiscalPlatform.Infrastructure.Kernel.Plugins;
using FiscalPlatform.Infrastructure.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure.Kernel;

/// <summary>
/// Factory that builds a fully configured Semantic Kernel instance.
/// Each kernel has access to:
///   - RetrievalPlugin  (legal source retrieval from Neo4j + embed server)
///   - AnalysisPlugin   (LLM-powered legal analysis)
/// The kernel is the brain — it decides which plugins to invoke and when.
/// For deterministic generation we call plugins explicitly.
/// For conversation/refinement the agent reasons and decides autonomously.
/// </summary>
public sealed class FiscalKernelFactory
{
    private readonly IConfiguration   _config;
    private readonly IRetrievalAgent  _retrieval;
    private readonly IEmbedSearchAgent _embed;
    private readonly ILlmAgent        _llm;
    private readonly RewardMemory     _reward;
    private readonly ILogger<FiscalKernelFactory> _logger;

    public FiscalKernelFactory(
        IConfiguration    config,
        IRetrievalAgent   retrieval,
        IEmbedSearchAgent embed,
        ILlmAgent         llm,
        RewardMemory      reward,
        ILogger<FiscalKernelFactory> logger)
    {
        _config    = config;
        _retrieval = retrieval;
        _embed     = embed;
        _llm       = llm;
        _reward    = reward;
        _logger    = logger;
    }

    /// <summary>
    /// Creates a Kernel instance with all fiscal plugins registered.
    /// The kernel connects to Azure OpenAI for its own reasoning
    /// and has access to retrieval + analysis plugins as tools.
    /// </summary>
    public Microsoft.SemanticKernel.Kernel Create()
    {
        var model      = GetEnv("OpenAI:ChatModel",  "OPENAI_CHAT_MODEL",  "gpt-4o");
        var endpoint   = GetEnv("OpenAI:Endpoint",   "OPENAI_ENDPOINT",    "");
        var apiKey     = GetEnv("OpenAI:ApiKey",     "OPENAI_API_KEY",     "");
        var apiVersion = GetEnv("OpenAI:ApiVersion", "OPENAI_API_VERSION", "2024-02-15-preview");

        var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey))
        {
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: model,
                endpoint:       endpoint,
                apiKey:         apiKey);
            _logger.LogInformation("SK Kernel: AzureOpenAI connected [{M}]", model);
        }
        else
        {
            _logger.LogWarning("SK Kernel: No OpenAI credentials — kernel has no LLM");
        }

        var kernel = builder.Build();

        // Register plugins — these become the agent's available tools
        kernel.ImportPluginFromObject(
            new RetrievalPlugin(_retrieval, _embed, _reward), "Retrieval");
        kernel.ImportPluginFromObject(
            new LegalAnalysisPlugin(_llm), "Analysis");

        _logger.LogInformation("SK Kernel created with {N} plugins", 2);
        return kernel;
    }

    private string GetEnv(string cfgKey, string envKey, string def)
    {
        var v = _config[cfgKey];
        if (!string.IsNullOrEmpty(v)) return v;
        v = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrEmpty(v) ? def : v;
    }
}
