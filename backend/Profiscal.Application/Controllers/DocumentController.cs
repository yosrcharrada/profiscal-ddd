using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Profiscal.Application.Fiscal;

namespace Profiscal.Application.Controllers;

/// <summary>
/// Handles client document uploads — extracts text so the frontend can pass it
/// to consultation generation as AttachedDocumentTexts.
/// </summary>
[ApiController]
[Route("api/document")]
[Authorize]
[Produces("application/json")]
public sealed class DocumentController : ControllerBase
{
    [HttpPost("extract")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Extract([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "File required." });

        var text = await FileTextExtractor.ExtractAsync(file);
        return Ok(new
        {
            filename      = file.FileName,
            extractedText = text,
            charCount     = text.Length,
        });
    }
}
