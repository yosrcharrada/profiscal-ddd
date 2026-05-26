using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FiscalPlatform.Application.Common.DTOs;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

/// <summary>
/// Document Generation Agent — fills template_fr.docx with consultation output.
/// Placeholders: [NOM_CLIENT], [REFERENCE], [DATE], [FAITS], [ETENDUE],
///               [ABREVIATIONS], [SOMMAIRE], [ANALYSES], [DOCUMENTS]
/// Builds EY-branded analysis table (1F3864 header, EBF2FA alternating rows).
/// Strips numeric Word artifacts from headers (^\d{7,}).
/// </summary>
public sealed class DocumentGenerationAgent(
    IHostEnvironment env,
    ILogger<DocumentGenerationAgent> logger)
    : IDocumentGenerationAgent
{
    public byte[] Generate(GenerateDocumentRequest req)
    {
        var path = Path.Combine(env.ContentRootPath, "template_fr.docx");
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
            ReplaceSectionContent(body, "[SOMMAIRE]",     ParseMarkdown(req.Output.SommairExecutif));
            ReplaceSectionContent(body, "[ANALYSES]",     ParseMarkdown(req.Output.Analyses));

            var docPs = ParseMarkdown(req.Output.Documents);
            if (req.Output.AnalysisTable.Any())
            {
                docPs.Add(new DocParagraph("", DocStyle.Normal));
                docPs.Add(new DocParagraph("Tableau de synthèse", DocStyle.Heading3));
                docPs.Add(new DocParagraph("__TABLE__", DocStyle.Normal));
            }
            ReplaceSectionContentWithTable(body, "[DOCUMENTS]", docPs, req.Output.AnalysisTable);

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
        var runs  = para.Elements<Run>().ToList();
        if (!runs.Any()) return;
        var merged = string.Concat(runs.Select(r => r.InnerText));
        // Strip leading numeric Word artifacts e.g. "7543802613660"
        merged = Regex.Replace(merged, @"^\d{7,}(?=[^\d])", "");

        foreach (var (k, v) in tokens) merged = merged.Replace(k, v);

        var first = runs[0];
        var rPr   = first.RunProperties?.CloneNode(true) as RunProperties;
        foreach (var r in runs) r.Remove();

        var newRun = new Run();
        if (rPr is not null) newRun.AppendChild(rPr);
        foreach (var line in merged.Split('\n'))
        {
            if (newRun.ChildElements.Any(c => c is Text)) newRun.AppendChild(new Break());
            newRun.AppendChild(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        }
        para.AppendChild(newRun);
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

    private static void ReplaceSectionContentWithTable(
        Body body, string placeholder, List<DocParagraph> content, List<AnalysisRow> table)
    {
        var para = body.Descendants<Paragraph>().FirstOrDefault(p => MergedText(p).Contains(placeholder));
        if (para is null) return;
        var parent = para.Parent!;
        foreach (var dp in content)
        {
            if (dp.Text == "__TABLE__") parent.InsertBefore(BuildAnalysisTable(table), para);
            else parent.InsertBefore(BuildParagraph(dp), para);
        }
        para.Remove();
    }

    private static string MergedText(Paragraph p) => string.Concat(p.Elements<Run>().Select(r => r.InnerText));

    // ─── Analysis table (EY colors) ───────────────────────────────────────────
    private static Table BuildAnalysisTable(List<AnalysisRow> rows)
    {
        var tbl = new Table();
        tbl.AppendChild(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "9640", Type = TableWidthUnitValues.Dxa }));

        var headers = new[] { ("N",5), ("Sujet / Point de l'étendue",20),
                               ("Analyse juridique",50), ("Conclusion",25) };
        var hdr = new TableRow();
        foreach (var (title, pct) in headers)
        {
            var w = ((int)(9640 * pct / 100.0)).ToString();
            hdr.AppendChild(new TableCell(
                new TableCellProperties(
                    new TableCellWidth { Width = w, Type = TableWidthUnitValues.Dxa },
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "1F3864" }),
                new Paragraph(new Run(
                    new RunProperties(new Bold(), new Color { Val = "FFFFFF" }, new FontSize { Val = "17" }),
                    new Text(title)))));
        }
        tbl.AppendChild(hdr);

        var verdicts = new[] {"OUI","NON","EXONÉR","SOUMIS","DÉDUCTIBL","NON DÉDUCTIBL","SUSPENDU"};
        for (int i = 0; i < rows.Count; i++)
        {
            var row  = rows[i];
            var fill = i % 2 == 0 ? "EBF2FA" : "FFFFFF";
            var tr   = new TableRow();

            void AddCell(string text, int pct, bool bold)
            {
                var w = ((int)(9640 * pct / 100.0)).ToString();
                tr.AppendChild(new TableCell(
                    new TableCellProperties(
                        new TableCellWidth { Width = w, Type = TableWidthUnitValues.Dxa },
                        new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill }),
                    new Paragraph(new Run(
                        new RunProperties(bold ? new Bold() : null!, new FontSize { Val = "16" }),
                        new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
            }

            var conclusionBold = verdicts.Any(v => row.Conclusion.ToUpper().StartsWith(v));
            AddCell((i + 1).ToString(), 5, true);
            AddCell(row.Sujet,          20, true);
            AddCell(row.Analyse,        50, false);
            AddCell(row.Conclusion,     25, conclusionBold);
            tbl.AppendChild(tr);
        }
        return tbl;
    }

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
