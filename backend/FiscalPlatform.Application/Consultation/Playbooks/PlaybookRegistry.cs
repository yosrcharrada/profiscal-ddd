namespace FiscalPlatform.Application.Consultation.Playbooks;

/// <summary>
/// Resolves a <see cref="CaseType"/> to its <see cref="CasePlaybook"/>. Definitions live here as
/// data. Generic and RsServiceForeign deliberately carry NO own prompt — they keep the legacy,
/// proven service flow (year-fixed to 2026), so the working Hong-Kong / Italy runs never regress.
/// Dividende / Interet / Redevance supply their own specialised démarche.
/// </summary>
public static class PlaybookRegistry
{
    // Switch (evaluated at call time) rather than a static dictionary: a static field-initialised
    // dictionary would capture the playbook fields BEFORE they are assigned (static init runs in
    // textual order), mapping every CaseType to null.
    public static CasePlaybook Get(CaseType type) => type switch
    {
        CaseType.RsServiceForeign => RsServiceForeign,
        CaseType.Dividende        => Dividende,
        CaseType.Interet          => Interet,
        CaseType.Redevance        => Redevance,
        _                         => Generic,
    };

    // ── Legacy path: empty SystemPrompt/Demarche → handler uses the existing BuildPhase2Prompt. ──
    private static readonly CasePlaybook Generic = new(
        Type: CaseType.Generic, Label: "Cas général / prestation internationale",
        Topics: new[] { "remunerations_techniques", "benefices_entreprises", "etablissement_stable" },
        BackstopArticlesCirppis: new[] { "45", "47", "52", "53" },
        BackstopArticlesTva: new[] { "1", "3", "5", "7", "19" },
        BackstopArticlesCdpf: new[] { "112" },
        TreatySubjects: new[] { "Etablissement stable", "Benefices des entreprises" },
        NeedsConventionArticle: true,
        SystemPrompt: "", Demarche: "", ForbiddenSteps: "", QualificationGuidance: "",
        RedactedSkeleton: "", JudgeCriteria: "");

    private static readonly CasePlaybook RsServiceForeign = Generic with
    {
        Type  = CaseType.RsServiceForeign,
        Label = "Retenue à la source — fournisseur étranger de services",
        Topics = new[] { "remunerations_techniques", "services_professionnels",
                         "etablissement_stable", "benefices_entreprises" },
        QualificationGuidance =
            "Le bénéficiaire est un FOURNISSEUR ÉTRANGER non-résident non établi. Dans l'article de " +
            "taux de RS (CIRPPIS Art.52), la ligne applicable est celle qui vise EXPRESSÉMENT les " +
            "« rémunérations et revenus servis aux non domiciliés ni établis » — PAS la ligne des " +
            "honoraires servis aux résidents soumis au régime réel. Lis le taux de CETTE ligne.",
    };

    // ── Dividende: distinct démarche. NO ES-of-service, NO TVA section. ──
    private static readonly CasePlaybook Dividende = new(
        Type: CaseType.Dividende,
        Label: "Retenue à la source sur DIVIDENDES versés à un non-résident",
        Topics: new[] { "dividendes", "elimination_double_imposition", "obligations_societes" },
        BackstopArticlesCirppis: new[] { "52", "53", "29" },
        BackstopArticlesTva: System.Array.Empty<string>(),
        BackstopArticlesCdpf: new[] { "112" },
        TreatySubjects: new[] { "Dividendes" },
        NeedsConventionArticle: true,
        SystemPrompt: ConventionIncomeSystem,
        Demarche:
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
            "       (certificat de retenue à la source) ; obligations déclaratives de la société distributrice.\n",
        ForbiddenSteps:
            "INTERDIT : ne PAS dérouler la séquence des prestations de services (établissement stable\n" +
            "de type chantier/présence, retenue de TVA de 100% par le preneur). Les dividendes sont\n" +
            "HORS CHAMP de la TVA : si la TVA est demandée, une seule phrase suffit (hors champ). " +
            "L'établissement stable ne se discute que pour dire, brièvement, que la simple détention du\n" +
            "capital n'en crée pas un.",
        QualificationGuidance:
            "Revenu = DIVIDENDE (distribution de bénéfices à l'actionnaire non-résident). Dans l'Art.52 " +
            "CIRPPIS, la ligne applicable est celle des « revenus distribués ». Dans la convention, c'est " +
            "l'article intitulé « Dividendes » (retrouvé par sujet, quel que soit son numéro).",
        RedactedSkeleton:
            "Analyse\n" +
            "En application des dispositions combinées de l'article [ART] du CIRPPIS [Sn] et de l'article\n" +
            "« Dividendes » de la convention [Sn], les dividendes de source tunisienne versés à [BÉNÉFICIAIRE]\n" +
            "sont imposables en Tunisie et font l'objet d'une retenue à la source au taux de [TAUX] [Sn].\n" +
            "[Si servis sans retenue] Dès lors qu'aucune retenue n'a été opérée, la société distributrice\n" +
            "risque, en cas de contrôle, la réclamation de la retenue non opérée par prise en charge\n" +
            "([FORMULE]) soit [MONTANT], des pénalités de retard de [TAUX] soit [MONTANT], et d'une pénalité\n" +
            "fixe de [TAUX] soit [MONTANT]. [Le cas échéant, une régularisation est possible dans le cadre de\n" +
            "[MESURE LF] [Sn].]\n" +
            "Verdict : [VERDICT].",
        JudgeCriteria:
            "Cas DIVIDENDE : le taux doit être lu dans la ligne « revenus distribués » de l'Art.52 (et non " +
            "honoraires/services). AUCUNE séquence d'établissement stable de prestation ni section TVA " +
            "détaillée (dividendes hors champ). Si les dividendes ont été servis sans retenue, le risque " +
            "(prise en charge, pénalités) doit être quantifié. L'article « Dividendes » de la convention " +
            "doit être visé.");

    // ── Interest: convention interest article + domestic rate. No ES-of-service, no TVA. ──
    private static readonly CasePlaybook Interet = new(
        Type: CaseType.Interet,
        Label: "Retenue à la source sur INTÉRÊTS versés à un non-résident",
        Topics: new[] { "interets", "elimination_double_imposition" },
        BackstopArticlesCirppis: new[] { "52", "53" },
        BackstopArticlesTva: System.Array.Empty<string>(),
        BackstopArticlesCdpf: new[] { "112" },
        TreatySubjects: new[] { "Interets" },
        NeedsConventionArticle: true,
        SystemPrompt: ConventionIncomeSystem,
        Demarche:
            "DÉMARCHE — INTÉRÊTS versés à un créancier NON-RÉSIDENT :\n" +
            "A. QUALIFICATION & TAUX\n" +
            "   A.1 Qualifier comme INTÉRÊTS au sens de l'article « Intérêts » de la convention [Sn]\n" +
            "       (numéro propre à la convention). La convention plafonne généralement le taux de la\n" +
            "       source.\n" +
            "   A.2 Taux de droit commun : lis-le dans la ligne de l'Art.52 CIRPPIS visant les intérêts\n" +
            "       servis aux non-résidents [Sn]. Retiens le plus favorable (plafond conventionnel vs droit commun).\n" +
            "B. AUTRES OBLIGATIONS — C.1 assiette = montant brut des intérêts ; C.2 formalisme du transfert\n" +
            "   (Art.112 CDPF + circulaire BCT n°9/2016).\n",
        ForbiddenSteps:
            "INTERDIT la séquence des prestations de services (ES chantier, RS de TVA 100%). Les intérêts\n" +
            "ne relèvent pas de la TVA — une phrase suffit si la question la soulève.",
        QualificationGuidance:
            "Revenu = INTÉRÊTS. Dans l'Art.52, ligne des intérêts servis aux non-résidents. Dans la " +
            "convention, l'article « Intérêts » (par sujet).",
        RedactedSkeleton:
            "Analyse\n" +
            "En application de l'article [ART] du CIRPPIS [Sn] et de l'article « Intérêts » de la convention\n" +
            "[Sn], les intérêts de source tunisienne versés à [BÉNÉFICIAIRE] font l'objet d'une retenue à la\n" +
            "source au taux de [TAUX] [Sn]. Verdict : [VERDICT].",
        JudgeCriteria:
            "Cas INTÉRÊTS : taux lu dans la ligne intérêts de l'Art.52 et l'article « Intérêts » de la " +
            "convention ; pas de séquence ES-service ni de TVA détaillée.");

    // ── Royalties: treaty royalties article (Art.12 model) + Art.52. ──
    private static readonly CasePlaybook Redevance = new(
        Type: CaseType.Redevance,
        Label: "Retenue à la source sur REDEVANCES versées à un non-résident",
        Topics: new[] { "redevances", "elimination_double_imposition" },
        BackstopArticlesCirppis: new[] { "52", "53" },
        BackstopArticlesTva: new[] { "1", "3", "5", "7", "19" },
        BackstopArticlesCdpf: new[] { "112" },
        TreatySubjects: new[] { "Redevances" },
        NeedsConventionArticle: true,
        SystemPrompt: ConventionIncomeSystem,
        Demarche:
            "DÉMARCHE — REDEVANCES versées à un bénéficiaire NON-RÉSIDENT :\n" +
            "A. QUALIFICATION & TAUX\n" +
            "   A.1 Qualifier comme REDEVANCE au sens de l'article « Redevances » de la convention [Sn].\n" +
            "       La convention attribue à la source un droit d'imposer plafonné.\n" +
            "   A.2 Taux de droit commun : ligne de l'Art.52 CIRPPIS visant les redevances servies aux\n" +
            "       non-résidents [Sn]. Retiens le plus favorable.\n" +
            "B. TVA — une redevance pour service utilisé en Tunisie peut être taxable (Art.3) au taux de\n" +
            "   l'Art.7 [Sn], avec retenue de la TVA par le preneur (Art.19) si le prestataire n'est pas établi.\n" +
            "C. AUTRES — C.1 assiette ; C.2 formalisme du transfert (Art.112 CDPF + circulaire BCT n°9/2016).\n",
        ForbiddenSteps:
            "N'assimile PAS une redevance à un simple bénéfice d'entreprise : l'article « Redevances » de la\n" +
            "convention prime, avec son taux plafonné propre.",
        QualificationGuidance:
            "Revenu = REDEVANCE (usage d'un droit, marque, brevet, logiciel, savoir-faire). Article " +
            "« Redevances » de la convention (par sujet) + ligne redevances de l'Art.52.",
        RedactedSkeleton:
            "Analyse\n" +
            "En application de l'article « Redevances » de la convention [Sn] et de l'article [ART] du CIRPPIS\n" +
            "[Sn], les redevances de source tunisienne versées à [BÉNÉFICIAIRE] font l'objet d'une retenue à la\n" +
            "source au taux de [TAUX] [Sn]. [TVA : …] Verdict : [VERDICT].",
        JudgeCriteria:
            "Cas REDEVANCE : article « Redevances » de la convention + ligne redevances de l'Art.52 ; pas de " +
            "requalification en bénéfice d'entreprise.");

    // Slim base for convention-income cases (dividende/intérêt/redevance): keeps the universal
    // anti-hallucination + citation rules of the legacy prompt WITHOUT the foreign-service séquence
    // (ES → RS → TVA → assiette → transfert) that must not be imposed on a dividend.
    private const string ConventionIncomeSystem =
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
