using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Infrastructure.Kernel.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

// Explicit alias to avoid conflict with FiscalPlatform.Infrastructure.Kernel namespace
using SKKernel = Microsoft.SemanticKernel.Kernel;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// TRUE ReAct Agent for legal source retrieval planning.
/// Brain + Memory + Actions — max 2 iterations with GPT-4o to stay under 90s total.
/// </summary>
public sealed class RetrievalPlannerAgent : IRetrievalPlannerAgent
{
    private readonly IRetrievalAgent   _retrieval;
    private readonly IEmbedSearchAgent _embed;
    private readonly IConfiguration    _config;
    private readonly ILogger<RetrievalPlannerAgent> _logger;

    private static readonly HashSet<string> NoteCommune2Exceptions =
        new(StringComparer.OrdinalIgnoreCase) { "allemagne" };

    private const int MaxIterations = 2;

    private const string PlannerInstructions =
        "Tu es un expert en recherche de sources juridiques fiscales tunisiennes.\n" +
        "Ta SEULE mission: trouver les sources juridiques EXACTES pour cette situation.\n" +
        "Tu as AU MAXIMUM 2 tours d'appels d'outils.\n\n" +

        "SÉQUENCE OBLIGATOIRE pour tout prestataire étranger:\n" +
        "1. TOUJOURS appeler fetch_convention_article avec article_type='etablissement_stable'\n" +
        "2. Si prestation/service/redevance: appeler fetch_convention_article avec article_type='redevances'\n" +
        "3. TOUJOURS appeler fetch_note_commune_2 (SAUF si pays = Allemagne)\n" +
        "4. TOUJOURS appeler fetch_domestic_retenue comme fallback\n\n" +

        "ARTICLES DE CONVENTION:\n" +
        "Art.5=établissement_stable, Art.7=benefices, Art.10=dividendes,\n" +
        "Art.11=interets, Art.12=redevances, Art.14=prof_independantes, Art.15=prof_dependantes\n\n" +

        "RÈGLES: Art.92 CIRPPIS = référence LF, pas article autonome.\n" +
        "Tout taux doit venir d'une source fetchée.\n" +
        "Appelle finish_retrieval dès ≥10 sources OU au 2ème tour.";

    public RetrievalPlannerAgent(
        IRetrievalAgent   retrieval,
        IEmbedSearchAgent embed,
        IConfiguration    config,
        ILogger<RetrievalPlannerAgent> logger)
    {
        _retrieval = retrieval;
        _embed     = embed;
        _config    = config;
        _logger    = logger;
    }

    public async Task<RetrievalPlan> PlanAndRetrieveAsync(
        string          situation,
        string          fiscalQuestion,
        HashSet<string> branches,
        List<string>    countries,
        bool            isIntl,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("┌─ [PLANNER] Starting retrieval planning…");

        var kernel = BuildPlannerKernel();

        var plugin = new RetrievalPlannerPlugin(
            _retrieval, _embed,
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<RetrievalPlannerPlugin>.Instance);

        kernel.ImportPluginFromObject(plugin, "Retrieval");

        var agent = new ChatCompletionAgent
        {
            Name         = "RetrievalPlanner",
            Instructions = PlannerInstructions,
            Kernel       = kernel,
        };

        var country      = countries.FirstOrDefault() ?? "non détecté";
        var noteCommune2 = isIntl && !NoteCommune2Exceptions.Contains(country)
            ? "APPLICABLE — doit être fetchée"
            : NoteCommune2Exceptions.Contains(country)
                ? "NON APPLICABLE (Allemagne)"
                : "non applicable (cas domestique)";

        var planningRequest =
            $"SITUATION: {situation[..Math.Min(situation.Length, 300)]}\n" +
            $"QUESTION: {fiscalQuestion[..Math.Min(fiscalQuestion.Length, 200)]}\n" +
            $"BRANCHES: {string.Join(", ", branches)}\n" +
            $"PAYS: {country}\n" +
            $"INTERNATIONAL: {(isIntl ? "OUI" : "NON")}\n" +
            $"NOTE COMMUNE N°2/2015: {noteCommune2}\n\n" +
            "Fetch les sources juridiques exactes. Appelle finish_retrieval quand tu as assez.";

        var history    = new ChatHistory();
        int iterations = 0;
        history.AddUserMessage(planningRequest);

        while (iterations < MaxIterations && !plugin.IsFinished)
        {
            iterations++;
            _logger.LogInformation("│  [PLANNER] Iteration {I}/{Max}", iterations, MaxIterations);

            try
            {
                var thread = new ChatHistoryAgentThread(history);
                await foreach (var response in agent.InvokeAsync(thread, cancellationToken: ct))
                {
                    var content = response.Message?.Content ?? "";
                    if (!string.IsNullOrEmpty(content))
                        history.AddAssistantMessage(content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("│  [PLANNER] Iteration {I} error: {E}", iterations, ex.Message);
                break;
            }
        }

        sw.Stop();
        var sources = plugin.GetAccumulatedSources();
        for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;

        _logger.LogInformation(
            "└─ [PLANNER] ✓ {Ms:F0}ms | {N} sources | income={I} | es={E} | iter={It}",
            sw.Elapsed.TotalMilliseconds, sources.Count,
            plugin.IncomeType, plugin.EsRiskPossible, iterations);

        return new RetrievalPlan(
            Sources:          sources,
            IncomeType:       plugin.IncomeType,
            EsRiskPossible:   plugin.EsRiskPossible,
            DetectedCountry:  plugin.DetectedCountry ?? countries.FirstOrDefault(),
            NoteCommune2Used: plugin.NoteCommune2Used,
            IterationsUsed:   iterations,
            PlannerReasoning: plugin.PlannerReasoning);
    }

    private SKKernel BuildPlannerKernel()
    {
        var model    = GetEnv("OpenAI:ChatModel", "OPENAI_CHAT_MODEL", "gpt-4o");
        var endpoint = GetEnv("OpenAI:Endpoint",  "OPENAI_ENDPOINT",   "");
        var apiKey   = GetEnv("OpenAI:ApiKey",    "OPENAI_API_KEY",    "");

        var builder = SKKernel.CreateBuilder();

        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey))
            builder.AddAzureOpenAIChatCompletion(model, endpoint, apiKey);

        return builder.Build();
    }

    private string GetEnv(string cfgKey, string envKey, string def)
    {
        var v = _config[cfgKey];
        if (!string.IsNullOrEmpty(v)) return v;
        v = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrEmpty(v) ? def : v.Trim().Trim('"').Trim('\'');
    }
}
