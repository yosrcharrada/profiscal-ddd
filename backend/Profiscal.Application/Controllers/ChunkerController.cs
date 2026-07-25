using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Profiscal.Domain.Contracts.Common;

namespace Profiscal.Application.Controllers;

/// <summary>
/// Server-side gateway to the qEntropy chunking service (the FastAPI app vendored under
/// <c>chunker/</c>), used by the admin "Base de connaissances" page.
///
/// WHY A PROXY rather than letting the browser call the chunker directly:
///  • <b>Authorisation.</b> Chunking ingests documents into the legal corpus, so it is an
///    admin-only operation. Routing through the API applies <c>[Authorize(Roles = "Admin")]</c>;
///    a direct browser → FastAPI call would bypass the platform's identity entirely, since the
///    chunker has no notion of users or roles.
///  • <b>Exposure.</b> The chunker never has to be reachable from the browser (or, later, from
///    outside the host at all) — only the API talks to it, over loopback.
///  • <b>One origin.</b> The SPA keeps talking to its own API, so there is no second CORS
///    surface to configure per environment.
///
/// The chunker's contract is passed through unchanged (upload → run → poll status → fetch
/// results), so this stays a thin, honest relay: it adds authorisation and error shaping, and
/// deliberately does NOT reinterpret the pipeline's payloads.
///
/// Configuration — <c>Chunker:BaseUrl</c> (default <c>http://127.0.0.1:8000</c>).
/// When the chunker is not running every endpoint answers 503 with an actionable message
/// rather than surfacing a raw connection error.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ChunkerController(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<ChunkerController> logger) : ControllerBase
{
    private string BaseUrl =>
        (config["Chunker:BaseUrl"] ?? "http://127.0.0.1:8000").TrimEnd('/');

    /// <summary>The pipeline can run for minutes on a large PDF; polling is cheap but the
    /// initial upload/parse of a big file is not, so allow a generous ceiling.</summary>
    private HttpClient Client()
    {
        var c = httpFactory.CreateClient();
        c.Timeout = TimeSpan.FromMinutes(10);
        return c;
    }

    /// <summary>Is the chunker reachable? Drives the admin page's "service offline" state.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        try
        {
            using var c = httpFactory.CreateClient();
            c.Timeout = TimeSpan.FromSeconds(3);
            var r = await c.GetAsync($"{BaseUrl}/health", ct);
            return Ok(ApiResponse<object>.Ok(new { alive = r.IsSuccessStatusCode, baseUrl = BaseUrl }));
        }
        catch
        {
            // A dead chunker is an expected operational state, not an error worth logging loudly.
            return Ok(ApiResponse<object>.Ok(new { alive = false, baseUrl = BaseUrl }));
        }
    }

    /// <summary>Which embedding backends the chunker can use (OpenAI appears only when a key
    /// is configured on its side; the local sentence-transformer backends are always listed).</summary>
    [HttpGet("backends")]
    public Task<IActionResult> Backends(CancellationToken ct) => RelayGetAsync("/backends", ct);

    /// <summary>
    /// Upload a document to chunk. Streams the multipart file straight through — the file is
    /// never buffered to disk here, and the API never inspects it.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(200_000_000)] // large scanned legal PDFs
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("Aucun fichier fourni."));

        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = file.OpenReadStream();
            var part = new StreamContent(stream);
            part.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            // The FastAPI handler binds `file: UploadFile = File(...)`, so the part MUST be named "file".
            content.Add(part, "file", file.FileName);

            using var c = Client();
            var resp = await c.PostAsync($"{BaseUrl}/upload", content, ct);
            return await PassThroughAsync(resp, ct);
        }
        catch (Exception ex)
        {
            return Unreachable(ex, "upload");
        }
    }

    /// <summary>
    /// Start the pipeline for a previously uploaded document. <paramref name="config"/> is the
    /// chunker's own JSON configuration blob, forwarded verbatim so the service stays the single
    /// owner of its tuning contract (strategies, q, embedding backend…).
    /// Returns immediately with a job id — the run itself is polled via <see cref="Status"/>.
    /// </summary>
    [HttpPost("run/{documentId}")]
    public async Task<IActionResult> Run(string documentId, [FromBody] RunChunkerRequest? body, CancellationToken ct)
    {
        try
        {
            using var content = new MultipartFormDataContent
            {
                // FastAPI declares `config: str = Form(default="{}")` — a form field, not JSON.
                { new StringContent(body?.Config ?? "{}"), "config" },
            };
            using var c = Client();
            var resp = await c.PostAsync($"{BaseUrl}/run/{Uri.EscapeDataString(documentId)}", content, ct);
            return await PassThroughAsync(resp, ct);
        }
        catch (Exception ex)
        {
            return Unreachable(ex, "run");
        }
    }

    /// <summary>Current stage + progress for a running job (polled by the admin page).</summary>
    [HttpGet("status/{jobId}")]
    public Task<IActionResult> Status(string jobId, CancellationToken ct) =>
        RelayGetAsync($"/status/{Uri.EscapeDataString(jobId)}", ct);

    /// <summary>The finished chunks, document profile and evaluation summary.</summary>
    [HttpGet("results/{jobId}")]
    public Task<IActionResult> Results(string jobId, CancellationToken ct) =>
        RelayGetAsync($"/results/{Uri.EscapeDataString(jobId)}", ct);

    // ── plumbing ─────────────────────────────────────────────────────────────
    private async Task<IActionResult> RelayGetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var c = Client();
            var resp = await c.GetAsync($"{BaseUrl}{path}", ct);
            return await PassThroughAsync(resp, ct);
        }
        catch (Exception ex)
        {
            return Unreachable(ex, path);
        }
    }

    /// <summary>Forward the chunker's JSON and status code untouched. The chunker uses 202 while
    /// a pipeline is still running and 500 to carry its own error text, and the admin page relies
    /// on seeing those verbatim — so we do not normalise them into the ApiResponse envelope.</summary>
    private async Task<IActionResult> PassThroughAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var json = await resp.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            Content     = json,
            ContentType = "application/json",
            StatusCode  = (int)resp.StatusCode,
        };
    }

    private IActionResult Unreachable(Exception ex, string what)
    {
        logger.LogWarning(ex, "[CHUNKER] {What} failed — service unreachable at {Url}", what, BaseUrl);
        return StatusCode(503, ApiResponse<object>.Fail(
            $"Le service de chunking est injoignable ({BaseUrl}). Démarrez-le puis réessayez."));
    }
}

/// <summary>Body of <c>POST api/chunker/run/{documentId}</c>. <see cref="Config"/> is the
/// chunker's own JSON config, forwarded as-is (empty object = its defaults).</summary>
public sealed class RunChunkerRequest
{
    public string? Config { get; set; }
}
