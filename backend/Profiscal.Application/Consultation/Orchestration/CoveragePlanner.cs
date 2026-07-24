using System.Text;
using System.Text.Json;
using Profiscal.Domain.Abstractions.Agents;
using Microsoft.Extensions.Logging;
using H = Profiscal.Application.Consultation.Commands.GenerateConsultation.GenerateConsultationCommandHandler;

namespace Profiscal.Application.Consultation.Orchestration;

/// <summary>
/// The COVERAGE PLANNER — the "thinking" step between the case brief and retrieval fulfilment.
///
/// Problem it solves: the static per-case checklist is the tax team's source map for the TYPICAL
/// shape of a case, but an étendue can imply aspects the checklist doesn't carry (the interest
/// consultation whose « régime fiscal » silently implied TVA — never fetched, never written). One
/// bounded LLM call reads each étendue point and SELECTS which fiscal aspects it implies — from a
/// closed, deterministic library. The LLM never invents a fetch spec, a document name, a rate or a
/// verdict: it only picks aspect KEYS; the RequiredSource templates and writer directives are code.
///
/// Selected aspects (that the case checklist doesn't already cover) become:
///   • extra RequiredSource items (Critical:false — they enrich retrieval, never deadlock the
///     completeness gate on an LLM choice), fetched by the same deterministic fulfil node;
///   • per-point coverage directives appended to the brief's démarche, so the writer must address
///     each implied aspect and the judge can hold it to that.
/// Fail-open: any LLM/parse failure returns the brief unchanged.
/// </summary>
public static class CoveragePlanner
{
    private sealed record Aspect(
        string Key,
        string LlmDescription,                       // shown to the selector LLM
        string WriterDirective,                      // appended to the démarche when selected
        Func<ConsultationState, RequiredSource> Make,
        string[] CoveredByKeyFragments);             // skip if the case checklist already has one

    private static readonly Aspect[] Library =
    {
        new("tva",
            "TVA : le point implique de déterminer le régime TVA de l'opération (champ, territorialité, exonérations, taux)",
            "TVA : détermine le régime TVA de l'opération À PARTIR des textes du CTVA cités [Sn] (champ, " +
            "territorialité, exonérations) — jamais de mémoire, jamais sans citation.",
            s => new RequiredSource("cov_tva",
                "CTVA — champ / territorialité / exonérations / taux applicables à l'opération",
                Critical: false, DocFragment: "code_tva", FetchDocFragment: "code_tva",
                FetchKeywords: new[] { "champ d'application", "territorialité", "exonéré", "opérations" }),
            new[] { "ctva", "tva" }),

        new("rs_taux",
            "RS : le point implique le taux de retenue à la source applicable au paiement",
            "RS : lis le taux dans la LIGNE de l'Art.52 CIRPPIS correspondant à la nature du revenu et à " +
            "la qualité du bénéficiaire [Sn] ; confronte-le au plafond conventionnel le cas échéant.",
            s => new RequiredSource("cov_rs_taux",
                "CIRPPIS Art.52 (taux de RS, texte complet avec %)",
                Critical: false, DocFragment: "code_irpp_is", ArticleNumber: "52",
                RequirePercent: true, FetchDocFragment: "code_irpp_is"),
            new[] { "art52", "52" }),

        new("assiette",
            "ASSIETTE : le point implique de préciser l'assiette de la retenue (montant brut, TVA comprise ou non)",
            "ASSIETTE : précise l'assiette de la RS à partir de la doctrine citée [Sn].",
            s => new RequiredSource("cov_assiette",
                "Note commune N°3/2015 (assiette de la retenue à la source)",
                Critical: false, DocFragment: "NC_2015_03", FetchDocFragment: "NC_2015_03",
                FetchKeywords: new[] { "assiette", "montant brut" }),
            new[] { "nc3", "2015_03" }),

        new("deductibilite_interets_associes",
            "DÉDUCTIBILITÉ : prêt/compte courant d'un ASSOCIÉ ou de la société mère → conditions de déductibilité des intérêts",
            "DÉDUCTIBILITÉ : examine les conditions de déductibilité des intérêts servis aux associés " +
            "(Art.48 CIRPPIS [Sn]) et APPLIQUE-les aux faits (capital libéré ? plafond vs capital ?).",
            s => new RequiredSource("cov_deduct_interets",
                "CIRPPIS Art.48 — conditions de déductibilité des intérêts servis aux associés",
                Critical: false, DocFragment: "code_irpp_is", ArticleNumber: "48",
                TextContains: "associés", RequirePercent: true, FetchDocFragment: "code_irpp_is"),
            new[] { "48" }),

        new("transfert_formalisme",
            "TRANSFERT : le point implique le transfert de fonds à l'étranger (certificats, justificatifs bancaires)",
            "TRANSFERT : traite le formalisme du transfert des fonds via l'Art.112 CDPF ET l'Art.21 de la " +
            "circulaire BCT N°2016-9 ENSEMBLE [Sn], en appliquant aux faits les conditions d'exonération " +
            "du certificat de régularisation prévues par l'Art.112.",
            s => new RequiredSource("cov_transfert_112",
                "Art.112 CDPF (certificat de régularisation — transfert des fonds)",
                Critical: false, DocFragment: "code_droits_procedures", ArticleNumber: "112",
                FetchDocFragment: "code_droits_procedures",
                FetchKeywords: new[] { "régularisation de leur situation fiscale", "attestation" }),
            new[] { "cdpf_112", "bct" }),

        new("regime_privilegie",
            "RÉGIME PRIVILÉGIÉ : bénéficiaire potentiellement établi dans un État à régime fiscal privilégié (et AUCUNE convention applicable)",
            "RÉGIME PRIVILÉGIÉ : vérifie le pays du bénéficiaire DANS la liste citée [Sn] ; si listé, " +
            "n'analyse PAS l'établissement stable — passe directement à la RS.",
            s => new RequiredSource("cov_privilegie",
                "Note commune N°16/2019 — liste des États/territoires à régime fiscal privilégié",
                Critical: false, DocFragment: "NC_2019_16", FetchDocFragment: "NC_2019_16",
                FetchKeywords: new[] { "privilégié", "régime fiscal", "liste des Etats" }),
            new[] { "privilegie", "nc_2019_16" }),

        new("penalites",
            "PÉNALITÉS : le point implique de quantifier des pénalités/intérêts de retard ou un risque de régularisation",
            "PÉNALITÉS : quantifie le risque (prise en charge, pénalités de retard) à partir des textes " +
            "cités [Sn], appliqués aux montants des faits.",
            s => new RequiredSource("cov_penalites",
                "CDPF — pénalités et intérêts de retard",
                Critical: false, DocFragment: "code_droits_procedures",
                FetchDocFragment: "code_droits_procedures",
                FetchKeywords: new[] { "pénalités de retard", "intérêt de retard" }),
            new[] { "penalite", "lf_regularisation" }),
    };

    public static async Task<CaseBrief> ExpandAsync(
        CaseBrief brief, ConsultationState state, ILlmAgent llm, ILogger logger, CancellationToken ct)
    {
        try
        {
            if (state.EtendueItems.Count == 0) return brief;

            var menu = string.Join("\n", Library.Select(a => $"- {a.Key} : {a.LlmDescription}"));
            var sys =
                "Tu es un fiscaliste tunisien senior. Pour CHAQUE point de l'étendue d'une consultation, " +
                "tu listes les ASPECTS fiscaux que le point IMPLIQUE réellement — en choisissant UNIQUEMENT " +
                "parmi les clés du menu fourni (aucune autre valeur). Règles métier : le « régime fiscal » " +
                "d'un paiement transfrontalier implique rs_taux ET tva ; un transfert en devises implique " +
                "transfert_formalisme ; un prêt consenti par un associé/une société mère implique " +
                "deductibilite_interets_associes ; une retenue non opérée implique penalites. N'invente rien : " +
                "un aspect non impliqué par le point ne doit PAS être listé. JSON PUR UNIQUEMENT : " +
                "{\"points\":[{\"point\":\"...\",\"aspects\":[\"cle\",...]}]}";
            var user =
                $"CAS : {brief.Label}\nFAITS (résumé) : {Trunc(state.ContexteFaits, 900)}\n\n" +
                $"MENU DES ASPECTS :\n{menu}\n\nPOINTS DE L'ÉTENDUE :\n" +
                string.Join("\n", state.EtendueItems.Select((e, i) => $"{i + 1}. {e}")) + "\nJSON.";

            var raw = await llm.CompleteAsync(sys, user, "Coverage", 500, ct);
            var parsed = raw is not null ? H.ParseJsonDict(raw) : null;
            if (parsed is null || !parsed.TryGetValue("points", out var pts) || pts.ValueKind != JsonValueKind.Array)
                return brief;

            var selected = new List<(string Point, string AspectKey)>();
            foreach (var p in pts.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var point = p.TryGetProperty("point", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() ?? "" : "";
                if (!p.TryGetProperty("aspects", out var asp) || asp.ValueKind != JsonValueKind.Array) continue;
                foreach (var a in asp.EnumerateArray())
                    if (a.ValueKind == JsonValueKind.String && a.GetString() is { Length: > 0 } key)
                        selected.Add((point, key.Trim().ToLowerInvariant()));
            }
            if (selected.Count == 0) return brief;

            // Merge: only aspects the case checklist doesn't already cover; bounded; never critical.
            var existingKeys = brief.RequiredSources.Select(r => r.Key.ToLowerInvariant()).ToList();
            var extraSources = new List<RequiredSource>();
            var directives   = new StringBuilder();
            foreach (var group in selected.GroupBy(s => s.AspectKey))
            {
                var aspect = Library.FirstOrDefault(a => a.Key == group.Key);
                if (aspect is null) continue;   // unknown key hallucinated → ignored
                var covered = aspect.CoveredByKeyFragments.Any(f => existingKeys.Any(k => k.Contains(f)));
                if (!covered && extraSources.Count < 4)
                    extraSources.Add(aspect.Make(state));
                directives.AppendLine($"   • {aspect.WriterDirective}");
            }

            var demarche = brief.Demarche;
            if (directives.Length > 0)
                demarche += "\nCOUVERTURE REQUISE (déduite de l'étendue — chaque aspect ci-dessous doit être " +
                            "traité dans l'analyse, fondé sur les sources citées [Sn]) :\n" + directives;

            logger.LogInformation("► [GRAPH:Coverage] aspects=[{A}] → +{N} dynamic source item(s)",
                string.Join(", ", selected.Select(s => s.AspectKey).Distinct()), extraSources.Count);

            return brief with
            {
                Demarche = demarche,
                RequiredSources = brief.RequiredSources.Concat(extraSources).ToList(),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GRAPH:Coverage] failed — keeping static brief");
            return brief;
        }
    }

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s[..n] + "…" : s);
}
