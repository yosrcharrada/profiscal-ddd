using System.Security.Claims;
using System.Text.Json;
using FiscalPlatform.Application.Chat.Queries.Chat;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using FiscalPlatform.Application.Consultation.Commands.GenerateConsultation;
using FiscalPlatform.Application.Consultation.Commands.RateConsultation;
using FiscalPlatform.Application.Consultation.Commands.RefineConsultation;
using FiscalPlatform.Application.KnowledgeBase.Queries.GetStats;
using FiscalPlatform.Application.Search.Queries.SearchLegalDocuments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Profiscal.API.Fiscal;
using Profiscal.Contracts.Common;

namespace Profiscal.API.Controllers;

/// <summary>
/// Fiscal engine — semantic search, legal chatbot, and consultation generation/refinement.
/// Every endpoint requires a valid Profiscal JWT (our auth). Backed by the colleague's
/// GraphRAG pipeline over Neo4j + an LLM.
/// </summary>
[ApiController]
[Route("api/fiscal")]
[Authorize]
[Produces("application/json")]
public sealed class FiscalController(
    IMediator mediator,
    ISearchAgent searchAgent,
    IRetrievalAgent retrievalAgent,
    IDocumentGenerationAgent docAgent,
    ChatQueryHandler chatAgent,
    ConsultationStore store) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
            ? id : null;

    // ───────────────────────── SEARCH ENGINE ─────────────────────────
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequestDto req, CancellationToken ct)
    {
        // Allow a filter-only search (no query text) as long as at least one filter narrows it —
        // e.g. "show all Conventions". Reject only when there is neither a query nor any filter.
        var hasFilter = !string.Equals(req.DocType, "all", StringComparison.OrdinalIgnoreCase)
                     || !string.Equals(req.ChunkType, "all", StringComparison.OrdinalIgnoreCase)
                     || !string.Equals(req.Corpus, "all", StringComparison.OrdinalIgnoreCase)
                     || req.Year > 0 || req.Number.Length > 0 || req.DateText.Length > 0;
        if (string.IsNullOrWhiteSpace(req.Query) && !hasFilter)
            return BadRequest(ApiResponse<object>.Fail("Enter a search term or select a filter."));
        var result = await mediator.Send(new SearchLegalDocumentsQuery(req), ct);
        return Ok(ApiResponse<SearchResultDto>.Ok(result));
    }

    /// <summary>Whole-document view: assemble every passage of a document in reading order.
    /// Powers clicking a collapsed search result to read the full text (the Google model).</summary>
    [HttpGet("search/document/{documentId}")]
    public async Task<IActionResult> GetDocument(string documentId, CancellationToken ct)
    {
        var doc = await searchAgent.GetDocumentAsync(documentId, ct);
        return doc is null
            ? NotFound(ApiResponse<object>.Fail("Document introuvable."))
            : Ok(ApiResponse<LegalDocumentDto>.Ok(doc));
    }

    [HttpGet("search/health")]
    public async Task<IActionResult> SearchHealth()
    {
        var alive = await searchAgent.IsAliveAsync();
        var count = alive ? await searchAgent.CountAsync() : 0;
        return Ok(ApiResponse<object>.Ok(new { alive, count }));
    }

    // ───────────────────────── AGGREGATE HEALTH ─────────────────────────
    /// <summary>One call the UI uses to show what's connected: Neo4j, the LLM, the embed server.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health([FromServices] IConfiguration config, [FromServices] IHttpClientFactory http, CancellationToken ct)
    {
        var neo4j = await searchAgent.IsAliveAsync();
        var chunks = neo4j ? await searchAgent.CountAsync() : 0;

        var llmConfigured = !string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"]);

        bool embed = false;
        try
        {
            var url = config["EmbedServer:Url"] ?? "http://127.0.0.1:8081/embed_search";
            var healthUrl = url.Replace("/embed_search", "/health");
            using var c = http.CreateClient();
            c.Timeout = TimeSpan.FromSeconds(2);
            var r = await c.GetAsync(healthUrl, ct);
            embed = r.IsSuccessStatusCode;
        }
        catch { embed = false; }

        return Ok(ApiResponse<object>.Ok(new
        {
            neo4j, chunks, llmConfigured, embedServer = embed,
            ready = neo4j && llmConfigured,
        }));
    }

    // ───────────────────────── KNOWLEDGE BASE STATS ─────────────────────────
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct) =>
        Ok(ApiResponse<KnowledgeBaseStatsDto>.Ok(await mediator.Send(new GetKnowledgeBaseStatsQuery(), ct)));

    [HttpGet("stats/health")]
    public async Task<IActionResult> StatsHealth() =>
        Ok(ApiResponse<object>.Ok(new { alive = await retrievalAgent.IsAliveAsync() }));

    // ───────────────────────── CHATBOT ─────────────────────────
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatApiRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(ApiResponse<object>.Fail("Question required."));
        var result = await mediator.Send(new ChatQuery(req.Question, req.History ?? new()), ct);
        return Ok(ApiResponse<ChatResponseDto>.Ok(result));
    }

    /// <summary>
    /// Streaming chatbot (Server-Sent Events): emits `status` updates while the agent works,
    /// then `sources`, then the answer `token`-by-token, then `done`.
    /// </summary>
    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] ChatApiRequest req, CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(req.Question))
        {
            await WriteSse("error", "{\"message\":\"Question required.\"}", ct);
            return;
        }

        await foreach (var ev in chatAgent.StreamAsync(new ChatQuery(req.Question, req.History ?? new()), ct))
        {
            var (name, payload) = ev switch
            {
                ChatStatusEvent s  => ("status",  JsonSerializer.Serialize(new { phase = s.Phase, text = s.Text }, SseJson)),
                ChatSourcesEvent s => ("sources", JsonSerializer.Serialize(s.Sources, SseJson)),
                ChatTokenEvent t   => ("token",   JsonSerializer.Serialize(new { text = t.Text }, SseJson)),
                ChatDoneEvent d    => ("done",    JsonSerializer.Serialize(new { elapsedMs = d.ElapsedMs }, SseJson)),
                _                  => ("", ""),
            };
            if (name.Length == 0) continue;
            await WriteSse(name, payload, ct);
        }
    }

    private async Task WriteSse(string evName, string data, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {evName}\ndata: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    // ───────────────────────── CONSULTATIONS ─────────────────────────
    /// <summary>Generate a consultation. Returns the structured output (JSON) for the editor.</summary>
    [HttpPost("consultations/generate")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Generate([FromBody] GenerateConsultationApiRequest req, CancellationToken ct)
    {
        var reference  = string.IsNullOrWhiteSpace(req.Reference)  ? $"CONS-{DateTime.Now:yyyy-MMdd}" : req.Reference!;
        var clientName = string.IsNullOrWhiteSpace(req.ClientName) ? "Client" : req.ClientName!;

        var mode = string.Equals(req.Mode?.Trim(), "concise", StringComparison.OrdinalIgnoreCase)
            ? "concise" : "detaillee";

        var dto = await mediator.Send(new GenerateConsultationCommand(
            reference, clientName, req.Situation, req.FiscalQuestion,
            req.Documents ?? new(),
            (req.AttachedDocumentTexts ?? new()).Where(t => !string.IsNullOrEmpty(t)).ToList(),
            mode), ct);

        await store.SaveAsync(dto.ConsultationId, reference, clientName,
            req.Situation, req.FiscalQuestion, dto.Output, CurrentUserId, ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            consultationId = dto.ConsultationId,
            reference,
            clientName,
            method   = dto.Method,
            elapsedMs = dto.ElapsedMs,
            output   = dto.Output,
        }));
    }

    /// <summary>List consultations (mine, or all for admins).</summary>
    [HttpGet("consultations")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool all = false,
        [FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null, CancellationToken ct = default)
    {
        var owner = (all && User.IsInRole("Admin")) ? (Guid?)null : CurrentUserId;
        var rows  = await store.ListAsync(owner, search, dateFrom, dateTo, ct);
        return Ok(ApiResponse<object>.Ok(rows.Select(c => new
        {
            id = c.Id, c.Reference, c.ClientName, c.FiscalQuestion,
            c.Method, c.SourcesCount, c.Rating, c.RefineCount,
            c.IsInternational, createdAt = c.CreatedAt, updatedAt = c.UpdatedAt,
        })));
    }

    /// <summary>Reopen a saved consultation with its full output for editing.</summary>
    [HttpGet("consultations/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await store.GetAsync(id, ct);
        if (row is null) return NotFound(ApiResponse<object>.Fail("Consultation not found."));
        return Ok(ApiResponse<object>.Ok(new
        {
            id = row.Id, row.Reference, row.ClientName, row.Situation, row.FiscalQuestion,
            row.Method, row.Rating, row.RatingComment, row.RefineCount,
            createdAt = row.CreatedAt, updatedAt = row.UpdatedAt,
            output = store.DeserializeOutput(row),
        }));
    }

    /// <summary>Rename a consultation from the history rail (double-click rename).</summary>
    [HttpPut("consultations/{id:guid}/rename")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameConsultationApiRequest req, CancellationToken ct)
    {
        var name = (req.ClientName ?? "").Trim();
        if (name.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("A name is required."));
        var ok = await store.RenameAsync(id, name, CurrentUserId, User.IsInRole("Admin"), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { renamed = true, clientName = name }))
            : NotFound(ApiResponse<object>.Fail("Consultation not found."));
    }

    /// <summary>Delete a consultation (owner or admin).</summary>
    [HttpDelete("consultations/{id:guid}")]
    public async Task<IActionResult> DeleteConsultation(Guid id, CancellationToken ct)
    {
        var ok = await store.DeleteAsync(id, CurrentUserId, User.IsInRole("Admin"), ct);
        return ok
            ? Ok(ApiResponse<object>.Ok(new { deleted = true }))
            : NotFound(ApiResponse<object>.Fail("Consultation not found."));
    }

    [HttpPost("consultations/rate")]
    public async Task<IActionResult> Rate([FromBody] RateApiRequest req, CancellationToken ct)
    {
        await mediator.Send(new RateConsultationCommand(req.ConsultationId, req.Reference, req.Stars, req.Comment), ct);
        return Ok(ApiResponse<object>.Ok(new { message = $"Rated {req.Stars}★." }));
    }

    /// <summary>Export a consultation output as a Word .docx.</summary>
    [HttpPost("consultations/export")]
    public IActionResult Export([FromBody] ExportApiRequest req)
    {
        var bytes = docAgent.Generate(new GenerateDocumentRequest(
            req.Reference ?? "CONS", req.ClientName ?? "Client",
            req.Situation ?? "", req.FiscalQuestion ?? "",
            req.Documents ?? new(), req.Output));
        var safe = System.Text.RegularExpressions.Regex.Replace(req.ClientName ?? "Client", @"[^\w\s-]", "").Trim().Replace(" ", "_");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"Consultation_{safe}_{DateTime.Now:dd-MM-yyyy}.docx");
    }

    // ───────────────────────── REFINEMENT (editable sections) ─────────────────────────
    [HttpPost("refine/session/start")]
    public async Task<IActionResult> StartSession([FromBody] StartSessionApiRequest req, CancellationToken ct)
    {
        var sessionId = await mediator.Send(
            new StartConsultationSessionCommand(req.ConsultationId, req.ClientName, req.Reference), ct);
        return Ok(ApiResponse<object>.Ok(new { sessionId }));
    }

    [HttpPost("refine/message")]
    public async Task<IActionResult> Refine([FromBody] RefineApiRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new RefineConsultationCommand(
            req.SessionId, req.UserMessage,
            req.CurrentOutput ?? new FiscalPlatform.Application.Common.DTOs.ConsultationOutput(),
            req.Sources ?? new(), req.TargetSection), ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            reply         = result.AssistantReply,
            sectionName   = result.SectionName,
            updatedOutput = result.UpdatedOutput,
            sessionId     = result.SessionId,
        }));
    }

    [HttpPost("refine/session/end")]
    public async Task<IActionResult> EndSession([FromBody] EndSessionApiRequest req, CancellationToken ct)
    {
        await mediator.Send(new EndConsultationSessionCommand(req.SessionId), ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Session ended." }));
    }

    /// <summary>Persist the edited output back to a saved consultation.</summary>
    [HttpPut("consultations/{id:guid}/output")]
    public async Task<IActionResult> SaveOutput(Guid id, [FromBody] FiscalPlatform.Application.Common.DTOs.ConsultationOutput output, CancellationToken ct)
    {
        await store.UpdateOutputAsync(id, output, ct);
        return Ok(ApiResponse<object>.Ok(new { message = "Saved." }));
    }
}
