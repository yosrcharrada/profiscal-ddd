using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Profiscal.Domain.Dtos;
using Profiscal.Domain.Abstractions.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Profiscal.Infrastructure.Agents;

/// <summary>
/// Document Generation Agent — fills template_fr.docx with consultation output.
/// Placeholders: [NOM_CLIENT], [REFERENCE], [DATE], [FAITS], [ETENDUE],
///               [ABREVIATIONS], [SOMMAIRE], [ANALYSES], [DOCUMENTS]
/// Beware: the template's "SOMMAIRE" Heading1 is the table of CONTENTS — the sommaire exécutif is
/// the separate [SOMMAIRE] section, which sits just before "Analyses".
/// Token replacement is drawing-safe: runs carrying images/shapes/fields are never
/// touched, so the EY logo, the yellow client box and the header survive intact.
/// </summary>
public sealed class DocumentGenerationAgent(
    IConfiguration config,
    ILogger<DocumentGenerationAgent> logger)
    : IDocumentGenerationAgent
{
    public byte[] Generate(GenerateDocumentRequest req)
    {
        // Try config path, then current directory, then AppContext base
        var templateName = "template_fr.docx";
        var path = config["TemplatePath"] ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), templateName);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, templateName);
        if (!File.Exists(path)) throw new FileNotFoundException("template_fr.docx not found", path);

        using var mem = new MemoryStream();
        using (var fs = File.OpenRead(path)) fs.CopyTo(mem);
        mem.Position = 0;

        using (var doc = WordprocessingDocument.Open(mem, true))
        {
            var body = doc.MainDocumentPart!.Document.Body
                       ?? throw new InvalidOperationException("Document body is null");

            var today  = DateTime.Now;
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["[NOM_CLIENT]"]    = req.ClientName,
                ["{{CLIENT_NAME}}"] = req.ClientName,
                ["[REFERENCE]"]     = req.Reference,
                ["{{REFERENCE}}"]   = req.Reference,
                ["[DATE]"]          = today.ToString("dd/MM/yyyy"),
                ["{{DATE}}"]        = today.ToString("dd/MM/yyyy"),
                ["[MM/AA]"]         = today.ToString("MM/yy"),
            };

            ReplaceTokensEverywhere(doc, tokens);
            ReplaceSectionContent(body, "[FAITS]",        ParseMarkdown(req.Output.ContexteFaits));
            ReplaceSectionContent(body, "[ETENDUE]",      ParseMarkdown(req.Output.Etendue));
            ReplaceSectionContent(body, "[ABREVIATIONS]", ParseMarkdown(req.Output.Abbreviations));

            // The sommaire exécutif is its own section, ahead of the analyses: the reader gets the
            // verdicts first, then the demonstration behind them. NOTE: the [SOMMAIRE] placeholder
            // was ADDED to template_fr.docx for this — the template never had one (its "SOMMAIRE"
            // Heading1 is the table of CONTENTS), so this call used to match nothing and the
            // summary was silently dropped from every generated .docx, showing only in the web
            // editor. If you swap the template, carry the placeholder over.
            ReplaceSectionContent(body, "[SOMMAIRE]",  ParseMarkdown(req.Output.SommairExecutif));
            ReplaceSectionContent(body, "[ANALYSES]",  ParseMarkdown(req.Output.Analyses));
            ReplaceSectionContent(body, "[DOCUMENTS]", ParseMarkdown(req.Output.Documents));

            doc.MainDocumentPart.Document.Save();
        }
        logger.LogInformation("Document generated: {Client} {Ref}", req.ClientName, req.Reference);
        return mem.ToArray();
    }

    // ─── Token replacement ────────────────────────────────────────────────────
    private static void ReplaceTokensEverywhere(WordprocessingDocument doc, Dictionary<string, string> tokens)
    {
        var body = doc.MainDocumentPart!.Document.Body!;
        foreach (var para in body.Descendants<Paragraph>())
            ApplyTokensInParagraph(para, tokens);
        foreach (var hp in doc.MainDocumentPart.HeaderParts)
            foreach (var para in hp.Header.Descendants<Paragraph>())
                ApplyTokensInParagraph(para, tokens);
        foreach (var fp in doc.MainDocumentPart.FooterParts)
            foreach (var para in fp.Footer.Descendants<Paragraph>())
                ApplyTokensInParagraph(para, tokens);
    }

    private static void ApplyTokensInParagraph(Paragraph para, Dictionary<string, string> tokens)
    {
        // Operate ONLY on this paragraph's plain-text runs. Any run carrying a drawing, picture,
        // embedded object or field is left completely untouched — otherwise its internal geometry
        // (EMU coordinates like "-864235") leaks out as text and the image/shape is deleted.
        // Text INSIDE a drawing text box is a paragraph of its own and is handled when the
        // Descendants<Paragraph>() walk reaches it, so tokens in the yellow box still get replaced.
        var textRuns = para.Elements<Run>()
            .Where(r => r.Elements<Text>().Any()
                     && !r.Descendants<Drawing>().Any()
                     && !r.Descendants<Picture>().Any()
                     && !r.Descendants<EmbeddedObject>().Any()
                     && !r.Descendants<FieldChar>().Any())
            .ToList();
        if (textRuns.Count == 0) return;

        var merged = string.Concat(textRuns.Select(r => string.Concat(r.Elements<Text>().Select(t => t.Text))));

        // Fast path: no token in this paragraph → leave it byte-for-byte as the template had it
        // (this is what preserves the header, the logo and every unrelated paragraph).
        if (!tokens.Keys.Any(merged.Contains)) return;

        foreach (var (k, v) in tokens) merged = merged.Replace(k, v);

        // Collapse the matched text runs into the first (keeping its formatting); clear the rest.
        var first = textRuns[0];
        for (int i = 1; i < textRuns.Count; i++) textRuns[i].Remove();
        foreach (var child in first.Elements<Text>().ToList()) child.Remove();
        foreach (var child in first.Elements<Break>().ToList()) child.Remove();

        bool wrote = false;
        foreach (var line in merged.Split('\n'))
        {
            if (wrote) first.AppendChild(new Break());
            first.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            wrote = true;
        }
    }

    // ─── Section replacement ──────────────────────────────────────────────────
    private static void ReplaceSectionContent(Body body, string placeholder, List<DocParagraph> content)
    {
        var para = body.Descendants<Paragraph>().FirstOrDefault(p => MergedText(p).Contains(placeholder));
        if (para is null) return;
        var parent = para.Parent!;
        foreach (var dp in content) parent.InsertBefore(BuildParagraph(dp), para);
        para.Remove();
    }

    private static string MergedText(Paragraph p) => string.Concat(p.Elements<Run>().Select(r => r.InnerText));

    // ─── Markdown parser ──────────────────────────────────────────────────────
    private static List<DocParagraph> ParseMarkdown(string text)
    {
        var result = new List<DocParagraph>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if      (line.StartsWith("#### ")) result.Add(new DocParagraph(line[5..], DocStyle.Heading4));
            else if (line.StartsWith("### "))  result.Add(new DocParagraph(line[4..], DocStyle.Heading3));
            else if (line.StartsWith("## "))   result.Add(new DocParagraph(line[3..], DocStyle.Heading2));
            else if (line.StartsWith("**") && line.EndsWith("**") && line.Length > 4)
                result.Add(new DocParagraph(line[2..^2], DocStyle.Bold));
            else if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• "))
                // bullet line → its own paragraph with a bullet glyph
                result.Add(new DocParagraph("•  " + line[2..].TrimStart(), DocStyle.Normal));
            else
                result.Add(new DocParagraph(line, DocStyle.Normal));
        }
        return result;
    }

    private static OpenXmlElement BuildParagraph(DocParagraph dp) => dp.Style switch
    {
        DocStyle.Heading2 =>
            new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }),
                          new Run(new Text(dp.Text) { Space = SpaceProcessingModeValues.Preserve })),
        DocStyle.Heading3 =>
            new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading3" }),
                          new Run(new Text(dp.Text) { Space = SpaceProcessingModeValues.Preserve })),
        DocStyle.Heading4 =>
            new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading4" }),
                          new Run(new Text(dp.Text) { Space = SpaceProcessingModeValues.Preserve })),
        DocStyle.Bold =>
            new Paragraph(new Run(
                new RunProperties(new Bold()),
                new Text(dp.Text) { Space = SpaceProcessingModeValues.Preserve })),
        _ =>
            new Paragraph(BuildRuns(dp.Text)),
    };

    private static Run[] BuildRuns(string text)
    {
        var runs  = new List<Run>();
        var parts = SplitInline(text);
        foreach (var (t, bold, italic) in parts)
        {
            var rPr = new RunProperties();
            if (bold)   rPr.AppendChild(new Bold());
            if (italic) rPr.AppendChild(new Italic());
            var run = new Run();
            if (bold || italic) run.AppendChild(rPr);
            run.AppendChild(new Text(t) { Space = SpaceProcessingModeValues.Preserve });
            runs.Add(run);
        }
        return runs.ToArray();
    }

    private static List<(string Text, bool Bold, bool Italic)> SplitInline(string line)
    {
        var result = new List<(string, bool, bool)>();
        var rx     = new Regex(@"\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|\*(.+?)\*");
        var last   = 0;
        foreach (Match m in rx.Matches(line))
        {
            if (m.Index > last) result.Add((line[last..m.Index], false, false));
            if (m.Groups[1].Success) result.Add((m.Groups[1].Value, true,  true));
            else if (m.Groups[2].Success) result.Add((m.Groups[2].Value, true, false));
            else if (m.Groups[3].Success) result.Add((m.Groups[3].Value, false, true));
            last = m.Index + m.Length;
        }
        if (last < line.Length) result.Add((line[last..], false, false));
        return result;
    }
}

public sealed record DocParagraph(string Text, DocStyle Style);
public enum DocStyle { Normal, Heading2, Heading3, Heading4, Bold, Italic }
