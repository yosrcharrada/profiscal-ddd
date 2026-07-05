using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Consultation.Orchestration;
using FiscalPlatform.Application.Consultation.Playbooks;
using H = FiscalPlatform.Application.Consultation.Commands.GenerateConsultation.GenerateConsultationCommandHandler;

namespace FiscalPlatform.Application.Consultation.Agents;

/// <summary>
/// Shared mechanics for the case agents: assembling the CaseBrief from the per-case content hooks,
/// and the deterministic completeness matcher. Everything CASE-SPECIFIC — démarche, forbidden
/// steps, qualification, skeleton, judge criteria, required-sources checklist — is declared IN the
/// concrete agent below as readable code. The métier source map from the tax team lives here as
/// each case's checklist.
/// </summary>
public abstract class CaseAgentBase : ICaseAgent
{
    public abstract CaseType Type { get; }

    // ── Per-case content hooks (defaults = the legacy foreign-service flow) ──
    protected virtual string   Label                 => Type.ToString();
    protected virtual bool     UsesOwnPrompt         => false;
    protected virtual string   CaseSystemPrompt      => H.SystemPrompt;
    protected virtual string[] Topics                => Array.Empty<string>();
    protected virtual string   Demarche              => "";
    protected virtual string   ForbiddenSteps        => "";
    protected virtual string   QualificationGuidance => "";
    protected virtual string   RedactedSkeleton      => "";
    protected virtual string   JudgeCriteria         => "";

    /// <summary>The case's required-sources checklist — the tax team's source map, as data.</summary>
    protected abstract List<RequiredSource> BuildChecklist(ConsultationState state);

    public virtual CaseBrief BuildBrief(ConsultationState state) => new(
        Label, CaseSystemPrompt, UsesOwnPrompt, Demarche, ForbiddenSteps,
        QualificationGuidance, RedactedSkeleton, JudgeCriteria, Topics,
        BuildChecklist(state));

    public CompletenessReport VerifyCompleteness(
        CaseBrief brief, List<LegalSourceDto> sources, ICollection<string> countries)
    {
        // taxmindvf splits an article into paragraph-level parts, so a predicate combination
        // (e.g. TextContains + RequirePercent) may straddle two parts. Fallback: evaluate items
        // against the CONCATENATED text of all parts of the same article in the same document.
        var grouped = sources
            .GroupBy(s => (s.DocName ?? "") + "|" + Digits(s.ArticleRef))
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var first = g.First();
                return new LegalSourceDto
                {
                    DocName = first.DocName, DocType = first.DocType,
                    ArticleRef = first.ArticleRef, Year = first.Year,
                    Text = string.Join("\n", g.Select(x => x.Text)),
                };
            })
            .ToList();

        var missing = brief.RequiredSources
            .Where(req => !sources.Any(s => req.IsSatisfiedBy(s, countries)) &&
                          !grouped.Any(s => req.IsSatisfiedBy(s, countries)))
            .ToList();
        return new CompletenessReport(
            missing, missing.Where(m => m.Critical).ToList(), brief.RequiredSources.Count);
    }

    private static string Digits(string? s) =>
        string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());

    // ── Shared checklist fragments (métier constants) ──

    /// <summary>Art.112 CDPF + circulaire BCT n°9/2016 — formalisme du transfert des fonds.
    /// Best-effort: the graph carries mostly 112 bis, so we never block the loop on it, but the
    /// fulfilment always tries.</summary>
    protected static RequiredSource Cdpf112 => new(
        Key: "cdpf_112", Critical: false,
        Description: "Art.112 CDPF + circulaire BCT n°9/2016 (certificat de RS, transfert des fonds)",
        DocFragment: "code_droits_procedures", ArticleNumber: "112",
        FetchDocFragment: "code_droits_procedures",
        FetchKeywords: new[] { "certificat de retenue", "transfert", "attestation de régularisation" });

    /// <summary>NC 3/2015 — assiette de la RS (montant brut TVA comprise).</summary>
    protected static RequiredSource Nc3_2015 => new(
        Key: "nc3_2015", Critical: true,
        Description: "Note commune N°3/2015 (assiette de la retenue à la source)",
        DocFragment: "NC_2015_03",
        FetchKeywords: new[] { "honoraires", "assiette", "montant brut" });

    /// <summary>CIRPPIS Art.52 — the multi-rate menu. TextContains narrows to the LINE this case
    /// reads (métier: 'right rate, wrong case' = picking the wrong line).</summary>
    protected static RequiredSource Art52(string key, string description, string? lineContains) => new(
        Key: key, Critical: true, Description: description,
        DocFragment: "code_irpp_is", ArticleNumber: "52",
        RequirePercent: true, TextContains: lineContains,
        FetchDocFragment: "code_irpp_is");

    // Slim system prompt shared by the convention-income agents (dividende/intérêt/redevance): the
    // universal anti-hallucination + citation rules WITHOUT the foreign-service séquence that must
    // never be imposed on a dividend/interest/royalty.
    protected const string ConventionIncomeSystem =
        "Tu es Faiez Choyakh — fiscaliste tunisien senior, EY Tunisia.\n" +
        "CITATIONS : [S1],[S2]… uniquement. Jamais de document en clair. Jamais inventer un article.\n" +
        "TAUX : LIS chaque taux DEPUIS le texte de l'article cité [Sn] et recopie le chiffre EXACT. " +
        "Jamais de taux de mémoire, jamais supposé, jamais « à vérifier ».\n" +
        "TAUX SPÉCIFIQUE (lex specialis) : dans un article de taux, applique la LIGNE correspondant " +
        "PRÉCISÉMENT à la NATURE du revenu (dividende, intérêt, redevance) ET à la QUALITÉ du bénéficiaire " +
        "(non-résident) — jamais la première ligne venue.\n" +
        "PRINCIPE DIRECTEUR — TAUX LE PLUS FAVORABLE : entre le plafond conventionnel et le droit commun, " +
        "retenir le traitement le plus favorable dont les conditions sont remplies. La convention prime le droit commun.\n" +
        "CONVENTION : les articles conventionnels sont retrouvés PAR SUJET (« Dividendes », « Intérêts », " +
        "« Redevances ») — leur numéro varie d'une convention à l'autre ; ne présume aucun numéro.\n" +
        "HIÉRARCHIE : Convention → Codes → LdF → Doctrine.\n" +
        "VERDICTS : un seul par point (le taux chiffré réel lu dans [Sn] / EXONÉRÉ / SOUMIS / OUI / NON) ; " +
        "jamais le littéral « X% ». NON DOCUMENTÉ seulement si aucune source.\n" +
        "N'introduis AUCUNE condition non étayée par les faits. JSON PUR UNIQUEMENT.";
}

// ─────────────────────────────────────────────────────────────────────────────
// One agent per big case. Generic / RS-service brief the proven legacy prompt;
// the convention-income agents carry their own démarche/checklist/judge as code.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Fallback — general international / foreign-service flow (legacy prompt).</summary>
public sealed class GenericAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.Generic;
    protected override string Label => "Cas général / prestation internationale";
    protected override string[] Topics =>
        new[] { "remunerations_techniques", "benefices_entreprises", "etablissement_stable" };

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            Art52("art52_rate", "CIRPPIS Art.52 (article de taux de RS, texte complet avec %)", null),
            new("ctva_7", "CTVA Art.7 (taux de TVA)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "7", RequirePercent: true,
                FetchDocFragment: "code_tva"),
            Cdpf112,
        };
        return list;
    }
}

/// <summary>Foreign supplier of SERVICES — the tax team's map: ES (Art.45/47 + NC 2/2015 +
/// convention) → RS (Art.52 + convention) → TVA (1/3/5/19, taux Art.7) → assiette (NC 3/2015)
/// → transfert (Art.112 CDPF + BCT 9/2016).</summary>
public sealed class RsServiceForeignAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.RsServiceForeign;
    protected override string Label => "Retenue à la source — fournisseur étranger de services";
    protected override string[] Topics => new[]
        { "remunerations_techniques", "services_professionnels", "etablissement_stable", "benefices_entreprises" };

    protected override string JudgeCriteria =>
        "Cas SERVICE / FOURNISSEUR ÉTRANGER : l'établissement stable est tranché d'abord en droit commun " +
        "puis selon l'Art.5 de la convention. La RS applique la ligne de l'Art.52 visant les « non domiciliés " +
        "ni établis » (non les honoraires-résidents). TVA due par retenue de 100% du preneur. Assiette (montant " +
        "brut TVA comprise) et formalisme du transfert (Art.112 CDPF + circ. BCT 9/2016) ne doivent pas manquer.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            new("cirppis_45", "CIRPPIS Art.45 (champ IS des non-résidents)", Critical: true,
                DocFragment: "code_irpp_is", ArticleNumber: "45", FetchDocFragment: "code_irpp_is"),
            new("cirppis_47", "CIRPPIS Art.47 (bénéfices imposables — établissement en Tunisie)", Critical: true,
                DocFragment: "code_irpp_is", ArticleNumber: "47", FetchDocFragment: "code_irpp_is"),
            Art52("art52_nonresident",
                "CIRPPIS Art.52 — ligne « revenus servis aux non domiciliés ni établis »",
                lineContains: "non domicili"),
            Nc3_2015,
            new("ctva_3", "CTVA Art.3 (territorialité)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "3", FetchDocFragment: "code_tva"),
            new("ctva_7", "CTVA Art.7 (taux)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "7", RequirePercent: true, FetchDocFragment: "code_tva"),
            new("ctva_19", "CTVA Art.19 (retenue de 100% de la TVA — prestataire non établi)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "19", FetchDocFragment: "code_tva"),
            new("regime_privilegie", "Liste des États/territoires à régime fiscal privilégié", Critical: false,
                TextContains: "privilégié",
                FetchKeywords: new[] { "régime fiscal privilégié", "liste des Etats", "taux de l'impôt inférieur" }),
            Cdpf112,
        };

        // Convention country → the treaty ES article (by SUBJECT, accent-safe) + NC 2/2015 — the
        // métier exception: NC 2/2015 is NOT used for Allemagne (superseded by the newer convention).
        if (state.Countries.Count > 0)
        {
            list.Add(new("conv_es", "Article « Établissement stable » de la convention applicable",
                Critical: true, ConventionSubject: new[] { "tablissement stable" }, ExistenceConditional: true));
            if (!state.Countries.Any(c => c.Contains("allemagne", StringComparison.OrdinalIgnoreCase)))
                list.Add(new("nc2_2015", "Note commune N°2/2015 (lecture des conventions par pays)",
                    Critical: false, DocFragment: "NC_2015_02",
                    FetchKeywords: new[] { "convention", "non double imposition" }));
        }
        return list;
    }
}

/// <summary>Dividends to a non-resident shareholder. NO ES-of-service, NO TVA section.</summary>
public sealed class DividendeAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.Dividende;
    protected override bool     UsesOwnPrompt    => true;
    protected override string   CaseSystemPrompt => ConventionIncomeSystem;
    protected override string[] Topics =>
        new[] { "dividendes", "elimination_double_imposition", "obligations_societes" };
    protected override string   Label => "Retenue à la source sur DIVIDENDES versés à un non-résident";

    protected override string Demarche =>
        "DÉMARCHE — DIVIDENDES versés à un actionnaire NON-RÉSIDENT :\n" +
        "A. QUALIFICATION & TAUX\n" +
        "   A.1 Qualifier la somme comme DIVIDENDE au sens de l'article « Dividendes » de la\n" +
        "       convention applicable [Sn] (son numéro varie selon la convention). La convention\n" +
        "       attribue à l'État de la source (Tunisie) le droit d'imposer les dividendes, dans la\n" +
        "       limite de sa législation interne et du plafond conventionnel s'il en fixe un.\n" +
        "   A.2 Taux de RS de droit commun : lis-le dans la LIGNE de l'Art.52 CIRPPIS visant les\n" +
        "       « revenus distribués » [Sn] — PAS la ligne des honoraires ni des services. Retiens\n" +
        "       le traitement le plus favorable entre plafond conventionnel et droit commun.\n" +
        "B. RISQUE & RÉGULARISATION (uniquement si les dividendes ont été servis SANS retenue)\n" +
        "   • Quantifier, à partir du montant distribué et du taux lu : la RS non opérée par prise\n" +
        "     en charge (formule montant × taux / (100 − taux)), les pénalités de retard, la\n" +
        "     pénalité fixe éventuelle. Rappeler la prescription.\n" +
        "   • Mentionner toute mesure d'amnistie / dépôt de déclaration rectificative prévue par la\n" +
        "     loi de finances en vigueur si elle est retrouvée dans les sources [Sn].\n" +
        "C. AUTRES OBLIGATIONS\n" +
        "   C.1 Assiette = montant brut des dividendes distribués.\n" +
        "   C.2 Formalisme du transfert des fonds = Art.112 CDPF + circulaire BCT n°9/2016\n" +
        "       (certificat de retenue à la source) ; obligations déclaratives de la société distributrice.\n";

    protected override string ForbiddenSteps =>
        "INTERDIT : ne PAS dérouler la séquence des prestations de services (établissement stable\n" +
        "de type chantier/présence, retenue de TVA de 100% par le preneur). Les dividendes sont\n" +
        "HORS CHAMP de la TVA : si la TVA est demandée, une seule phrase suffit (hors champ). " +
        "L'établissement stable ne se discute que pour dire, brièvement, que la simple détention du\n" +
        "capital n'en crée pas un.";

    protected override string QualificationGuidance =>
        "Revenu = DIVIDENDE (distribution de bénéfices à l'actionnaire non-résident). Dans l'Art.52 " +
        "CIRPPIS, la ligne applicable est celle des « revenus distribués ». Dans la convention, c'est " +
        "l'article intitulé « Dividendes » (retrouvé par sujet, quel que soit son numéro).";

    protected override string RedactedSkeleton =>
        "Analyse\n" +
        "En application des dispositions combinées de l'article [ART] du CIRPPIS [Sn] et de l'article\n" +
        "« Dividendes » de la convention [Sn], les dividendes de source tunisienne versés à [BÉNÉFICIAIRE]\n" +
        "sont imposables en Tunisie et font l'objet d'une retenue à la source au taux de [TAUX] [Sn].\n" +
        "[Si servis sans retenue] Dès lors qu'aucune retenue n'a été opérée, la société distributrice\n" +
        "risque, en cas de contrôle, la réclamation de la retenue non opérée par prise en charge\n" +
        "([FORMULE]) soit [MONTANT], des pénalités de retard de [TAUX] soit [MONTANT], et d'une pénalité\n" +
        "fixe de [TAUX] soit [MONTANT]. [Le cas échéant, une régularisation est possible dans le cadre de\n" +
        "[MESURE LF] [Sn].]\n" +
        "Verdict : [VERDICT].";

    protected override string JudgeCriteria =>
        "Cas DIVIDENDE : le taux doit être lu dans la ligne « revenus distribués » de l'Art.52 (et non " +
        "honoraires/services). AUCUNE séquence d'établissement stable de prestation ni section TVA " +
        "détaillée (dividendes hors champ). Si les dividendes ont été servis sans retenue, le risque " +
        "(prise en charge, pénalités) doit être quantifié. L'article « Dividendes » de la convention " +
        "doit être visé.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            Art52("art52_distribues",
                "CIRPPIS Art.52 — ligne « revenus distribués » (taux de RS sur dividendes)",
                lineContains: "distribu"),
            new("cirppis_29", "CIRPPIS Art.29 (définition des revenus distribués)", Critical: false,
                DocFragment: "code_irpp_is", ArticleNumber: "29", FetchDocFragment: "code_irpp_is"),
            Cdpf112,
        };

        if (state.Countries.Count > 0)
            list.Add(new("conv_dividendes", "Article « Dividendes » de la convention applicable",
                Critical: true, ConventionSubject: new[] { "Dividendes" }, ExistenceConditional: true));

        // Only when the facts say the dividends were paid WITHOUT withholding does the
        // régularisation part of the démarche need its sources (amnistie / déclaration rectificative).
        var hay = (state.Command.Situation + " " + state.ContexteFaits).ToLowerInvariant();
        if (hay.Contains("aucune retenue") || hay.Contains("sans retenue") ||
            (hay.Contains("retenue") && (hay.Contains("n'a pas") || hay.Contains("non opérée") || hay.Contains("non operee"))))
            list.Add(new("lf_regularisation",
                "Mesure LF en vigueur : amnistie / déclaration rectificative (abandon des pénalités)",
                Critical: false, TextContains: "rectificative",
                FetchKeywords: new[] { "déclaration rectificative", "amnistie", "abandon des pénalités" }));

        return list;
    }
}

/// <summary>Interest paid to a non-resident creditor.</summary>
public sealed class InteretAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.Interet;
    protected override bool     UsesOwnPrompt    => true;
    protected override string   CaseSystemPrompt => ConventionIncomeSystem;
    protected override string[] Topics => new[] { "interets", "elimination_double_imposition" };
    protected override string   Label => "Retenue à la source sur INTÉRÊTS versés à un non-résident";

    protected override string Demarche =>
        "DÉMARCHE — INTÉRÊTS versés à un créancier NON-RÉSIDENT :\n" +
        "A. QUALIFICATION & TAUX\n" +
        "   A.1 Qualifier comme INTÉRÊTS au sens de l'article « Intérêts » de la convention [Sn]\n" +
        "       (numéro propre à la convention). La convention plafonne généralement le taux de la\n" +
        "       source.\n" +
        "   A.2 Taux de droit commun : lis-le dans la ligne de l'Art.52 CIRPPIS visant les intérêts\n" +
        "       servis aux non-résidents [Sn]. Retiens le plus favorable (plafond conventionnel vs droit commun).\n" +
        "B. AUTRES OBLIGATIONS — C.1 assiette = montant brut des intérêts ; C.2 formalisme du transfert\n" +
        "   (Art.112 CDPF + circulaire BCT n°9/2016).\n";

    protected override string ForbiddenSteps =>
        "INTERDIT la séquence des prestations de services (ES chantier, RS de TVA 100%). Les intérêts\n" +
        "ne relèvent pas de la TVA — une phrase suffit si la question la soulève.";

    protected override string QualificationGuidance =>
        "Revenu = INTÉRÊTS. Dans l'Art.52, ligne des intérêts servis aux non-résidents. Dans la " +
        "convention, l'article « Intérêts » (par sujet).";

    protected override string RedactedSkeleton =>
        "Analyse\n" +
        "En application de l'article [ART] du CIRPPIS [Sn] et de l'article « Intérêts » de la convention\n" +
        "[Sn], les intérêts de source tunisienne versés à [BÉNÉFICIAIRE] font l'objet d'une retenue à la\n" +
        "source au taux de [TAUX] [Sn]. Verdict : [VERDICT].";

    protected override string JudgeCriteria =>
        "Cas INTÉRÊTS : taux lu dans la ligne intérêts de l'Art.52 et l'article « Intérêts » de la " +
        "convention ; pas de séquence ES-service ni de TVA détaillée.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            Art52("art52_interets", "CIRPPIS Art.52 (texte complet avec % — ligne des intérêts)", null),
            Cdpf112,
        };
        if (state.Countries.Count > 0)
            list.Add(new("conv_interets", "Article « Intérêts » de la convention applicable",
                Critical: true, ConventionSubject: new[] { "Intérêts", "Interets" }, ExistenceConditional: true));
        return list;
    }
}

/// <summary>Royalties (redevances) paid to a non-resident.</summary>
public sealed class RedevanceAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.Redevance;
    protected override bool     UsesOwnPrompt    => true;
    protected override string   CaseSystemPrompt => ConventionIncomeSystem;
    protected override string[] Topics => new[] { "redevances", "elimination_double_imposition" };
    protected override string   Label => "Retenue à la source sur REDEVANCES versées à un non-résident";

    protected override string Demarche =>
        "DÉMARCHE — REDEVANCES versées à un bénéficiaire NON-RÉSIDENT :\n" +
        "A. QUALIFICATION & TAUX\n" +
        "   A.1 Qualifier comme REDEVANCE au sens de l'article « Redevances » de la convention [Sn].\n" +
        "       La convention attribue à la source un droit d'imposer plafonné.\n" +
        "   A.2 Taux de droit commun : ligne de l'Art.52 CIRPPIS visant les redevances servies aux\n" +
        "       non-résidents [Sn]. Retiens le plus favorable.\n" +
        "B. TVA — une redevance pour service utilisé en Tunisie peut être taxable (Art.3) au taux de\n" +
        "   l'Art.7 [Sn], avec retenue de la TVA par le preneur (Art.19) si le prestataire n'est pas établi.\n" +
        "C. AUTRES — C.1 assiette ; C.2 formalisme du transfert (Art.112 CDPF + circulaire BCT n°9/2016).\n";

    protected override string ForbiddenSteps =>
        "N'assimile PAS une redevance à un simple bénéfice d'entreprise : l'article « Redevances » de la\n" +
        "convention prime, avec son taux plafonné propre.";

    protected override string QualificationGuidance =>
        "Revenu = REDEVANCE (usage d'un droit, marque, brevet, logiciel, savoir-faire). Article " +
        "« Redevances » de la convention (par sujet) + ligne redevances de l'Art.52.";

    protected override string RedactedSkeleton =>
        "Analyse\n" +
        "En application de l'article « Redevances » de la convention [Sn] et de l'article [ART] du CIRPPIS\n" +
        "[Sn], les redevances de source tunisienne versées à [BÉNÉFICIAIRE] font l'objet d'une retenue à la\n" +
        "source au taux de [TAUX] [Sn]. [TVA : …] Verdict : [VERDICT].";

    protected override string JudgeCriteria =>
        "Cas REDEVANCE : article « Redevances » de la convention + ligne redevances de l'Art.52 ; pas de " +
        "requalification en bénéfice d'entreprise.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            Art52("art52_redevances", "CIRPPIS Art.52 (texte complet avec % — ligne des redevances)", null),
            new("ctva_3", "CTVA Art.3 (territorialité)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "3", FetchDocFragment: "code_tva"),
            new("ctva_7", "CTVA Art.7 (taux)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "7", RequirePercent: true, FetchDocFragment: "code_tva"),
            new("ctva_19", "CTVA Art.19 (retenue de la TVA — prestataire non établi)", Critical: false,
                DocFragment: "code_tva", ArticleNumber: "19", FetchDocFragment: "code_tva"),
            Cdpf112,
        };
        if (state.Countries.Count > 0)
            list.Add(new("conv_redevances", "Article « Redevances » de la convention applicable",
                Critical: true, ConventionSubject: new[] { "Redevances" }, ExistenceConditional: true));
        return list;
    }
}

/// <summary>Services between two RESIDENT entities — the tax team's droit-commun map:
/// RS: CIRPPIS Art.45/46/49/52 + NC 3/2015 (annexe 2 = the honoraires definition that drives the
/// qualification). TVA: Art.1/3/5, taux Art.7 + tableaux A/B. NO établissement stable, NO régime
/// privilégié, NO foreign-transfer formalism — the payment never leaves Tunisia.</summary>
public sealed class RsServiceLocalAgent : CaseAgentBase
{
    public override CaseType Type => CaseType.RsServiceLocal;
    protected override bool     UsesOwnPrompt    => true;
    protected override string   CaseSystemPrompt => DomesticSystem;
    protected override string[] Topics => new[] { "remunerations_techniques", "services_professionnels" };
    protected override string   Label => "Retenue à la source — services entre entités résidentes (droit commun)";

    protected override string Demarche =>
        "DÉMARCHE — SERVICES entre deux entités RÉSIDENTES (droit commun) :\n" +
        "A. QUALIFICATION DU SERVICE — l'étape décisive :\n" +
        "   A.1 Qualifier chaque prestation au regard de la définition des HONORAIRES donnée par la\n" +
        "       note commune N°3/2015 (annexe) [Sn] : rémunérations où l'activité INTELLECTUELLE joue\n" +
        "       un rôle prépondérant (professions techniques : études, ingénierie, conseil, audit,\n" +
        "       juridique, comptable…). Applique cette définition aux prestations DES FAITS.\n" +
        "   A.2 Distinguer, le cas échéant, les prestations où l'activité intellectuelle est\n" +
        "       inexistante et qui sont facturées SÉPARÉMENT — elles relèvent de la ligne des\n" +
        "       paiements au-delà du seuil prévu par l'Art.52 pour les montants payés au titre des\n" +
        "       acquisitions de biens et services [Sn].\n" +
        "B. TAUX DE LA RS — lis dans l'Art.52 [Sn] la LIGNE correspondant à la qualification retenue\n" +
        "   ET à la qualité du bénéficiaire : pour des honoraires servis à une PERSONNE MORALE\n" +
        "   SOUMISE À L'IS, c'est la ligne du taux RÉDUIT des honoraires (pas la ligne générale des\n" +
        "   honoraires du régime réel). Le verdict peut être DOUBLE si les faits le justifient\n" +
        "   (honoraires → taux réduit ; services non intellectuels facturés séparément → ligne du seuil).\n" +
        "C. TVA — uniquement si demandée dans l'étendue : territorialité (Art.3) et taux (Art.7) [Sn].\n";

    protected override string ForbiddenSteps =>
        "INTERDIT : AUCUNE analyse d'établissement stable (les deux entités sont résidentes — la\n" +
        "notion est sans objet et ne doit même pas être évoquée). AUCUN régime fiscal privilégié.\n" +
        "AUCUNE section transfert de fonds à l'étranger (paiement domestique). AUCUNE convention\n" +
        "fiscale internationale.";

    protected override string QualificationGuidance =>
        "Prestataire ET bénéficiaire sont RÉSIDENTS en Tunisie. La question centrale est la " +
        "QUALIFICATION du service (honoraires — activité intellectuelle prépondérante au sens de la " +
        "NC 3/2015 — vs autres services), puis la LIGNE de l'Art.52 correspondant à cette " +
        "qualification et à la qualité du bénéficiaire (personne morale soumise à l'IS → ligne du " +
        "taux réduit des honoraires).";

    protected override string RedactedSkeleton =>
        "Analyse\n" +
        "En vertu de l'article 52 du CIRPPIS [Sn], la retenue à la source est applicable aux taux\n" +
        "suivants : [LIGNES APPLICABLES]. L'annexe de la note commune N°3/2015 [Sn] définit les\n" +
        "honoraires comme [DÉFINITION]. Constituent vraisemblablement des honoraires passibles de la\n" +
        "retenue au taux de [TAUX], les services de [PRESTATIONS DES FAITS], l'intervention\n" +
        "intellectuelle y étant prépondérante. Pour autant, la retenue s'effectue au taux de [TAUX]\n" +
        "pour tout paiement dépassant [SEUIL] lorsque le service se limite à des prestations où\n" +
        "l'activité intellectuelle est inexistante et qu'il est facturé de façon séparée.\n" +
        "Verdict : [VERDICT].";

    protected override string JudgeCriteria =>
        "Cas SERVICES DOMESTIQUES (droit commun) : la qualification honoraires doit être JUSTIFIÉE par " +
        "la définition de la NC 3/2015 appliquée aux prestations des faits. Le taux doit venir de la " +
        "ligne RÉDUITE des honoraires servis aux personnes morales soumises à l'IS (pas la ligne " +
        "générale du régime réel). Un double verdict (honoraires / services non intellectuels facturés " +
        "séparément) est correct si les faits le justifient. AUCUNE section établissement stable, " +
        "régime privilégié, convention ou transfert à l'étranger ne doit apparaître.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state) => new()
    {
        Art52("art52_local", "CIRPPIS Art.52 (texte complet avec % — lignes honoraires réduites et seuil acquisitions)", null),
        Nc3_2015,
        new("cirppis_45", "CIRPPIS Art.45 (champ IS)", Critical: false,
            DocFragment: "code_irpp_is", ArticleNumber: "45", FetchDocFragment: "code_irpp_is"),
        new("cirppis_49", "CIRPPIS Art.49 (taux IS)", Critical: false,
            DocFragment: "code_irpp_is", ArticleNumber: "49", FetchDocFragment: "code_irpp_is"),
        new("ctva_3", "CTVA Art.3 (territorialité)", Critical: false,
            DocFragment: "code_tva", ArticleNumber: "3", FetchDocFragment: "code_tva"),
        new("ctva_7", "CTVA Art.7 (taux)", Critical: false,
            DocFragment: "code_tva", ArticleNumber: "7", RequirePercent: true, FetchDocFragment: "code_tva"),
    };

    private const string DomesticSystem =
        "Tu es Faiez Choyakh — fiscaliste tunisien senior, EY Tunisia.\n" +
        "CITATIONS : [S1],[S2]… uniquement. Jamais de document en clair. Jamais inventer un article.\n" +
        "TAUX : LIS chaque taux DEPUIS le texte de l'article cité [Sn] et recopie le chiffre EXACT. " +
        "Jamais de taux de mémoire, jamais supposé, jamais « à vérifier ».\n" +
        "TAUX SPÉCIFIQUE (lex specialis) : dans un article de taux, applique la LIGNE correspondant " +
        "PRÉCISÉMENT à la NATURE du service (honoraires vs autres services) ET à la QUALITÉ du " +
        "bénéficiaire (personne morale soumise à l'IS) — jamais la première ligne venue, jamais la " +
        "ligne générale quand une ligne réduite spécifique s'applique.\n" +
        "HIÉRARCHIE : Codes → Lois de finances → Doctrine (notes communes).\n" +
        "VERDICTS : le taux chiffré réel lu dans [Sn] ; un double verdict est admis quand les faits " +
        "distinguent deux catégories de prestations. NON DOCUMENTÉ seulement si aucune source.\n" +
        "N'introduis AUCUNE condition non étayée par les faits. JSON PUR UNIQUEMENT.";
}
