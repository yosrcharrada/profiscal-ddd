using Microsoft.AspNetCore.Mvc;
using FiscalPlatform.API.Requests;

namespace FiscalPlatform.API.Controllers;

/// <summary>
/// Handles client document uploads.
/// Uploaded files are extracted to text and passed to GenerateConsultationCommand.
/// </summary>
[ApiController]
[Route("api/document")]
public sealed class DocumentController : ControllerBase
{
    [HttpPost("extract")]
    public async Task<IActionResult> Extract([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "File required." });
        var text = await FileTextExtractor.ExtractAsync(file);
        return Ok(new { filename = file.FileName, extractedText = text, charCount = text.Length });
    }
}
