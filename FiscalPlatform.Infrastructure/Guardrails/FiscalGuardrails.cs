using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Guardrails;

/// <summary>
/// Fiscal Guardrails — input + output validation layer.
///
/// INPUT GUARDRAIL:
///   Verifies the question is genuinely fiscal before calling GPT-4o.
///   Blocks off-topic requests (cooking recipes, personal advice, etc.)
///   Saves tokens and prevents misuse.
///
/// OUTPUT GUARDRAIL:
///   After generation, verifies:
///   1. All [Sn] citations reference sources that actually exist
///   2. No section is empty or too short
///   3. Each analysis block contains a verdict
///   4. No hallucinated article numbers appear
/// </summary>
public sealed class FiscalGuardrails
{
    private readonly ILogger<FiscalGuardrails> _logger;

    // Fiscal keywords — at least one must appear in the question
    private static readonly string[] FiscalKeywords =
    {
        "impôt","taxe","tva","irpp","retenue","fiscal","fisc","cotisation",
        "déductib","exonér","assujetti","déclaration","société","bénéfice",
        "revenu","dividende","plus-value","convention","double imposition",
        "prix de transfert","management fee","prestation","facturation",
        "redevance","loyer","salaire","dirigeant","non-résident","source",
        "taux","base imposable","résultat","charge","produit","amortissement",
        "provision","crédit","remboursement","sanction","pénalité","contrôle",
        "vérification","redressement","réclamation","délai","prescription",
        "is","tvt","droits","droits d'enregistrement","douane","accise",
        "contribution","prélèvement","retenu","versement","acompte",
    };

    // Patterns that suggest hallucinated content
    private static readonly Regex[] HallucinationPatterns =
    {
        new(@"\[code\s+\w+[_\-]\d{4}\s*,\s*Art\.\s*\d+\]", RegexOptions.IgnoreCase), // [code irpp_is_2019, Art. 24]
        new(@"article\s+\d+\s+(?:du|de la|de l')\s+(?:code|loi)", RegexOptions.IgnoreCase), // article 52 du code (without [Sn])
    };

    public FiscalGuardrails(ILogger<FiscalGuardrails> logger) => _logger = logger;

    // ── INPUT GUARDRAIL ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates that the input is a genuine fiscal question.
    /// Returns (isValid, reason).
    /// </summary>
    public (bool IsValid, string? Reason) ValidateInput(string situation, string fiscalQuestion)
    {
        var combined = (situation + " " + fiscalQuestion).ToLower();

        // Must contain at least one fiscal keyword
        bool hasFiscalContent = FiscalKeywords.Any(kw =>
            combined.Contains(kw, StringComparison.OrdinalIgnoreCase));

        if (!hasFiscalContent)
        {
            _logger.LogWarning("INPUT GUARDRAIL: No fiscal keywords detected in question");
            return (false, "La question ne semble pas être de nature fiscale. " +
                          "Veuillez préciser votre question fiscale (IS, TVA, retenue, etc.)");
        }

        // Minimum length checks
        if (situation.Trim().Length < 20)
            return (false, "La situation doit décrire le contexte en au moins 20 caractères.");

        if (fiscalQuestion.Trim().Length < 10)
            return (false, "La question fiscale doit comporter au moins 10 caractères.");

        _logger.LogInformation("INPUT GUARDRAIL: ✅ Valid fiscal question");
        return (true, null);
    }

    // ── OUTPUT GUARDRAIL ──────────────────────────────────────────────────────

    /// <summary>
    /// Validates the generated consultation output.
    /// Returns a list of issues found (empty = all good).
    /// </summary>
    public List<GuardrailIssue> ValidateOutput(
        ConsultationOutput output,
        List<LegalSourceDto> sources)
    {
        var issues = new List<GuardrailIssue>();
        var maxIdx = sources.Count;

        // 1. Check all [Sn] citations reference real sources
        var allText = output.Analyses + " " + output.SommairExecutif + " " + output.Documents;
        var citationMatches = Regex.Matches(allText, @"\[S(\d+)\]");
        foreach (Match m in citationMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var idx) && idx > maxIdx)
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Error,
                    $"Citation [S{idx}] references a non-existent source (max: S{maxIdx})",
                    "Citation invalide détectée — source inexistante"));
            }
        }

        // 2. Check analyses is not empty
        if (string.IsNullOrWhiteSpace(output.Analyses) || output.Analyses.Length < 100)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Error,
                "Analyses section is empty or too short",
                "La section analyses est vide ou insuffisante"));
        }

        // 3. Check sommaire exists
        if (string.IsNullOrWhiteSpace(output.SommairExecutif) || output.SommairExecutif.Length < 50)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Warning,
                "Sommaire is empty or too short",
                "Le sommaire exécutif est insuffisant"));
        }

        // 4. Check each analysis table row has a verdict
        foreach (var row in output.AnalysisTable)
        {
            var hasVerdict = new[] { "OUI", "NON", "SOUMIS", "EXONÉR", "DÉDUCTIBL", "%" }
                .Any(v => row.Conclusion.ToUpper().Contains(v));
            if (!hasVerdict)
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Warning,
                    $"Row '{row.Sujet[..Math.Min(row.Sujet.Length, 40)]}' has no clear verdict",
                    "Un point d'analyse n'a pas de verdict clair"));
            }
        }

        // 5. Check for hallucination patterns (citation format violations)
        foreach (var pattern in HallucinationPatterns)
        {
            var matches = pattern.Matches(allText);
            foreach (Match m in matches)
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Error,
                    $"Hallucinated citation pattern detected: '{m.Value}'",
                    $"Citation mal formatée détectée: '{m.Value}' — doit utiliser [Sn]"));
                _logger.LogWarning("OUTPUT GUARDRAIL: Hallucination pattern: {P}", m.Value);
            }
        }

        // 6. Minimum citation count
        var citationCount = citationMatches.Count;
        if (citationCount < 3)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Warning,
                $"Only {citationCount} citations found — expected at least 3",
                "Peu de sources citées — la consultation manque peut-être de fondements juridiques"));
        }

        if (!issues.Any())
            _logger.LogInformation("OUTPUT GUARDRAIL: ✅ All checks passed ({C} citations, {R} table rows)",
                citationCount, output.AnalysisTable.Count);
        else
            _logger.LogWarning("OUTPUT GUARDRAIL: {N} issue(s) found", issues.Count);

        return issues;
    }
}

public sealed record GuardrailIssue(
    GuardrailSeverity Severity,
    string            TechnicalDetail,
    string            UserMessage);

public enum GuardrailSeverity { Warning, Error }
