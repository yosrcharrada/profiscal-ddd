using System.Text;

namespace FiscalPlatform.API.Requests;

/// <summary>
/// Extracts text from uploaded client documents (.txt, .docx, .pdf).
/// Used by ConsultationController and DocumentController.
/// Supports: Meeting requirement — attached documents.
/// </summary>
public static class FileTextExtractor
{
    public static async Task<string> ExtractAsync(IFormFile file)
    {
        if (file is null || file.Length == 0) return "";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        try
        {
            if (ext == ".txt")
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            if (ext == ".docx")
            {
                using var ms  = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;
                using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(ms, false);
                var body = doc.MainDocumentPart?.Document.Body;
                return body is null ? "" :
                    string.Join("\n", body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                        .Select(p => p.InnerText).Where(t => !string.IsNullOrWhiteSpace(t)));
            }
            if (ext == ".pdf")
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                // Best-effort ASCII extraction — for production use PdfPig or iTextSharp
                var bytes = ms.ToArray();
                var sb    = new StringBuilder();
                for (int i = 0; i < bytes.Length - 1; i++)
                {
                    if (bytes[i] >= 32 && bytes[i] < 127) sb.Append((char)bytes[i]);
                    else if (bytes[i] == 10 || bytes[i] == 13) sb.Append('\n');
                }
                var raw = sb.ToString();
                // Clean up PDF artifacts
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"[^\S\n]{3,}", " ");
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\n{3,}", "\n\n");
                return raw.Length > 100 ? raw[..Math.Min(raw.Length, 15000)] : "";
            }
        }
        catch { /* non-critical — return empty */ }
        return "";
    }
}
