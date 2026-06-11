using System.Text.RegularExpressions;
using FiscalPlatform.Application.Common.DTOs;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Guardrails;

/// <summary>
/// Fiscal Guardrails — input + output validation.
///
/// INPUT GUARDRAIL:
///   Verifies question is genuinely fiscal before calling GPT-4o.
///
/// OUTPUT GUARDRAIL:
///   After generation verifies:
///   1. All [Sn] citations reference sources that actually exist
///   2. No section is empty or too short
///   3. Each analysis block contains a verdict
///   4. No hallucination citation patterns
///   5. All percentages cite their [Sn] source
///   6. Art.92 CIRPPIS flagged as LF reference warning
/// </summary>
public sealed class FiscalGuardrails
{
    private readonly ILogger<FiscalGuardrails> _logger;

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
        "is ","tvt","droits","droits d'enregistrement","douane","accise",
        "contribution","prélèvement","retenu","versement","acompte","redevances",
        "établissement stable","assistance technique","frais de siège",
    };

    // Patterns that indicate hallucinated or wrong citation format
    private static readonly Regex[] HallucinationPatterns =
    {
        // Wrong format: [code irpp_is_2019, Art. 24] — should be [S1]
        new(@"\[code\s+\w+[_\-]\d{4}\s*,\s*Art\.\s*\d+\]", RegexOptions.IgnoreCase),
        // Wrong format: (article X du code) — should cite [Sn]
        new(@"\(article\s+\d+\s+du\s+(?:code|loi)\b", RegexOptions.IgnoreCase),
    };

    // Pattern: percentage NOT followed by [Sn] citation within 100 chars
    private static readonly Regex PercentagePattern =
        new(@"\b(\d{1,2}(?:[.,]\d)?)\s*%\b", RegexOptions.IgnoreCase);
    private static readonly Regex CitationNearby =
        new(@"\[S\d+\]", RegexOptions.IgnoreCase);

    public FiscalGuardrails(ILogger<FiscalGuardrails> logger) => _logger = logger;

    // ── INPUT GUARDRAIL ───────────────────────────────────────────────────────

    public (bool IsValid, string? Reason) ValidateInput(string situation, string fiscalQuestion)
    {
        var combined = (situation + " " + fiscalQuestion).ToLower();

        bool hasFiscalContent = FiscalKeywords.Any(kw =>
            combined.Contains(kw, StringComparison.OrdinalIgnoreCase));

        if (!hasFiscalContent)
        {
            _logger.LogWarning("INPUT GUARDRAIL: No fiscal keywords detected");
            return (false,
                "La question ne semble pas être de nature fiscale. " +
                "Veuillez préciser votre question fiscale (IS, TVA, retenue, etc.)");
        }

        if (situation.Trim().Length < 20)
            return (false, "La situation doit décrire le contexte en au moins 20 caractères.");

        if (fiscalQuestion.Trim().Length < 10)
            return (false, "La question fiscale doit comporter au moins 10 caractères.");

        _logger.LogInformation("INPUT GUARDRAIL: ✅ Valid fiscal question");
        return (true, null);
    }

    // ── OUTPUT GUARDRAIL ──────────────────────────────────────────────────────

    public List<GuardrailIssue> ValidateOutput(
        ConsultationOutput output, List<LegalSourceDto> sources)
    {
        var issues = new List<GuardrailIssue>();
        var maxIdx = sources.Count;
        var allText = (output.Analyses ?? "") + " " +
                      (output.SommairExecutif ?? "") + " " +
                      (output.Documents ?? "");

        // 1. Check [Sn] citations reference real sources
        var citationMatches = Regex.Matches(allText, @"\[S(\d+)\]");
        foreach (Match m in citationMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var idx) && idx > maxIdx)
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Error,
                    $"[S{idx}] references non-existent source (max: S{maxIdx})",
                    $"Citation [S{idx}] invalide — source inexistante"));
            }
        }

        // 2. Check analyses section not empty
        if (string.IsNullOrWhiteSpace(output.Analyses) || output.Analyses.Length < 100)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Error,
                "Analyses section empty or too short",
                "La section analyses est vide ou insuffisante"));
        }

        // 3. Check sommaire exists
        if (string.IsNullOrWhiteSpace(output.SommairExecutif) ||
            output.SommairExecutif.Length < 50)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Warning,
                "Sommaire too short",
                "Le sommaire exécutif est insuffisant"));
        }

        // 4. Check verdicts in table
        foreach (var row in output.AnalysisTable)
        {
            var hasVerdict = new[] { "OUI", "NON", "SOUMIS", "EXONÉR", "DÉDUCTIBL", "%" }
                .Any(v => row.Conclusion.ToUpper().Contains(v));
            if (!hasVerdict)
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Warning,
                    $"Row '{row.Sujet[..Math.Min(row.Sujet.Length,40)]}' has no verdict",
                    "Un point d'analyse n'a pas de verdict clair"));
            }
        }

        // 5. Check hallucination citation patterns
        foreach (var pattern in HallucinationPatterns)
        {
            foreach (Match m in pattern.Matches(allText))
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Error,
                    $"Wrong citation format: '{m.Value}'",
                    $"Format de citation invalide: '{m.Value}' — utiliser [Sn]"));
                _logger.LogWarning("OUTPUT GUARDRAIL: Wrong citation: {P}", m.Value);
            }
        }

        // 6. NEW: Check percentages cite their source
        // Find all percentages and check if [Sn] appears within 150 chars after
        var percentMatches = PercentagePattern.Matches(allText);
        foreach (Match pm in percentMatches)
        {
            var windowEnd = Math.Min(allText.Length, pm.Index + 150);
            var window    = allText[pm.Index..windowEnd];
            if (!CitationNearby.IsMatch(window))
            {
                issues.Add(new GuardrailIssue(
                    GuardrailSeverity.Warning,
                    $"Percentage '{pm.Value}' has no [Sn] citation within 150 chars",
                    $"Taux {pm.Value} non justifié par une source [Sn]"));
            }
        }

        // 7. NEW: Check for Art.92 CIRPPIS cited as standalone article
        // (it's an LF amendment reference, not an autonomous CIRPPIS article)
        var art92Pattern = new Regex(
            @"(?:CIRPPIS|code\s+irpp)[^\]]*Art\.?\s*92\b(?!\s*\[réf\.\s*LF\])",
            RegexOptions.IgnoreCase);
        foreach (Match m in art92Pattern.Matches(allText))
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Warning,
                $"Art.92 CIRPPIS cited as standalone — it's an LF reference: '{m.Value}'",
                "Art.92 CIRPPIS est une référence LF, pas un article autonome du code"));
            _logger.LogWarning("OUTPUT GUARDRAIL: Art.92 CIRPPIS cited as standalone");
        }

        // 8. Minimum citation count
        if (citationMatches.Count < 3)
        {
            issues.Add(new GuardrailIssue(
                GuardrailSeverity.Warning,
                $"Only {citationMatches.Count} citations (expected ≥3)",
                "Peu de sources citées — la consultation manque de fondements juridiques"));
        }

        if (!issues.Any())
            _logger.LogInformation(
                "OUTPUT GUARDRAIL: ✅ All checks passed ({C} citations, {R} rows)",
                citationMatches.Count, output.AnalysisTable.Count);
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
