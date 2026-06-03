using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiscalPlatform.Application.Common.Interfaces.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FiscalPlatform.Infrastructure.Agents;

public sealed class LlmAgent(IConfiguration config, ILogger<LlmAgent> logger) : ILlmAgent
{
    private readonly string _model      = (config["OpenAI:ChatModel"]  is { Length: > 0 } m)  ? m  : (Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL")  ?? "gpt-4o");
    private readonly string _apiKey     = (config["OpenAI:ApiKey"]     is { Length: > 0 } k)  ? k  : (Environment.GetEnvironmentVariable("OPENAI_API_KEY")     ?? "");
    private readonly string _endpoint   = (config["OpenAI:Endpoint"]   is { Length: > 0 } e)  ? e  : (Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")    ?? "");
    private readonly string _apiVersion = (config["OpenAI:ApiVersion"] is { Length: > 0 } av) ? av : (Environment.GetEnvironmentVariable("OPENAI_API_VERSION") ?? "2024-02-15-preview");
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(4) };

    public Task<string?> CompleteAsync(string sys, string user, string label,
        int maxTokens = 3000, CancellationToken ct = default) =>
        SendAsync(sys, new[] { ("user", user) }, label, ct);

    public Task<string?> ChatAsync(IEnumerable<(string Role, string Content)> history,
        string sys, CancellationToken ct = default) =>
        SendAsync(sys, history, "Chat", ct);

    private async Task<string?> SendAsync(
        string sys,
        IEnumerable<(string Role, string Content)> msgs,
        string label,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            logger.LogError("[{L}] API key missing", label);
            return null;
        }

        // Strip any accidental quotes/spaces from api version (DotNetEnv quirk)
        var apiVersion = _apiVersion.Trim().Trim('"').Trim('\'').Trim();

        var url = _endpoint.TrimEnd('/') +
                  $"/openai/deployments/{_model}/chat/completions?api-version={apiVersion}";

        // Build messages array — system first, then conversation
        var allMsgs = new object[] { new { role = "system", content = sys } }
            .Concat(msgs.Select(m => (object)new { role = m.Role, content = m.Content }))
            .ToArray();

        // Match old project exactly: only messages + temperature, no max_tokens
        // The EY Azure endpoint rejects requests with max_tokens parameter
        var body = JsonSerializer.Serialize(new
        {
            messages    = allMsgs,
            temperature = 0,
        });

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(3));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                logger.LogInformation("  [{L}] attempt {A}/2 — {C} chars",
                    label, attempt, body.Length);

                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add("api-key", _apiKey);

                var resp = await _http.SendAsync(req, cts.Token);
                var rb   = await resp.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                {
                    var preview = rb.Length > 300 ? rb[..300] : rb;
                    logger.LogError("  [{L}] HTTP {S} ({Ms:F0}ms): {B}",
                        label, (int)resp.StatusCode, sw.Elapsed.TotalMilliseconds, preview);

                    if ((int)resp.StatusCode is 401 or 403)
                    {
                        logger.LogError("  [{L}] Auth failure — check OPENAI_API_KEY", label);
                        return null;
                    }

                    if (attempt < 2) { await Task.Delay(5000, ct); continue; }
                    return null;
                }

                using var doc = JsonDocument.Parse(rb);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()?.Trim();

                var finish = doc.RootElement
                    .GetProperty("choices")[0]
                    .TryGetProperty("finish_reason", out var fr)
                    ? fr.GetString() ?? "" : "";

                logger.LogInformation("  [{L}] ✓ {Ms:F0}ms | {N} chars | finish={F}",
                    label, sw.Elapsed.TotalMilliseconds, text?.Length ?? 0, finish);

                if (finish == "length")
                    logger.LogWarning("  [{L}] ⚠ response was truncated", label);

                return text;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                logger.LogWarning("  [{L}] timeout {A}/2 ({Ms:F0}ms)",
                    label, attempt, sw.Elapsed.TotalMilliseconds);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                logger.LogError("  [{L}] connection error ({Ms:F0}ms): {M}",
                    label, sw.Elapsed.TotalMilliseconds, ex.Message);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError("  [{L}] error ({Ms:F0}ms): {M}",
                    label, sw.Elapsed.TotalMilliseconds,
                    ex.Message[..Math.Min(ex.Message.Length, 200)]);
                if (attempt < 2) await Task.Delay(5000, ct);
            }
        }

        logger.LogError("  [{L}] all attempts failed", label);
        return null;
    }
}
