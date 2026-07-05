using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;
using FiscalPlatform.Application.Consultation.Playbooks;

namespace FiscalPlatform.Application.Consultation.Orchestration;

/// <summary>
/// The single shared state that flows through every node of the consultation workflow — the graph's
/// message type. One mutable instance travels the edges (in-process), so each executor reads what
/// upstream produced and writes its own contribution. Loop counters bound the two cycles
/// (retrieval-completeness, judge-revision) so the graph always terminates.
/// </summary>
public sealed class ConsultationState
{
    // ── Input & understanding (filled by the handler before the workflow starts) ──
    public required GenerateConsultationCommand Command   { get; init; }
    public required List<string>    EtendueItems           { get; init; }
    public required string          ContexteFaits          { get; init; }
    public required string          Sommaire               { get; init; }
    public required List<string>    Countries              { get; init; }
    public required bool            IsInternational        { get; set; }
    public required HashSet<string> Branches               { get; init; }
    public required RetrievalPlan   Plan                   { get; init; }
    public required List<LegalSourceDto> Sources           { get; init; }

    // ── [1] Qualification ──
    public CaseType CaseType { get; set; } = CaseType.Generic;

    // ── [2] Case brief (the case agent's contract) ──
    public CaseBrief? Brief { get; set; }

    // ── [3]/[4] Retrieval fulfilment + completeness ──
    public CompletenessReport? Completeness { get; set; }
    public int  RetrievalLoops { get; set; }
    public const int MaxRetrievalLoops = 3;   // initial fulfil + 1 completeness retry + 1 judge-driven

    // ── [5] Writer ──
    public string Analyses    { get; set; } = "";
    public int    WriterLoops { get; set; }
    public const int MaxWriterLoops = 2;      // initial draft + 1 judge-driven revision

    // ── [6] Judge ──
    public bool         JudgeAccepted     { get; set; }
    public string       JudgeGuidance     { get; set; } = "";
    public List<string> JudgeMissingTopics{ get; set; } = new();
    public bool         JudgeNeedsSources { get; set; }
    public bool         JudgeRan          { get; set; }

    // ── [7]/[8] Expert voice + finalize ──
    public bool ExpertApplied { get; set; }
    public List<AnalysisRow> Table { get; set; } = new();

    // ── Telemetry: per-node timings surfaced back into the handler's timing table ──
    public List<(string Step, double Ms, string Note)> Timings { get; } = new();

    // ── Routing helpers (evaluated by the conditional edges) ──
    public bool NeedsRetrievalRetry =>
        Completeness is { CriticallyComplete: false } && RetrievalLoops < MaxRetrievalLoops;

    public bool NeedsJudgeRetrieval =>
        JudgeRan && !JudgeAccepted && JudgeNeedsSources && JudgeMissingTopics.Count > 0
        && RetrievalLoops < MaxRetrievalLoops;

    public bool NeedsWriterRevision =>
        JudgeRan && !JudgeAccepted && !NeedsJudgeRetrieval && WriterLoops < MaxWriterLoops;

    public bool ProceedToExpert =>
        JudgeRan && (JudgeAccepted || (!NeedsJudgeRetrieval && !NeedsWriterRevision));
}
