using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Playbooks;
using Microsoft.Extensions.Logging;
using H = FiscalPlatform.Application.Consultation.Commands.GenerateConsultation.GenerateConsultationCommandHandler;

namespace FiscalPlatform.Application.Consultation.Agents;

/// <summary>
/// Shared mechanics for every case agent: run Phase-2, parse, revise, and (for convention-income
/// cases) acquire + pin the treaty income article and the domestic rate article. Everything that is
/// SPECIFIC to a case — its démarche, its forbidden steps, its qualification, its structure skeleton,
/// its source subjects, its judge criteria — is declared IN the concrete agent below, as readable
/// code, not pulled from a data table. The base is plumbing; the agents are the brains.
/// </summary>
public abstract class CaseAgentBase : ICaseAgent
{
    protected readonly ILlmAgent       Llm;
    protected readonly IRetrievalAgent Retrieval;
    protected readonly ILogger         Logger;

    protected CaseAgentBase(ILlmAgent llm, IRetrievalAgent retrieval, ILogger logger)
    {
        Llm = llm; Retrieval = retrieval; Logger = logger;
    }

    public abstract CaseType Type { get; }

    // ── Per-case content hooks (defaults = the legacy foreign-service flow) ──
    protected virtual string   Label                 => Type.ToString();
    /// <summary>False → use the proven legacy Phase-2 builder (Generic / RS-service). True → this
    /// agent builds its own specialised prompt from the hooks below.</summary>
    protected virtual bool     UsesOwnPrompt         => false;
    protected virtual string   CaseSystemPrompt      => H.SystemPrompt;
    protected virtual string[] Topics                => System.Array.Empty<string>();
    protected virtual string[] TreatySubjects        => System.Array.Empty<string>();
    protected virtual bool     NeedsConventionArticle => false;
    protected virtual string   Demarche              => "";
    protected virtual string   ForbiddenSteps        => "";
    protected virtual string   QualificationGuidance => "";
    protected virtual string   RedactedSkeleton      => "";
    protected virtual string   JudgeCriteria         => "";

    public virtual async Task<CaseDraft> DraftAsync(CaseContext ctx, CancellationToken ct = default)
    {
        var sources = ctx.Sources;   // same reference the handler holds — augmentations are visible

        if (UsesOwnPrompt && NeedsConventionArticle && TreatySubjects.Length > 0 && ctx.Countries.Any())
            await AcquireConventionIncomeSourcesAsync(ctx, sources, ct);

        var system   = CaseSystemPrompt;
        var user     = BuildUserPrompt(ctx, sources);
        var raw      = await Llm.CompleteAsync(system, user, "Phase2", 3800, ct);
        var analyses = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");
        Logger.LogInformation("► [AGENT {T}] draft {N} chars", Type, analyses.Length);
        return new CaseDraft(analyses, sources, system, JudgeCriteria);
    }

    public virtual async Task<string> ReviseAsync(
        CaseContext ctx, CaseDraft draft, string guidance, CancellationToken ct = default)
    {
        var user = BuildUserPrompt(ctx, draft.Sources) +
            "\n\n═══ CORRECTIONS DEMANDÉES (relecture qualité) ═══\n" + guidance +
            "\nCorrige ces points en conservant strictement le format et le niveau de détail demandé.";
        var raw     = await Llm.CompleteAsync(draft.SystemPrompt, user, "Phase2-Revise", 3800, ct);
        var revised = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");
        return string.IsNullOrWhiteSpace(revised) ? draft.Analyses : revised;
    }

    protected virtual string BuildUserPrompt(CaseContext ctx, List<LegalSourceDto> sources) =>
        UsesOwnPrompt
            ? BuildOwnPrompt(ctx, sources)
            : H.BuildPhase2Prompt(ctx.Command, sources, ctx.EtendueItems, ctx.Sommaire, ctx.ContexteFaits,
                                  ctx.IsInternational, ctx.Branches, ctx.Plan);

    // Specialised Phase-2 prompt assembled from this agent's own hooks. No rates, no verdicts — every
    // number is read from the sources. The universal EY style card supplies the voice.
    protected string BuildOwnPrompt(CaseContext ctx, List<LegalSourceDto> sources)
    {
        var cmd     = ctx.Command;
        var n       = ctx.EtendueItems.Count;
        var et      = string.Join("\n", ctx.EtendueItems.Select((x, i) => $"  {i + 1}. {x}"));
        var concise = string.Equals(cmd.Mode, "concise", System.StringComparison.OrdinalIgnoreCase);
        var format  = concise
            ? "FORMAT — VERSION CONCISE : mêmes qualification, mêmes verdicts et mêmes taux que la version " +
              "détaillée, mais CONDENSÉS (chaque point en quelques phrases). N'omets aucun verdict ni taux.\n"
            : "FORMAT — VERSION DÉTAILLÉE : prose professionnelle continue, chaque point développé selon la " +
              "démarche ci-dessus, chaque règle appliquée aux faits et close par une position claire.\n";

        return
            $"PHASE 2 — JSON avec 1 clé: analyses.\n\n" +
            $"CAS QUALIFIÉ : {Label}\n\n" +
            $"Client : {cmd.ClientName} | Question : {cmd.FiscalQuestion}\n\n" +
            $"FAITS ÉTABLIS (section 1.1) :\n{ctx.ContexteFaits}\n\n" +
            $"ÉTENDUE ({n} point(s) demandé(s)) :\n{et}\n\n" +
            H.SourcesBlock(sources) + "\n" +
            "═══ QUALIFICATION ═══\n" + QualificationGuidance + "\n\n" +
            Demarche + "\n" +
            (string.IsNullOrWhiteSpace(ForbiddenSteps) ? "" : "═══ À NE PAS FAIRE ═══\n" + ForbiddenSteps + "\n\n") +
            EyStyle.Card + "\n" +
            "═══ MODÈLE DE STRUCTURE (forme uniquement — les crochets sont des ESPACES À REMPLIR depuis les " +
            "faits et les sources ; ne recopie JAMAIS un contenu du modèle) ═══\n" + RedactedSkeleton + "\n\n" +
            format +
            $"Organise en blocs « 4.1 » à « 4.{n} » (un par point d'étendue).\n" +
            "[Sn] OBLIGATOIRE ; tout taux cite sa source [Sn] et est LU dans son texte.\n\n" +
            "{\"analyses\":\"4. ANALYSES\\n\\n[blocs]\"}";
    }

    // Fetch the treaty income article (BY SUBJECT — its number varies per convention) and the domestic
    // rate article (CIRPPIS Art.52/53, newest year), then pin both to the front of the window so the
    // convention flood a treaty case pulls in cannot crowd them out.
    protected async Task AcquireConventionIncomeSourcesAsync(
        CaseContext ctx, List<LegalSourceDto> sources, CancellationToken ct)
    {
        var before = sources.Count;
        try
        {
            foreach (var country in ctx.Countries.Where(c => !string.IsNullOrWhiteSpace(c)).Take(2))
            foreach (var subj in TreatySubjects)
            {
                var hits = await Retrieval.FetchBySubjectAsync(
                    "conv_" + country, new[] { subj }, Topics,
                    new[] { subj.ToLowerInvariant() }, ct) ?? new List<LegalSourceDto>();
                var existing = new HashSet<string>(
                    sources.Select(s => s.ChunkId).Where(id => !string.IsNullOrEmpty(id)));
                foreach (var h in hits.Where(h => h is not null &&
                             (string.IsNullOrEmpty(h.ChunkId) || existing.Add(h.ChunkId))))
                    sources.Add(h);
            }
            var dom = await Retrieval.FetchDomesticRetenueAsync(new List<string>(), ct)
                      ?? new List<LegalSourceDto>();
            var seenDom = new HashSet<string>(sources.Select(s => s.ChunkId).Where(id => !string.IsNullOrEmpty(id)));
            foreach (var h in dom.Where(h => h is not null &&
                         (string.IsNullOrEmpty(h.ChunkId) || seenDom.Add(h.ChunkId))))
                sources.Add(h);
        }
        catch (System.Exception ex) { Logger.LogWarning(ex, "[AGENT {T}] source acquisition failed", Type); }

        PinConventionIncomeSources(sources, ctx.Countries, TreatySubjects);
        for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;
        Logger.LogInformation("► [AGENT {T}] +{N} src, pinned treaty+Art52", Type, sources.Count - before);
    }

    // Force the two decisive sources to the front: the treaty income article matching this agent's
    // subject (Dividendes / Intérêts / Redevances) for a detected country, and CIRPPIS Art.52/53.
    protected static void PinConventionIncomeSources(
        List<LegalSourceDto> sources, ICollection<string> countries, string[] treatySubjects)
    {
        static string Head(LegalSourceDto s) =>
            (s.Text ?? "")[..System.Math.Min((s.Text ?? "").Length, 60)];

        bool IsIncomeTreaty(LegalSourceDto s) =>
            string.Equals(s.DocType, "Convention", System.StringComparison.OrdinalIgnoreCase) &&
            treatySubjects.Any(subj => Head(s).Contains(subj, System.StringComparison.OrdinalIgnoreCase)) &&
            (countries.Count == 0 || countries.Any(c =>
                (s.DocName ?? "").Contains(c, System.StringComparison.OrdinalIgnoreCase)));

        bool IsDomesticRate(LegalSourceDto s) =>
            (s.DocName ?? "").Contains("code_irpp_is", System.StringComparison.OrdinalIgnoreCase) &&
            (H.Digits(s.ArticleRef) == "52" || H.Digits(s.ArticleRef) == "53") &&
            (s.Text ?? "").Contains('%');

        var treaty   = sources.Where(IsIncomeTreaty).ToList();
        var domestic = sources.Where(s => !IsIncomeTreaty(s) && IsDomesticRate(s)).ToList();
        var rest     = sources.Where(s => !IsIncomeTreaty(s) && !IsDomesticRate(s)).ToList();

        sources.Clear();
        sources.AddRange(treaty);
        sources.AddRange(domestic);
        sources.AddRange(rest);
    }

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
// One class per big case. Generic / RS-service run the proven legacy flow; the
// convention-income agents each carry their own démarche/sources/judge as code.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Fallback — the general international / foreign-service flow (legacy prompt).</summary>
public sealed class GenericAgent : CaseAgentBase
{
    public GenericAgent(ILlmAgent l, IRetrievalAgent r, ILogger<GenericAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Generic;
    protected override string Label => "Cas général / prestation internationale";
}

/// <summary>Foreign supplier of SERVICES: établissement stable → RS → TVA → assiette → transfert.
/// Runs the proven legacy service prompt; owns its qualification emphasis and judge criteria.</summary>
public sealed class RsServiceForeignAgent : CaseAgentBase
{
    public RsServiceForeignAgent(ILlmAgent l, IRetrievalAgent r, ILogger<RsServiceForeignAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.RsServiceForeign;
    protected override string Label => "Retenue à la source — fournisseur étranger de services";
    protected override string[] Topics => new[]
        { "remunerations_techniques", "services_professionnels", "etablissement_stable", "benefices_entreprises" };
    protected override string JudgeCriteria =>
        "Cas SERVICE / FOURNISSEUR ÉTRANGER : l'établissement stable est tranché d'abord en droit commun " +
        "puis selon l'Art.5 de la convention. La RS applique la ligne de l'Art.52 visant les « non domiciliés " +
        "ni établis » (non les honoraires-résidents). TVA due par retenue de 100% du preneur. Assiette (montant " +
        "brut TVA comprise) et formalisme du transfert (Art.112 CDPF + circ. BCT 9/2016) ne doivent pas manquer.";
}

/// <summary>Dividends distributed to a non-resident shareholder. NO ES-of-service, NO TVA section.</summary>
public sealed class DividendeAgent : CaseAgentBase
{
    public DividendeAgent(ILlmAgent l, IRetrievalAgent r, ILogger<DividendeAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Dividende;

    protected override bool     UsesOwnPrompt          => true;
    protected override string   CaseSystemPrompt       => ConventionIncomeSystem;
    protected override bool     NeedsConventionArticle => true;
    protected override string[] TreatySubjects         => new[] { "Dividendes" };
    protected override string[] Topics                 =>
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
}

/// <summary>Interest paid to a non-resident creditor. Convention interest article + Art 52 line.</summary>
public sealed class InteretAgent : CaseAgentBase
{
    public InteretAgent(ILlmAgent l, IRetrievalAgent r, ILogger<InteretAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Interet;

    protected override bool     UsesOwnPrompt          => true;
    protected override string   CaseSystemPrompt       => ConventionIncomeSystem;
    protected override bool     NeedsConventionArticle => true;
    protected override string[] TreatySubjects         => new[] { "Interets" };
    protected override string[] Topics                 => new[] { "interets", "elimination_double_imposition" };
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
}

/// <summary>Royalties (redevances) paid to a non-resident. Treaty royalties article + Art 52 line.</summary>
public sealed class RedevanceAgent : CaseAgentBase
{
    public RedevanceAgent(ILlmAgent l, IRetrievalAgent r, ILogger<RedevanceAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Redevance;

    protected override bool     UsesOwnPrompt          => true;
    protected override string   CaseSystemPrompt       => ConventionIncomeSystem;
    protected override bool     NeedsConventionArticle => true;
    protected override string[] TreatySubjects         => new[] { "Redevances" };
    protected override string[] Topics                 => new[] { "redevances", "elimination_double_imposition" };
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
}
