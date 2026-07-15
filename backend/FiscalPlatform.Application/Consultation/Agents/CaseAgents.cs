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

    // The FIRST contiguous digit run, not every digit in the string concatenated — see the matching
    // fix + rationale in CaseBrief.cs's RequiredSource.Digits (same bug, same fix, kept in sync).
    private static string Digits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var start = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsDigit(s[i])) { start = i; break; }
        }
        if (start < 0) return "";
        var end = start;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return s[start..end];
    }

    // ── Shared checklist fragments (métier constants) ──

    /// <summary>CDPF Art.112 (attestation de régularisation — transfert des fonds). The taxmindvf
    /// graph had the real Art.112 text mis-stamped under the WRONG article number (an=110) while
    /// an=112 chunks were actually « 112 bis » — that data was corrected (a clean article_number=112
    /// group, header + 7 alinéas, re-imported from a graph where the chunker got it right), so this
    /// is now NUMBER-anchored via ArticleNumber and routes through the line-precise
    /// FetchArticleLinesAsync fetcher. TextContains is kept as an extra guard against ever picking
    /// up a stray un-migrated copy. French CDPF editions stop at 2025 (2026 is Arabic-only).</summary>
    protected static RequiredSource Cdpf112 => new(
        Key: "cdpf_112", Critical: true,
        Description: "Art.112 CDPF (attestation de régularisation, transfert des fonds) + décret d'application",
        DocFragment: "code_droits_procedures", ArticleNumber: "112",
        TextContains: "régularisation de leur situation fiscale",
        FetchDocFragment: "code_droits_procedures",
        FetchKeywords: new[] { "régularisation de leur situation fiscale", "attestation", "article 112" });

    /// <summary>Circulaire de la BCT aux intermédiaires agréés N°2016-9 (transferts au titre des
    /// opérations courantes) — the concrete BCT text governing the transfer-of-funds formalism that
    /// CDPF Art.112 refers to. Art.21 is the provision the tax team actually cites for the
    /// justificatifs required when a Tunisian debtor transfers taxable income/profits abroad.
    /// Previously absent from the corpus entirely (writer-facing text forbade naming a circulaire
    /// because none was ever grounded); now a real, retrievable document — number-anchored.</summary>
    protected static RequiredSource BctCirculaire => new(
        Key: "bct_circulaire_21", Critical: false,
        Description: "Circulaire BCT N°2016-9, Art.21 (justificatifs du transfert de fonds à l'étranger)",
        DocFragment: "circulaire_bct", ArticleNumber: "21",
        FetchDocFragment: "circulaire_bct",
        FetchKeywords: new[] { "règlements au titre des opérations courantes", "transfert", "justificatif" });

    /// <summary>NC 14/2013 — the Note Commune entirely dedicated to commenting CDPF Art.112:
    /// the best-grounded doctrine for the transfer-formalism section (present in both graphs).</summary>
    protected static RequiredSource Nc112Doctrine => new(
        Key: "nc112_doctrine", Critical: false,
        Description: "Note commune N°14/2013 (commentaire de l'article 112 CDPF)",
        DocFragment: "NC_2013_14",
        FetchDocFragment: "NC_2013_14",
        FetchKeywords: new[] { "régularisation", "transfert", "attestation" });

    /// <summary>NC 3/2015 — assiette de la RS (montant brut TVA comprise).</summary>
    protected static RequiredSource Nc3_2015 => new(
        Key: "nc3_2015", Critical: true,
        Description: "Note commune N°3/2015 (assiette de la retenue à la source)",
        DocFragment: "NC_2015_03",
        // Scope the keyword fetch to NC_2015_03 — without this the fetch searched ALL docs and the
        // assiette note lost its slot to unrelated 'honoraires' chunks (NC_2002_12…), so the writer
        // attributed the « montant brut TVA comprise » rule to Art.52 instead of NC 3/2015.
        FetchDocFragment: "NC_2015_03",
        FetchKeywords: new[] { "assiette de la retenue", "montant brut", "toute taxe comprise" });

    /// <summary>CIRPPIS Art.52 — the multi-rate menu. TextContains narrows to the LINE this case
    /// reads (métier: 'right rate, wrong case' = picking the wrong line).</summary>
    protected static RequiredSource Art52(string key, string description, string? lineContains) => new(
        Key: key, Critical: true, Description: description,
        DocFragment: "code_irpp_is", ArticleNumber: "52",
        RequirePercent: true, TextContains: lineContains,
        FetchDocFragment: "code_irpp_is");

    /// <summary>CTVA Art.7 (taux normal de TVA). ANCHORED ON CONTENT, not the article number:
    /// on taxmindvf, article_number='7' in code_tva is NON-UNIQUE — the code compilation bundles
    /// dozens of unrelated « Article 7 » (application décrets, annexed rate lists), so fetching by
    /// number returns a polluted blob and the real 19% line drowns → "taux NON DOCUMENTÉ". The
    /// phrase « à la taxe sur la valeur ajoutée au taux » is the unique signature of the operative
    /// rate clause; the rate value itself is still READ from the retrieved text, never hardcoded.
    /// Critical overridable for the domestic/RS-local case where TVA is only conditionally in scope.</summary>
    protected static RequiredSource Ctva7(bool critical = true) => new(
        Key: "ctva_7", Critical: critical,
        Description: "CTVA Art.7 (taux normal de la TVA)",
        DocFragment: "code_tva", TextContains: "valeur ajoutée au taux", RequirePercent: true,
        FetchDocFragment: "code_tva",
        FetchKeywords: new[] { "soumis à la taxe sur la valeur ajoutée au taux", "valeur ajoutée au taux" });

    // Slim system prompt shared by the convention-income agents (dividende/intérêt/redevance): the
    // universal anti-hallucination + citation rules WITHOUT the foreign-service séquence that must
    // never be imposed on a dividend/interest/royalty.
    protected const string ConventionIncomeSystem =
        "Tu est un expert — fiscaliste tunisien senior, EY Tunisia.\n" +
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
            Ctva7(),
            Cdpf112,
            Nc112Doctrine,
            BctCirculaire,
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
        "Cas SERVICE / FOURNISSEUR ÉTRANGER. RÈGLE PRIORITAIRE : si le pays du bénéficiaire figure dans la " +
        "liste des États à régime fiscal privilégié [Sn], l'établissement stable NE doit PAS être analysé ni " +
        "même mentionné — sa présence dans ce cas est une faute ; on passe directement à la RS. Sinon " +
        "(pays sans régime privilégié), l'ES est tranché d'abord en droit commun puis selon l'Art.5 de la " +
        "convention. La RS applique la ligne de l'Art.52 visant les « non domiciliés ni établis » (non les " +
        "honoraires-résidents). TVA due par retenue de 100% du preneur. Ne doivent pas manquer : l'assiette " +
        "(montant brut TVA comprise) et le formalisme du transfert des fonds à l'étranger — Art.112 CDPF ET " +
        "Art.21 de la circulaire BCT N°2016-9 (les deux ensemble) lorsqu'ils figurent dans les sources [Sn], " +
        "avec les conditions d'exonération du certificat de régularisation prévues par l'Art.112.";

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
            Ctva7(),
            new("ctva_19", "CTVA Art.19 (retenue de 100% de la TVA — prestataire non établi)", Critical: true,
                DocFragment: "code_tva", ArticleNumber: "19", TextContains: "réalisation par les personnes morales",
                FetchDocFragment: "code_tva"),
            // The privileged-regime LIST lives in NC 16/2019 (the arrêté reproduced there names the
            // countries, e.g. Hong Kong). Target that document directly — the old generic keyword
            // fetch (doc='') pulled random chunks and never the list, so the writer said "aucune
            // mention de Hong Kong" and the deterministic ES-ban (which needs this list present) never
            // fired. Satisfied by any NC_2019_16 chunk naming a privileged regime.
            new("regime_privilegie", "Note commune N°16/2019 — liste des États/territoires à régime fiscal privilégié",
                Critical: false, DocFragment: "NC_2019_16",
                FetchDocFragment: "NC_2019_16",
                FetchKeywords: new[] { "Gibraltar", "Guernesey", "Hong Kong", "Île de Man", "régime fiscal privilégié" }
                    .Concat(state.Countries.Take(3))
                    .ToArray()),
            Cdpf112,
            Nc112Doctrine,
            BctCirculaire,
        };

        // Convention country → the treaty ES article (by SUBJECT, accent-safe) + NC 2/2015 — the
        // métier exception: NC 2/2015 is NOT used for Allemagne (superseded by the newer convention).
        if (state.Countries.Count > 0)
        {
            list.Add(new("conv_es", "Article « Établissement stable » de la convention applicable",
                Critical: true, ConventionSubject: new[] { "tablissement stable" }, ExistenceConditional: true));
            // Treaty « Redevances » article — needed so the writer can TEST (and usually exclude) the
            // redevance qualification of a technical-service payment BEFORE defaulting to « bénéfices
            // d'entreprise » (Art.7). Non-critical + existence-conditional: many services aren't
            // redevances and some treaties/countries have no such article (or no treaty at all, e.g. HK).
            list.Add(new("conv_redevances_test",
                "Article « Redevances » de la convention (pour écarter/retenir la qualification redevance du service)",
                Critical: false, ConventionSubject: new[] { "Redevances", "Redevance" }, ExistenceConditional: true));
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
        "   A.2 Taux de RS de droit commun — QUALIFICATION DÉCISIVE : un dividende est un « REVENU\n" +
        "       DISTRIBUÉ ». Lis le taux dans la LIGNE de l'Art.52 CIRPPIS visant EXPRESSÉMENT les\n" +
        "       « revenus distribués » [Sn]. NE JAMAIS le qualifier de « revenus de capitaux\n" +
        "       mobiliers » : cette ligne-là vise d'AUTRES revenus (intérêts, etc.) et son taux ne\n" +
        "       s'applique PAS aux dividendes — la retenir est l'erreur de qualification type.\n" +
        "       Ne pas retenir non plus les lignes honoraires/services. Retiens ensuite le\n" +
        "       traitement le plus favorable entre plafond conventionnel et droit commun.\n" +
        "B. RISQUE & RÉGULARISATION (uniquement si les dividendes ont été servis SANS retenue)\n" +
        "   • Quantifier, à partir du montant distribué et du taux lu : la RS non opérée par prise\n" +
        "     en charge (formule montant × taux / (100 − taux)), les pénalités de retard, la\n" +
        "     pénalité fixe éventuelle. Rappeler la prescription.\n" +
        "   • Mentionner toute mesure d'amnistie / dépôt de déclaration rectificative prévue par la\n" +
        "     loi de finances en vigueur si elle est retrouvée dans les sources [Sn].\n" +
        "C. TRANSFERT DES FONDS À L'ÉTRANGER & FORMALISME (à traiter EN DÉTAIL dès que l'étendue\n" +
        "   porte sur le transfert en devises ou le certificat de régularisation)\n" +
        "   C.1 Assiette = montant brut des dividendes distribués.\n" +
        "   C.2 Le transfert en devises des dividendes vers le non-résident est régi CONJOINTEMENT par\n" +
        "       l'Art.112 du CDPF [Sn] et l'Art.21 de la circulaire BCT N°2016-9 [Sn] — les DEUX textes\n" +
        "       du transfert de fonds à l'étranger : les citer ENSEMBLE lorsqu'ils sont fournis.\n" +
        "   C.3 L'Art.112 CDPF subordonne le transfert à un CERTIFICAT DE RÉGULARISATION de la situation\n" +
        "       fiscale, SAUF si l'une de ses conditions d'exonération est remplie — LIS-les dans le texte\n" +
        "       cité [Sn] : revenus exonérés ; revenus hors champ ; revenus AYANT DÉJÀ FAIT L'OBJET DE LA\n" +
        "       RETENUE À LA SOURCE (sur présentation du certificat de retenue) ; non-résidents au sens de\n" +
        "       la réglementation de change. Applique aux FAITS la condition pertinente et conclus\n" +
        "       (ex. : si la RS a déjà été opérée et un certificat de retenue établi, le transfert peut se\n" +
        "       faire sur ce seul certificat, sans certificat de régularisation distinct). Cite UNIQUEMENT\n" +
        "       les textes réellement fournis [Sn] — n'invente ni numéro d'article ni numéro de circulaire.\n";

    protected override string ForbiddenSteps =>
        "INTERDIT ABSOLU — ÉTABLISSEMENT STABLE : ne JAMAIS évoquer, mentionner ni analyser\n" +
        "l'établissement stable — ni en droit interne, ni au sens de la convention (Art.4/5) — même\n" +
        "d'une seule phrase, même pour dire qu'il n'y en a pas, même pour écarter la détention du\n" +
        "capital. Un dividende est un revenu de capitaux mobiliers : la notion d'établissement stable\n" +
        "est SANS OBJET et ne doit apparaître NULLE PART (ni dans l'analyse, ni dans un verdict, ni\n" +
        "dans le tableau de synthèse). Sa seule présence est une faute.\n" +
        "INTERDIT aussi : la séquence des prestations de services (ES de chantier, retenue de TVA de\n" +
        "100% par le preneur). Les dividendes sont HORS CHAMP de la TVA : si la TVA est demandée, une\n" +
        "seule phrase suffit (hors champ).";

    protected override string QualificationGuidance =>
        "Revenu = DIVIDENDE (distribution de bénéfices à l'actionnaire non-résident). Qualification " +
        "fiscale tunisienne : « REVENUS DISTRIBUÉS » — jamais « revenus de capitaux mobiliers », qui " +
        "est une catégorie DIFFÉRENTE de l'Art.52 visant d'autres revenus. Dans l'Art.52 CIRPPIS, la " +
        "ligne applicable est exclusivement celle visant expressément les « revenus distribués ». Dans " +
        "la convention, c'est l'article intitulé « Dividendes » (retrouvé par sujet, quel que soit son numéro).";

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
        "Cas DIVIDENDE : le taux doit être lu dans la ligne « revenus distribués » de l'Art.52. Toute " +
        "qualification des dividendes en « revenus de capitaux mobiliers » est une ERREUR DE " +
        "QUALIFICATION rédhibitoire (catégorie différente, taux différent) — rejeter le projet. Idem " +
        "pour les lignes honoraires/services. AUCUNE mention d'établissement stable où que ce soit " +
        "(analyse, verdict, tableau) — sa seule présence est une faute rédhibitoire. AUCUNE section TVA " +
        "détaillée (dividendes hors champ). Si les dividendes ont été servis sans retenue, le risque " +
        "(prise en charge, pénalités) doit être quantifié. L'article « Dividendes » de la convention " +
        "doit être visé. Dès que l'étendue porte sur le TRANSFERT DES FONDS à l'étranger, l'Art.112 " +
        "CDPF (avec ses conditions d'exonération du certificat de régularisation) ET l'Art.21 de la " +
        "circulaire BCT N°2016-9 doivent être visés s'ils figurent dans les sources [Sn].";

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
            Nc112Doctrine,
            BctCirculaire,
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
        "DÉMARCHE — INTÉRÊTS versés à un créancier NON-RÉSIDENT (le « régime fiscal » des intérêts\n" +
        "couvre TOUJOURS : RS + TVA + déductibilité le cas échéant + formalisme — même si l'étendue\n" +
        "ne détaille pas chaque impôt) :\n\n" +
        "A. QUALIFICATION & TAUX DE RS — démarche en deux temps :\n" +
        "   A.1 Qualifier comme INTÉRÊTS au sens de l'article « Intérêts » de la convention [Sn]\n" +
        "       (numéro propre à la convention). La convention plafonne généralement le taux de la\n" +
        "       source.\n" +
        "   A.2 Droit commun — le taux de RS dépend de la QUALIFICATION selon l'Art.48-VII du\n" +
        "       CIRPPIS [Sn]. LIS dans le texte de l'Art.48-VII les conditions de déductibilité et\n" +
        "       détermine la part DÉDUCTIBLE vs NON-DÉDUCTIBLE :\n" +
        "       — la part DÉDUCTIBLE de l'assiette de l'IS constitue des « revenus de capitaux\n" +
        "         mobiliers » → lis le taux de RS correspondant dans l'Art.52 CIRPPIS [Sn] ;\n" +
        "       — la part NON-DÉDUCTIBLE constitue des « revenus de valeurs mobilières » → lis le\n" +
        "         taux de RS correspondant dans l'Art.52 CIRPPIS [Sn].\n" +
        "       Les causes de non-déductibilité prévues par l'Art.48-VII [Sn] sont :\n" +
        "         (i)  les intérêts supportés lorsque le capital social n'est pas entièrement libéré ;\n" +
        "         (ii) la fraction des intérêts calculée sur la base du taux excédentaire par rapport\n" +
        "              au taux-plafond fixé par le même article — LIS ce taux-plafond DANS le texte\n" +
        "              [Sn], ne le cite JAMAIS de mémoire ;\n" +
        "         (iii) les intérêts afférents aux montants de prêts excédant le plafond par rapport\n" +
        "               au capital social fixé par le même article — LIS ce plafond DANS le texte\n" +
        "               [Sn], ne le cite JAMAIS de mémoire.\n" +
        "       APPLIQUE ces conditions aux faits (capital libéré ? taux convenu ? montant du prêt\n" +
        "       vs capital ?) et conclus sur la ventilation déductible / non-déductible.\n" +
        "   A.3 Retiens le plus favorable entre le plafond conventionnel et le droit commun.\n\n" +
        "B. TVA — À TRAITER SYSTÉMATIQUEMENT :\n" +
        "   B.1 COMMENCE par le tableau « B » annexé au CTVA [Sn] : repère si « les intérêts\n" +
        "       débiteurs » (item 14 du paragraphe II) y figurent. SI OUI, le taux applicable est\n" +
        "       celui indiqué dans le TITRE du tableau B (« …soumis à la TVA au taux de __% »)\n" +
        "       — recopie ce taux EXACT, c'est le taux réduit, PAS le taux normal de l'Art.7.\n" +
        "   B.2 L'Art.7 du CTVA [Sn] confirme : les opérations reprises au tableau « B » sont\n" +
        "       soumises au taux indiqué par ce tableau — PAS au taux normal. Ne retiens le taux\n" +
        "       normal de l'Art.7 que pour les opérations qui NE figurent PAS au tableau B.\n" +
        "   B.3 Territorialité : détermine si l'opération entre dans le champ de la TVA à partir\n" +
        "       du texte du CTVA [Sn] (services consommés / utilisés en Tunisie).\n" +
        "   B.4 Si la TVA est due et que le prêteur n'est pas établi en Tunisie, le bénéficiaire\n" +
        "       tunisien retient la TVA due [Sn]. Si le bénéficiaire est totalement exportateur,\n" +
        "       vérifier le régime de suspension de TVA applicable [Sn].\n" +
        "   Ne conclus JAMAIS sur la TVA sans un [Sn] à l'appui.\n\n" +
        "C. FORMALISME & AUTRES OBLIGATIONS — C.1 assiette = montant brut des intérêts ;\n" +
        "   C.2 formalisme du transfert (certificat de retenue à la source, Art.112 CDPF [Sn] ; si\n" +
        "   l'Art.21 de la circulaire BCT N°2016-9 figure parmi les sources [Sn], vise-le pour les\n" +
        "   justificatifs exigés — cite uniquement les textes réellement fournis [Sn]).\n";

    protected override string ForbiddenSteps =>
        "INTERDIT ABSOLU — ÉTABLISSEMENT STABLE : ne JAMAIS évoquer, mentionner ni analyser\n" +
        "l'établissement stable — ni en droit interne, ni au sens de la convention (Art.5/7) — même\n" +
        "d'une phrase, même pour l'écarter. Un intérêt est un revenu de créance : la notion d'ES est\n" +
        "SANS OBJET et ne doit apparaître NULLE PART (ni analyse, ni verdict, ni tableau).\n" +
        "INTERDIT aussi : la séquence des prestations de services (ES de chantier, présence de\n" +
        "personnel). Le point TVA se traite sur la base des textes du CTVA fournis [Sn], jamais par\n" +
        "affirmation non sourcée.\n" +
        "INTERDIT : citer de mémoire le taux-plafond ou le ratio capital de l'Art.48-VII — ces\n" +
        "seuils DOIVENT être LUS dans le texte de l'article fourni [Sn] et recopiés EXACTEMENT.";

    protected override string QualificationGuidance =>
        "Revenu = INTÉRÊTS (rémunération d'une créance/prêt). La qualification fiscale en droit commun " +
        "dépend de la déductibilité au sens de l'Art.48-VII CIRPPIS : la part déductible = « revenus de " +
        "capitaux mobiliers » ; la part non-déductible = « revenus de valeurs mobilières ». Chaque " +
        "catégorie a son propre taux de RS dans l'Art.52 CIRPPIS. Dans la convention, l'article " +
        "« Intérêts » (retrouvé par sujet, numéro variable) plafonne le taux.";

    protected override string RedactedSkeleton =>
        "Analyse\n" +
        "En application de l'article 48-VII du CIRPPIS [Sn], la déductibilité des intérêts est examinée.\n" +
        "La part déductible constitue des revenus de capitaux mobiliers soumis à la RS au taux de [TAUX]\n" +
        "lu dans l'Art.52 [Sn]. La part non-déductible constitue des revenus de valeurs mobilières\n" +
        "soumis au taux de [TAUX] lu dans l'Art.52 [Sn]. L'article « Intérêts » de la convention [Sn]\n" +
        "plafonne le taux à [TAUX]. Verdict : [VERDICT].";

    protected override string JudgeCriteria =>
        "Cas INTÉRÊTS — critères de rejet :\n" +
        "1. DÉDUCTIBILITÉ & DOUBLE QUALIFICATION : l'analyse DOIT examiner les conditions de " +
        "déductibilité de l'Art.48-VII CIRPPIS [Sn] et ventiler la part déductible (« revenus de " +
        "capitaux mobiliers ») vs la part non-déductible (« revenus de valeurs mobilières »). " +
        "Chaque catégorie → son propre taux de RS lu dans l'Art.52 CIRPPIS [Sn]. L'absence de cette " +
        "ventilation, ou un taux unique appliqué sans distinguer, justifie le rejet.\n" +
        "2. TVA : les « intérêts débiteurs » figurent au tableau « B » du CTVA [Sn] — le taux applicable " +
        "est donc le taux RÉDUIT du tableau B (lu dans son titre), PAS le taux normal de l'Art.7. " +
        "Appliquer le taux normal (ex. 19%) aux intérêts alors qu'ils figurent au tableau B est une " +
        "ERREUR rédhibitoire — rejeter le projet. La retenue de la TVA par le bénéficiaire tunisien " +
        "doit être mentionnée si le prêteur est non-établi. Un verdict TVA sans citations justifie le rejet.\n" +
        "3. ÉTABLISSEMENT STABLE : AUCUNE mention où que ce soit — sa seule présence est une " +
        "faute rédhibitoire.\n" +
        "4. CONVENTION : le plafond conventionnel de l'article « Intérêts » doit être lu [Sn] et " +
        "comparé au droit commun — retenir le plus favorable.\n" +
        "5. FORMALISME : Art.112 CDPF et Art.21 de la circulaire BCT N°2016-9 visés ensemble " +
        "s'ils figurent dans les sources.";

    protected override List<RequiredSource> BuildChecklist(ConsultationState state)
    {
        var list = new List<RequiredSource>
        {
            Art52("art52_interets", "CIRPPIS Art.52 (texte complet avec % — lignes capitaux mobiliers ET valeurs mobilières)", null),
            new("cirppis_48_interets",
                "CIRPPIS Art.48-VII — conditions de déductibilité des intérêts (plafonds, taux-limite, libération du capital)",
                Critical: true, DocFragment: "code_irpp_is", ArticleNumber: "48",
                TextContains: "intérêts", RequirePercent: true,
                FetchDocFragment: "code_irpp_is"),
            Ctva7(),
            new("ctva_tableau_b_interets",
                "CTVA Tableau « B » — item 14 : les intérêts débiteurs (taux réduit du tableau B)",
                Critical: true, DocFragment: "code_tva", TextContains: "intérêts débiteurs",
                FetchDocFragment: "code_tva",
                FetchKeywords: new[] { "intérêts débiteurs", "radio-télédiffusion", "location navires", "projection films" }),
            new("ctva_champ_interets", "CTVA — champ d'application et territorialité (services consommés en Tunisie)",
                Critical: false, DocFragment: "code_tva", TextContains: "territoire",
                FetchDocFragment: "code_tva",
                FetchKeywords: new[] { "territoire", "consommé", "utilisé en Tunisie", "retenue", "prestataire non résident" }),
            Cdpf112,
            Nc112Doctrine,
            BctCirculaire,
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
        "C. AUTRES — C.1 assiette ; C.2 formalisme du transfert (certificat de retenue, Art.112 CDPF [Sn] ;\n" +
        "   si l'Art.21 de la circulaire BCT N°2016-9 figure parmi les sources [Sn], vise-le pour les\n" +
        "   justificatifs exigés — cite uniquement les textes réellement fournis [Sn]).\n";

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
            Ctva7(),
            new("ctva_19", "CTVA Art.19 (retenue de la TVA — prestataire non établi)", Critical: false,
                DocFragment: "code_tva", ArticleNumber: "19", TextContains: "réalisation par les personnes morales",
                FetchDocFragment: "code_tva"),
            Cdpf112,
            Nc112Doctrine,
            BctCirculaire,
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
        Ctva7(critical: false),
    };

    private const string DomesticSystem =
        "Tu est un expert — fiscaliste tunisien senior, EY Tunisia.\n" +
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
