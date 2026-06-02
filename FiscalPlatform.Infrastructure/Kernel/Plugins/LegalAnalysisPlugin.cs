using System.ComponentModel;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.SemanticKernel;

namespace FiscalPlatform.Infrastructure.Kernel.Plugins;

/// <summary>
/// SK Plugin — wraps LLM-powered legal analysis as kernel functions.
/// The agent calls these when it needs to generate or refine legal content.
/// Each function is a specialized analysis tool the agent can invoke.
/// </summary>
public sealed class LegalAnalysisPlugin
{
    private readonly ILlmAgent _llm;

    private const string BaseSystem =
        "Tu es Faiez Choyakh, fiscaliste EY Tunisia. " +
        "Cite uniquement [S1],[S2]... Pas d'invention d'articles. " +
        "Verdicts clairs: OUI/NON/X%/EXONERE/SOUMIS.";

    public LegalAnalysisPlugin(ILlmAgent llm) => _llm = llm;

    /// <summary>
    /// Analyze a specific fiscal point using the provided legal sources.
    /// The agent calls this for each question point in the étendue.
    /// </summary>
    [KernelFunction("analyze_fiscal_point")]
    [Description("Analyze a specific fiscal point using provided legal sources. Returns a structured analysis with applicable principle, application to the case, and a clear verdict.")]
    public async Task<string> AnalyzeFiscalPoint(
        [Description("The specific fiscal point to analyze")] string point,
        [Description("The client situation context")] string situation,
        [Description("Legal sources in [S1] [S2] format")] string sources,
        [Description("Legal branch: IS, TVA, Retenue, IRPP, PrixTransfert")] string branch)
    {
        var prompt =
            $"Analyser ce point fiscal:\n{point}\n\n" +
            $"Situation: {situation[..Math.Min(situation.Length, 300)]}\n\n" +
            $"Branche: {branch}\n\n" +
            $"SOURCES:\n{sources[..Math.Min(sources.Length, 3000)]}\n\n" +
            "FORMAT:\n" +
            "Principe applicable : [Sn] : \"citation exacte\".\n" +
            "Application au cas : appliquer le principe aux faits.\n" +
            "Conclusion : VERDICT — justification.";

        return await _llm.CompleteAsync(BaseSystem, prompt, $"Analyze:{branch}", 1500)
               ?? "Analyse non disponible.";
    }

    /// <summary>
    /// Refine or correct a section of an existing consultation.
    /// The agent calls this when the user requests a modification.
    /// </summary>
    [KernelFunction("refine_section")]
    [Description("Modify or correct a specific section of an existing fiscal consultation based on user instructions. Preserves professional style and citation format.")]
    public async Task<string> RefineSection(
        [Description("The section name to refine (analyses, sommaire, etendue, etc.)")] string sectionName,
        [Description("Current content of the section")] string currentContent,
        [Description("User's instruction for what to change")] string instruction,
        [Description("Available legal sources in [S1] format")] string sources)
    {
        var prompt =
            $"Section à modifier: {sectionName}\n\n" +
            $"Contenu actuel:\n{currentContent[..Math.Min(currentContent.Length, 2000)]}\n\n" +
            $"Instruction: {instruction}\n\n" +
            $"Sources disponibles:\n{sources[..Math.Min(sources.Length, 1500)]}\n\n" +
            "Retourner UNIQUEMENT le texte corrigé de la section, sans explication.";

        return await _llm.CompleteAsync(
            "Tu es Faiez Choyakh, fiscaliste EY Tunisia. Conserve le style professionnel et les citations [Sn].",
            prompt, $"Refine:{sectionName}", 2000)
               ?? currentContent;
    }

    /// <summary>
    /// Generate the sommaire exécutif from the analysis results.
    /// The agent calls this after all points are analyzed.
    /// </summary>
    [KernelFunction("generate_sommaire")]
    [Description("Generate the executive summary (sommaire exécutif) from the analyzed fiscal points. Produces a concise verdict for each point.")]
    public async Task<string> GenerateSommaire(
        [Description("All analyzed points and their verdicts")] string analysisResults,
        [Description("Client name")] string clientName)
    {
        var prompt =
            $"Client: {clientName}\n\n" +
            $"Analyses:\n{analysisResults[..Math.Min(analysisResults.Length, 3000)]}\n\n" +
            "Générer un sommaire exécutif concis avec un verdict clair par point. " +
            "Max 1 citation [Sn] par point. Format professionnel EY.";

        return await _llm.CompleteAsync(BaseSystem, prompt, "Sommaire", 1500)
               ?? "Sommaire non disponible.";
    }

    /// <summary>
    /// Answer a factual fiscal question directly.
    /// The agent calls this for simple Q&A in the chat feature.
    /// </summary>
    [KernelFunction("answer_fiscal_question")]
    [Description("Answer a direct fiscal question using provided legal sources. For simple factual questions that don't require full consultation generation.")]
    public async Task<string> AnswerFiscalQuestion(
        [Description("The fiscal question")] string question,
        [Description("Legal sources in [S1] format")] string sources)
    {
        var prompt =
            $"Question: {question}\n\n" +
            $"Sources:\n{sources[..Math.Min(sources.Length, 3000)]}\n\n" +
            "Répondre directement en citant les sources [Sn]. " +
            "Si la réponse n'est pas dans les sources, le dire clairement.";

        return await _llm.CompleteAsync(BaseSystem, prompt, "Answer", 1000)
               ?? "Réponse non disponible.";
    }
}
