using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Playbooks;
using Microsoft.Extensions.Logging;
using H = FiscalPlatform.Application.Consultation.Commands.GenerateConsultation.GenerateConsultationCommandHandler;

namespace FiscalPlatform.Application.Consultation.Agents;

/// <summary>
/// Shared behaviour for every case agent: acquire case-specific sources, build the case prompt
/// (from the playbook), run Phase-2, and revise on request. Concrete agents differ only by their
/// <see cref="Type"/>, which selects the playbook — so each case is its own class (independently
/// registered, testable and upgradeable) with zero duplicated plumbing. The prompt/source builders
/// are the exact same shared functions the pipeline already used, so behaviour is unchanged.
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
    protected CasePlaybook Playbook => PlaybookRegistry.Get(Type);

    public virtual async Task<CaseDraft> DraftAsync(CaseContext ctx, CancellationToken ct = default)
    {
        var pb      = Playbook;
        var sources = ctx.Sources;   // same reference the handler holds — augmentations are visible

        if (pb.HasOwnPrompt && pb.NeedsConventionArticle &&
            pb.TreatySubjects.Length > 0 && ctx.Countries.Any())
            await AcquireConventionIncomeSourcesAsync(ctx, sources, pb, ct);

        var system   = pb.HasOwnPrompt ? pb.SystemPrompt : H.SystemPrompt;
        var user     = BuildUserPrompt(ctx, sources, pb);
        var raw      = await Llm.CompleteAsync(system, user, "Phase2", 3800, ct);
        var analyses = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");
        Logger.LogInformation("► [AGENT {T}] draft {N} chars", Type, analyses.Length);
        return new CaseDraft(analyses, sources, system, pb.JudgeCriteria);
    }

    public virtual async Task<string> ReviseAsync(
        CaseContext ctx, CaseDraft draft, string guidance, CancellationToken ct = default)
    {
        var user = BuildUserPrompt(ctx, draft.Sources, Playbook) +
            "\n\n═══ CORRECTIONS DEMANDÉES (relecture qualité) ═══\n" + guidance +
            "\nCorrige ces points en conservant strictement le format et le niveau de détail demandé.";
        var raw     = await Llm.CompleteAsync(draft.SystemPrompt, user, "Phase2-Revise", 3800, ct);
        var revised = H.GetStr(H.ParseJsonDict(raw ?? ""), "analyses");
        return string.IsNullOrWhiteSpace(revised) ? draft.Analyses : revised;
    }

    // The specialised (convention-income) playbooks build their own prompt; the legacy path
    // (Generic / RS-service) uses the original Phase-2 builder unchanged.
    protected virtual string BuildUserPrompt(CaseContext ctx, List<LegalSourceDto> sources, CasePlaybook pb) =>
        pb.HasOwnPrompt
            ? H.BuildPlaybookPhase2Prompt(ctx.Command, sources, ctx.EtendueItems, ctx.Sommaire, ctx.ContexteFaits, pb)
            : H.BuildPhase2Prompt(ctx.Command, sources, ctx.EtendueItems, ctx.Sommaire, ctx.ContexteFaits,
                                  ctx.IsInternational, ctx.Branches, ctx.Plan);

    // Fetch the treaty income article (by subject — its number varies per convention) and the domestic
    // rate article (CIRPPIS Art.52/53, newest year), then pin both to the front of the window so the
    // convention flood a treaty case pulls in cannot crowd them out.
    protected async Task AcquireConventionIncomeSourcesAsync(
        CaseContext ctx, List<LegalSourceDto> sources, CasePlaybook pb, CancellationToken ct)
    {
        var before = sources.Count;
        try
        {
            foreach (var country in ctx.Countries.Where(c => !string.IsNullOrWhiteSpace(c)).Take(2))
            foreach (var subj in pb.TreatySubjects)
            {
                var hits = await Retrieval.FetchBySubjectAsync(
                    "conv_" + country, new[] { subj }, pb.Topics,
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

        H.PinPlaybookSources(sources, ctx.Countries, pb);
        for (int i = 0; i < sources.Count; i++) sources[i].Index = i + 1;
        Logger.LogInformation("► [AGENT {T}] +{N} src, playbook-pinned", Type, sources.Count - before);
    }
}

// ── One class per big case. Behaviour comes from the base + the playbook selected by Type. ──

public sealed class GenericAgent : CaseAgentBase
{
    public GenericAgent(ILlmAgent l, IRetrievalAgent r, ILogger<GenericAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Generic;
}

public sealed class RsServiceForeignAgent : CaseAgentBase
{
    public RsServiceForeignAgent(ILlmAgent l, IRetrievalAgent r, ILogger<RsServiceForeignAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.RsServiceForeign;
}

public sealed class DividendeAgent : CaseAgentBase
{
    public DividendeAgent(ILlmAgent l, IRetrievalAgent r, ILogger<DividendeAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Dividende;
}

public sealed class InteretAgent : CaseAgentBase
{
    public InteretAgent(ILlmAgent l, IRetrievalAgent r, ILogger<InteretAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Interet;
}

public sealed class RedevanceAgent : CaseAgentBase
{
    public RedevanceAgent(ILlmAgent l, IRetrievalAgent r, ILogger<RedevanceAgent> g) : base(l, r, g) { }
    public override CaseType Type => CaseType.Redevance;
}
