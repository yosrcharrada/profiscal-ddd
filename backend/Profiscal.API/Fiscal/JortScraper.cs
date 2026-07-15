using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Profiscal.API.Fiscal;

/// <summary>One notice as scraped from the IORT actualité feed.</summary>
public record ScrapedActivity(
    string Title, string Description, string DateText, DateTime? PublishedOn, string Category, string Hash);

public interface IJortScraper
{
    Task<IReadOnlyList<ScrapedActivity>> ScrapeAsync(CancellationToken ct = default);
}

/// <summary>
/// Scrapes the live "جميع المستجدات" (all updates) list from the Tunisian
/// Official Gazette site (iort.gov.tn). That site is a WinDev/WebDev app whose
/// URLs carry a per-session token, so we can't hardcode a link — instead we
/// replay its navigation over plain HTTP every run:
///
///   1. GET  /WD120AWP/WD120Awp.exe/CONNECT/SITEIORT  → a fresh "Principal"
///      page containing a form (with a new CTX session) and hidden fields.
///   2. POST that form with WD_BUTTON_CLICK_=A21       → the actualité list.
///   3. GET  the list's SYNC url with ?WD_ACTION_=SCROLLTABLE&amp;ZR_ACTUALITE=N
///      to page through the remaining items.
///
/// Each notice is rendered as three divs: _N_A6 (title), _N_A8 (date dd/MM/yyyy)
/// and _N_A7 (description). We extract those and dedupe by a content hash.
/// </summary>
public partial class JortScraper(ILogger<JortScraper> logger) : IJortScraper
{
    private const string BaseUrl    = "http://www.iort.gov.tn";
    private const string ConnectUrl = BaseUrl + "/WD120AWP/WD120Awp.exe/CONNECT/SITEIORT";
    private const string UpdatesButton = "A21"; // "جميع المستجدات"

    public async Task<IReadOnlyList<ScrapedActivity>> ScrapeAsync(CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ProfiscalBot/1.0; +tax platform)");

        // 1. Entry point → Principal page (fresh session + form).
        var connectHtml = await http.GetStringAsync(ConnectUrl, ct);
        var action = PrincipalFormRegex().Match(connectHtml);
        if (!action.Success)
        {
            logger.LogWarning("JORT scrape: PRINCIPAL form not found on connect page");
            return [];
        }

        // Collect the form's hidden inputs, then simulate the "all updates" click.
        var fields = new Dictionary<string, string>();
        foreach (Match m in HiddenInputRegex().Matches(connectHtml))
            fields[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;
        fields["WD_BUTTON_CLICK_"] = UpdatesButton;
        fields["WD_ACTION_"]       = string.Empty;

        // 2. POST → actualité list (page 1).
        var actionUrl = BaseUrl + action.Groups[1].Value;
        using var resp = await http.PostAsync(actionUrl, new FormUrlEncodedContent(fields), ct);
        var html = await resp.Content.ReadAsStringAsync(ct);

        var results = new Dictionary<string, ScrapedActivity>();
        ParsePage(html, results);

        // 3. Follow pagination until a page adds nothing new (or we hit a safety cap).
        var syncUrl = ActualiteSyncRegex().Match(html);
        if (syncUrl.Success)
        {
            for (var offset = 4; offset <= 400; offset += 4)
            {
                ct.ThrowIfCancellationRequested();
                var before = results.Count;
                var pageUrl = $"{BaseUrl}{syncUrl.Groups[1].Value}?WD_ACTION_=SCROLLTABLE&ZR_ACTUALITE={offset}";
                string pageHtml;
                try { pageHtml = await http.GetStringAsync(pageUrl, ct); }
                catch (Exception ex) { logger.LogDebug(ex, "JORT pagination stopped at offset {Offset}", offset); break; }
                ParsePage(pageHtml, results);
                if (results.Count == before) break;
            }
        }

        logger.LogInformation("JORT scrape: {Count} activities collected", results.Count);
        return results.Values.ToList();
    }

    /// <summary>Extract every _N_A6/_A7/_A8 triple from one rendered page.</summary>
    private static void ParsePage(string html, Dictionary<string, ScrapedActivity> acc)
    {
        var titles = FieldsBySuffix(html, "A6");
        var dates  = FieldsBySuffix(html, "A8");
        var descs  = FieldsBySuffix(html, "A7");

        foreach (var (idx, rawTitle) in titles)
        {
            var title = Clean(rawTitle);
            if (title.Length == 0) continue;

            var dateText = Clean(dates.GetValueOrDefault(idx, string.Empty));
            var desc     = Clean(descs.GetValueOrDefault(idx, string.Empty));

            DateTime? published = DateTime.TryParseExact(
                dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : null;

            var hash = Sha256($"{title}|{dateText}|{desc}");
            acc[hash] = new ScrapedActivity(title, desc, dateText, published, Categorize(title, desc), hash);
        }
    }

    private static Dictionary<int, string> FieldsBySuffix(string html, string suffix)
    {
        var dict = new Dictionary<int, string>();
        foreach (Match m in Regex.Matches(html, $"<div id=\"_(\\d+)_{suffix}\">(.*?)</div>", RegexOptions.Singleline))
            if (int.TryParse(m.Groups[1].Value, out var n)) dict[n] = m.Groups[2].Value;
        return dict;
    }

    /// <summary>Best-effort classification from Arabic keywords in the notice.</summary>
    private static string Categorize(string title, string desc)
    {
        var text = title + " " + desc;
        if (text.Contains("طلب عروض") || text.Contains("استشارة")) return "Tender";
        if (text.Contains("المخطط"))                              return "Plan";
        if (text.Contains("إصدار")   || text.Contains("النصوص"))  return "Publication";
        return "Notice";
    }

    private static string Clean(string s)
    {
        var noTags  = Regex.Replace(s, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags).Replace(' ', ' ');
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [GeneratedRegex("FORM NAME=PRINCIPAL ACTION=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PrincipalFormRegex();

    [GeneratedRegex("<INPUT TYPE=HIDDEN NAME=([A-Z_0-9]+)(?:\\s+VALUE=\"([^\"]*)\")?", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInputRegex();

    [GeneratedRegex("(/WD120AWP/WD120Awp\\.exe/CTX_[^/\"]+/Actualite/SYNC_-?\\d+)")]
    private static partial Regex ActualiteSyncRegex();
}
