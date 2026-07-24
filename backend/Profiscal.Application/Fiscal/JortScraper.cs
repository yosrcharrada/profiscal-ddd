using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Profiscal.Application.Fiscal;

/// <summary>One text (loi / décret / arrêté / avis) of the latest JORT issue, in French.</summary>
public record ScrapedActivity(
    string Title, string Description, string DateText, DateTime? PublishedOn, string Category, string Hash);

public interface IJortScraper
{
    Task<IReadOnlyList<ScrapedActivity>> ScrapeAsync(CancellationToken ct = default);

    /// <summary>
    /// Mint a fresh, shareable IORT deep link **in French**. The site's URLs carry a
    /// per-session CTX token, so we open a brand-new session server-side, switch the
    /// language to French, navigate to the wanted page and hand back its URL — that URL
    /// then works cookie-less in the user's browser.
    /// Targets: "latest" (PageDernierParu — Journal officiel: lois, décrets, arrêtés et avis)
    /// or "home" (French Principal page).
    /// </summary>
    Task<string?> GetLiveLinkAsync(string target = "latest", CancellationToken ct = default);

    /// <summary>
    /// Download the official PDF of one text of the latest JORT issue, located by its
    /// French title. Returns null when the text is no longer on the "dernier paru" page
    /// (a newer issue replaced it) or the download failed.
    /// </summary>
    Task<byte[]?> GetPdfAsync(string title, CancellationToken ct = default);
}

/// <summary>
/// Scrapes the latest Journal Officiel (JORT) issue from iort.gov.tn — in FRENCH.
/// The site is a WinDev/WebDev app whose URLs carry a per-session token, so we
/// replay its navigation over plain HTTP every run:
///
///   1. GET  /WD120AWP/WD120Awp.exe/CONNECT/SITEIORT   → Arabic "Principal" page.
///   2. POST WD_BUTTON_CLICK_=M32 ("Français")          → French Principal page.
///   3. POST WD_BUTTON_CLICK_=M7  ("Journal officiel (lois, décrets, arrêtés et avis)")
///      → PageDernierParu: the latest issue, one row per text in zone A12.
///
/// Each row N carries: div _N_A3 (ministry, blank = same as previous row), an
/// A2 anchor (the text's French title) and an A6 anchor ("PDF-texte") which,
/// posted with A12=N, streams the official PDF. The issue date sits in the
/// hidden input M9 (dd/MM/yyyy). Pages are ISO-8859-1 encoded.
/// </summary>
public partial class JortScraper(ILogger<JortScraper> logger) : IJortScraper
{
    private const string BaseUrl       = "http://www.iort.gov.tn";
    private const string ConnectUrl    = BaseUrl + "/WD120AWP/WD120Awp.exe/CONNECT/SITEIORT";
    private const string FrenchButton  = "M32"; // "Français"
    private const string JortButton    = "M7";  // "Journal officiel (lois, décrets, arrêtés et avis)"
    private const string PdfButton     = "A6";  // "PDF-texte" (with A12=<row>)

    /* ───────────────────────── session replay ───────────────────────── */

    private static HttpClient NewClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ProfiscalBot/1.0; +tax platform)");
        return http;
    }

    /// <summary>WebDev pages declare iso-8859-1 (French) or UTF-8 (Arabic) — decode accordingly.</summary>
    private static string Decode(byte[] bytes)
    {
        var probe = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 600));
        return probe.Contains("utf-8", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(bytes)
            : Encoding.Latin1.GetString(bytes);
    }

    private static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match m in HiddenInputRegex().Matches(html))
            fields[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;
        return fields;
    }

    private static string? FormAction(string html)
    {
        var m = AnyFormActionRegex().Match(html);
        return m.Success ? m.Groups[2].Value : null;
    }

    private static async Task<(string Html, byte[] Raw, string? ContentType)> ClickAsync(
        HttpClient http, string actionUrl, string pageHtml, string button,
        Dictionary<string, string>? extra, CancellationToken ct)
    {
        var fields = HiddenFields(pageHtml);
        fields["WD_BUTTON_CLICK_"] = button;
        fields["WD_ACTION_"]       = string.Empty;
        if (extra is not null)
            foreach (var (k, v) in extra) fields[k] = v;

        using var resp = await http.PostAsync(actionUrl, new FormUrlEncodedContent(fields), ct);
        var raw = await resp.Content.ReadAsByteArrayAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        var html = contentType == "application/pdf" ? string.Empty : Decode(raw);
        return (html, raw, contentType);
    }

    /// <summary>connect → Français → Journal officiel. Returns the French PageDernierParu html + its action url (+ the French Principal action url).</summary>
    private static async Task<(string JortHtml, string JortAction, string PrincipalAction)?> OpenFrenchJortAsync(
        HttpClient http, CancellationToken ct)
    {
        var connectRaw  = await http.GetByteArrayAsync(ConnectUrl, ct);
        var connectHtml = Decode(connectRaw);
        var action = FormAction(connectHtml);
        if (action is null) return null;

        var (frHtml, _, _) = await ClickAsync(http, BaseUrl + action, connectHtml, FrenchButton, null, ct);
        var frAction = FormAction(frHtml);
        if (frAction is null) return null;

        var (jortHtml, _, _) = await ClickAsync(http, BaseUrl + frAction, frHtml, JortButton, null, ct);
        var jortAction = FormAction(jortHtml);
        if (jortAction is null) return null;

        return (jortHtml, BaseUrl + jortAction, BaseUrl + frAction);
    }

    /* ───────────────────────── scraping ───────────────────────── */

    public async Task<IReadOnlyList<ScrapedActivity>> ScrapeAsync(CancellationToken ct = default)
    {
        using var http = NewClient();
        var opened = await OpenFrenchJortAsync(http, ct);
        if (opened is null)
        {
            logger.LogWarning("JORT scrape: couldn't reach the French Journal officiel page");
            return [];
        }

        var (html, _, _) = opened.Value;
        var rows = ParseIssue(html);
        logger.LogInformation("JORT scrape: {Count} texts collected from the latest French issue", rows.Count);
        return rows;
    }

    /// <summary>Parse every text row of the PageDernierParu issue.</summary>
    private static List<ScrapedActivity> ParseIssue(string html)
    {
        // Issue date lives in the (disabled) text input M9 (dd/MM/yyyy).
        var dateText = IssueDateRegex().Match(html) is { Success: true } dm ? dm.Groups[1].Value : string.Empty;
        DateTime? published = DateTime.TryParseExact(
            dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

        // Ministries: _N_A3 divs; blank = same ministry as the previous row.
        var ministries = new Dictionary<int, string>();
        foreach (Match m in MinistryRegex().Matches(html))
            if (int.TryParse(m.Groups[1].Value, out var n))
                ministries[n] = Clean(m.Groups[2].Value);

        var items = new List<ScrapedActivity>();
        var currentMinistry = string.Empty;
        foreach (Match m in TitleAnchorRegex().Matches(html))
        {
            if (!int.TryParse(m.Groups[1].Value, out var row)) continue;
            var title = Clean(m.Groups[2].Value);
            if (title.Length == 0) continue;

            var ministry = ministries.GetValueOrDefault(row, string.Empty);
            if (ministry.Length > 0) currentMinistry = ministry;

            items.Add(new ScrapedActivity(
                title, currentMinistry, dateText, published,
                Categorize(title),
                Sha256($"{title}|{dateText}")));
        }
        return items;
    }

    /// <summary>Category from the French title's opening words.</summary>
    private static string Categorize(string title)
    {
        var t = title.TrimStart();
        if (t.StartsWith("Loi",      StringComparison.OrdinalIgnoreCase)) return "Loi";
        if (t.StartsWith("Décret",   StringComparison.OrdinalIgnoreCase)) return "Decret";
        if (t.StartsWith("Arrêté",   StringComparison.OrdinalIgnoreCase)) return "Arrete";
        if (t.StartsWith("Avis",     StringComparison.OrdinalIgnoreCase)) return "Avis";
        if (t.StartsWith("Décision", StringComparison.OrdinalIgnoreCase)) return "Decision";
        return "Autre";
    }

    /* ───────────────────────── live links & PDFs ───────────────────────── */

    public async Task<string?> GetLiveLinkAsync(string target = "latest", CancellationToken ct = default)
    {
        try
        {
            using var http = NewClient();
            var opened = await OpenFrenchJortAsync(http, ct);
            if (opened is null) return null;
            return string.Equals(target, "home", StringComparison.OrdinalIgnoreCase)
                ? opened.Value.PrincipalAction
                : opened.Value.JortAction;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JORT live-link mint failed for target {Target}", target);
            return null;
        }
    }

    public async Task<byte[]?> GetPdfAsync(string title, CancellationToken ct = default)
    {
        try
        {
            using var http = NewClient();
            var opened = await OpenFrenchJortAsync(http, ct);
            if (opened is null) return null;
            var (html, action, _) = opened.Value;

            // Locate the row whose title matches the stored activity.
            var wanted = Clean(title);
            int rowIndex = -1;
            foreach (Match m in TitleAnchorRegex().Matches(html))
            {
                if (Clean(m.Groups[2].Value).Equals(wanted, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(m.Groups[1].Value, out var n))
                {
                    rowIndex = n;
                    break;
                }
            }
            if (rowIndex < 0)
            {
                logger.LogInformation("JORT pdf: text not on the current issue page — {Title}", wanted);
                return null;
            }

            var (_, raw, contentType) = await ClickAsync(http, action, html, PdfButton,
                new Dictionary<string, string> { ["A12"] = rowIndex.ToString() }, ct);

            if (contentType == "application/pdf" || (raw.Length > 4 && raw[0] == '%' && raw[1] == 'P'))
                return raw;

            logger.LogWarning("JORT pdf: unexpected response ({Type}, {Len} bytes) for row {Row}", contentType, raw.Length, rowIndex);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JORT pdf download failed for {Title}", title);
            return null;
        }
    }

    /* ───────────────────────── helpers ───────────────────────── */

    private static string Clean(string s)
    {
        var noTags  = Regex.Replace(s, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags).Replace(' ', ' ');
        // The site emits Windows-1252 numeric refs (&#145;–&#151;) that .NET decodes to
        // C1 control chars — map them back to their real punctuation ("d’un", "–" …).
        var sb = new StringBuilder(decoded.Length);
        foreach (var ch in decoded)
            sb.Append(ch switch
            {
                '\u0091' => '‘',
                '\u0092' => '’',
                '\u0093' => '“',
                '\u0094' => '”',
                '\u0096' => '–',
                '\u0097' => '—',
                '\u0085' => '…',
                _ => ch
            });
        return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [GeneratedRegex("<INPUT TYPE=HIDDEN NAME=([A-Z_0-9]+)(?:\\s+VALUE=\"([^\"]*)\")?", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInputRegex();

    [GeneratedRegex("FORM NAME=(\\w+) ACTION=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AnyFormActionRegex();

    /// <summary>The A2 anchor of one row: javascript:_PAGE_.A12.value=N;… followed by the title.</summary>
    [GeneratedRegex("name=A2 href=\"javascript:_PAGE_\\.A12\\.value=(\\d+);[^\"]*\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex TitleAnchorRegex();

    /// <summary>Ministry heading div of one row: _N_A3.</summary>
    [GeneratedRegex("<div id=\"_(\\d+)_A3\">(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex MinistryRegex();

    /// <summary>The issue date field: &lt;INPUT TYPE=TEXT … NAME=M9 VALUE="dd/MM/yyyy" …&gt;.</summary>
    [GeneratedRegex("NAME=M9\\s+VALUE=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex IssueDateRegex();
}
