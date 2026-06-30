using System.Text;

namespace Profiscal.API.Fiscal;

/// <summary>
/// Extracts plain text from uploaded client documents (.txt, .docx, .pdf).
/// Used before consultation generation when the client attaches supporting documents.
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
                var bytes = ms.ToArray();
                var sb    = new StringBuilder();
                for (int i = 0; i < bytes.Length - 1; i++)
                {
                    if (bytes[i] >= 32 && bytes[i] < 127) sb.Append((char)bytes[i]);
                    else if (bytes[i] == 10 || bytes[i] == 13) sb.Append('\n');
                }
                var raw = sb.ToString();
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"[^\S\n]{3,}", " ");
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\n{3,}", "\n\n");
                return raw.Length > 100 ? raw[..Math.Min(raw.Length, 15000)] : "";
            }
        }
        catch { /* non-critical — return empty */ }
        return "";
    }
}
